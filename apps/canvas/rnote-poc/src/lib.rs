//! Approved migration proof of concept for a renderer-neutral Rnote core.
//!
//! This crate deliberately depends on `rnote-engine` with default features disabled.
//! It must not enable Rnote's `ui` feature or depend on GTK/Libadwaita.

pub mod ffi;

use std::collections::HashSet;
use std::fs::{self, File};
use std::path::Path;
use std::time::{Instant, SystemTime, UNIX_EPOCH};

use anyhow::{Context, Result};
use nalgebra::Vector2;
use rnote_compose::penevent::PenEvent;
use rnote_compose::penpath::Element;
use rnote_compose::utils::{add_xml_header, wrap_svg_root};
use rnote_compose::builders::PenPathBuilderType;
use rnote_compose::builders::ShapeBuilderType;
use rnote_compose::Color;
use rnote_engine::document::Layout;
use rnote_engine::document::background::PatternStyle;
use rnote_engine::engine::export::{DocExportFormat, DocExportPrefs, SelectionExportFormat, SelectionExportPrefs};
use rnote_engine::engine::{EngineConfig, EngineConfigShared};
use rnote_engine::engine::EngineSnapshot;
use rnote_engine::pens::pensconfig::brushconfig::BrushStyle;
use rnote_engine::pens::pensconfig::eraserconfig::EraserStyle;
use rnote_engine::pens::pensconfig::selectorconfig::SelectorStyle;
use rnote_engine::pens::pensconfig::shaperconfig::ShaperStyle;
use rnote_engine::pens::pensconfig::toolsconfig::ToolStyle;
use rnote_engine::pens::{PenMode, PenStyle};
use rnote_engine::Engine;

/// One normalized CakeOS/HUI pointer sample in Canvas document coordinates.
///
/// Tilt remains in the CakeOS boundary because Rnote 0.14.2 only accepts position
/// and pressure in its core `Element` type. Keeping tilt here prevents a lossy
/// public API if the engine is extended later.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasPointerSample {
    pub x: f64,
    pub y: f64,
    pub pressure: f64,
    pub tilt_x: f64,
    pub tilt_y: f64,
}

impl CanvasPointerSample {
    pub fn new(x: f64, y: f64, pressure: f64) -> Self {
        Self {
            x,
            y,
            pressure: pressure.clamp(0.0, 1.0),
            tilt_x: 0.0,
            tilt_y: 0.0,
        }
    }

    fn rnote_element(self) -> Element {
        Element::new(Vector2::new(self.x, self.y), self.pressure)
    }
}

/// Rnote tool styles exposed at the CakeOS boundary.
///
/// Order mirrors Rnote's pens sidebar: brush (pen + highlighter share the
/// brush pen), shaper, typewriter, eraser, selector, tools.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasTool {
    #[default]
    Pen,
    Highlighter,
    Eraser,
    Selector,
    Shape,
    Typewriter,
    Tools,
}

impl CanvasTool {
    fn rnote_pen_style(self) -> PenStyle {
        match self {
            Self::Pen | Self::Highlighter => PenStyle::Brush,
            Self::Eraser => PenStyle::Eraser,
            Self::Selector => PenStyle::Selector,
            Self::Shape => PenStyle::Shaper,
            Self::Typewriter => PenStyle::Typewriter,
            Self::Tools => PenStyle::Tools,
        }
    }
}

/// Rnote brush ink styles (the Pen/Highlighter pair shares the brush pen).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasBrushStyle {
    Marker,
    #[default]
    Solid,
    Textured,
}

/// Rnote pen-path builders for the brush pen.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasBrushBuilder {
    Simple,
    Curved,
    #[default]
    Modeled,
}

impl CanvasBrushBuilder {
    fn rnote_builder(self) -> PenPathBuilderType {
        match self {
            Self::Simple => PenPathBuilderType::Simple,
            Self::Curved => PenPathBuilderType::Curved,
            Self::Modeled => PenPathBuilderType::Modeled,
        }
    }
}

/// Rnote shaper render styles.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasShaperStyle {
    #[default]
    Smooth,
    Rough,
}

/// Rnote eraser modes.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasEraserStyle {
    #[default]
    TrashColliding,
    SplitColliding,
}

/// Rnote selector capture styles.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasSelectorStyle {
    Polygon,
    #[default]
    Rectangle,
    Single,
    IntersectingPath,
}

/// Rnote utility tool styles.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasToolsStyle {
    #[default]
    VerticalSpace,
    OffsetCamera,
    Zoom,
    Laser,
}

/// Rnote document layouts.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasLayout {
    FixedSize,
    ContinuousVertical,
    SemiInfinite,
    #[default]
    Infinite,
}

/// Rnote background patterns.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasPattern {
    None,
    Lines,
    Grid,
    #[default]
    Dots,
    IsometricGrid,
    IsometricDots,
}

/// Document export formats (subset of Rnote's doc export).
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasDocExportFormat {
    #[default]
    Svg,
    Pdf,
    Xopp,
}

/// RGBA colour at the CakeOS boundary. Components are clamped to [0, 1].
#[derive(Debug, Clone, Copy, PartialEq, Default)]
pub struct CanvasRgba {
    pub r: f64,
    pub g: f64,
    pub b: f64,
    pub a: f64,
}

impl CanvasRgba {
    pub fn new(r: f64, g: f64, b: f64, a: f64) -> Self {
        fn clamp01(v: f64) -> f64 {
            if !v.is_finite() { 0.0 } else { v.clamp(0.0, 1.0) }
        }
        Self { r: clamp01(r), g: clamp01(g), b: clamp01(b), a: clamp01(a) }
    }

    fn rnote_color(self) -> Color {
        Color::new(self.r, self.g, self.b, self.a)
    }

    fn from_rnote(color: Color) -> Self {
        Self::new(color.r, color.g, color.b, color.a)
    }
}

/// Page format dimensions at the CakeOS boundary.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasFormatSize {
    pub width: f64,
    pub height: f64,
    pub dpi: f64,
}

/// Rnote's concrete shape builders. The shape pen is not emulated by CakeOS.
///
/// ABI note: the first four discriminants preserve the v2 C ABI numbering
/// (Rectangle=0, Ellipse=1, Line=2, Arrow=3). The remaining builders follow in
/// Rnote donor order.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasShape {
    #[default]
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Grid,
    CoordSystem2D,
    CoordSystem3D,
    QuadrantCoordSystem2D,
    FociEllipse,
    QuadBez,
    CubBez,
    Polyline,
    Polygon,
}

impl CanvasShape {
    fn rnote_builder(self) -> ShapeBuilderType {
        match self {
            Self::Rectangle => ShapeBuilderType::Rectangle,
            Self::Ellipse => ShapeBuilderType::Ellipse,
            Self::Line => ShapeBuilderType::Line,
            Self::Arrow => ShapeBuilderType::Arrow,
            Self::Grid => ShapeBuilderType::Grid,
            Self::CoordSystem2D => ShapeBuilderType::CoordSystem2D,
            Self::CoordSystem3D => ShapeBuilderType::CoordSystem3D,
            Self::QuadrantCoordSystem2D => ShapeBuilderType::QuadrantCoordSystem2D,
            Self::FociEllipse => ShapeBuilderType::FociEllipse,
            Self::QuadBez => ShapeBuilderType::QuadBez,
            Self::CubBez => ShapeBuilderType::CubBez,
            Self::Polyline => ShapeBuilderType::Polyline,
            Self::Polygon => ShapeBuilderType::Polygon,
        }
    }
}

/// Camera state expressed in Canvas document coordinates.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasViewport {
    pub center_x: f64,
    pub center_y: f64,
    pub zoom: f64,
}

/// Coordinate space used by all stable Canvas bridge geometry in this PoC.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CanvasCoordinateSpace {
    Document,
}

/// Renderer-neutral format exposed to HUI.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CanvasRenderFormat {
    Svg,
}

impl CanvasRenderFormat {
    pub const fn mime_type(self) -> &'static str {
        match self {
            Self::Svg => "image/svg+xml",
        }
    }
}

/// Document-space bounds represented by a render frame.
///
/// Rnote normalizes exported SVG coordinates to a zero-based viewBox. These
/// bounds retain the corresponding original Canvas document-space rectangle so
/// HUI can map input and viewport state to the normalized renderer payload.
#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CanvasDocumentBounds {
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
}

/// Stable renderer-neutral payload for HUI composition.
///
/// HUI owns viewport pan/zoom and surface-to-document transforms. The bytes are
/// intentionally whole-document SVG for this first slice; dirty-region/tile
/// output is a later performance boundary.
#[derive(Debug, Clone, PartialEq)]
pub struct CanvasRenderFrame {
    pub format: CanvasRenderFormat,
    pub coordinate_space: CanvasCoordinateSpace,
    pub bounds: CanvasDocumentBounds,
    pub bytes: Vec<u8>,
}

impl CanvasRenderFrame {
    pub const fn mime_type(&self) -> &'static str {
        self.format.mime_type()
    }
}

/// Smallest engine boundary intended to prove that Rnote can run beneath HUI
/// without embedding `rnote-ui`.
#[derive(Debug)]
pub struct HeadlessCanvasEngine {
    engine: Engine,
    stroke_active: bool,
    tool: CanvasTool,
    shape: CanvasShape,
    brush_style: CanvasBrushStyle,
    config: EngineConfigShared,
}

impl Default for HeadlessCanvasEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl HeadlessCanvasEngine {
    pub fn new() -> Self {
        let mut engine = Engine::default();
        let config = EngineConfigShared::from(EngineConfig::default());
        // Keep Rnote's configuration private to the engine while retaining the
        // shared handle needed to select its real marker and shape builders.
        let _ = engine.install_config(&config, None);
        let mut canvas = Self {
            engine,
            stroke_active: false,
            tool: CanvasTool::Pen,
            shape: CanvasShape::Rectangle,
            brush_style: CanvasBrushStyle::Solid,
            config,
        };
        canvas.configure_tool();
        canvas
    }

    fn send_pen_event(&mut self, event: PenEvent) {
        let _ = self.engine.handle_pen_event(event, Some(PenMode::Pen), Instant::now());
    }

    pub fn tool(&self) -> CanvasTool {
        self.tool
    }

    /// Change the tool used by subsequent stroke events. Tool changes are kept
    /// outside an active stroke so one gesture cannot change interpretation midway.
    pub fn set_tool(&mut self, tool: CanvasTool) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas stroke tool while a stroke is active");
        }
        self.tool = tool;
        self.configure_tool();
        Ok(())
    }

    /// Select the builder used by Rnote's shape pen.
    pub fn set_shape(&mut self, shape: CanvasShape) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas shape while a stroke is active");
        }
        self.shape = shape;
        if self.tool == CanvasTool::Shape {
            self.configure_tool();
        }
        Ok(())
    }

    fn configure_tool(&mut self) {
        {
            let mut config = self.config.write();
            // The Highlighter tool is Rnote's marker brush variant; the Pen tool
            // honours the configured brush style (Solid/Textured/Marker).
            config.pens_config.brush_config.style = match self.tool {
                CanvasTool::Highlighter => BrushStyle::Marker,
                CanvasTool::Pen => match self.brush_style {
                    CanvasBrushStyle::Marker => BrushStyle::Marker,
                    CanvasBrushStyle::Solid => BrushStyle::Solid,
                    CanvasBrushStyle::Textured => BrushStyle::Textured,
                },
                _ => config.pens_config.brush_config.style,
            };
            config.pens_config.shaper_config.builder_type = self.shape.rnote_builder();
        }
        let _ = self.engine.change_pen_mode(PenMode::Pen);
        let _ = self.engine.change_pen_style(self.tool.rnote_pen_style());
    }

    /// Begin a freehand stroke. Device arbitration (mouse/touch/stylus/palm
    /// rejection) intentionally stays above this adapter in HUI.
    pub fn begin_stroke(&mut self, sample: CanvasPointerSample) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("a Canvas stroke is already active");
        }

        self.send_pen_event(PenEvent::Down {
            element: sample.rnote_element(),
            modifier_keys: HashSet::new(),
        });
        self.stroke_active = true;
        Ok(())
    }

    /// Append a pressure-sensitive sample to the active stroke.
    pub fn update_stroke(&mut self, sample: CanvasPointerSample) -> Result<()> {
        if !self.stroke_active {
            anyhow::bail!("cannot update a Canvas stroke before begin_stroke");
        }

        self.send_pen_event(PenEvent::Down {
            element: sample.rnote_element(),
            modifier_keys: HashSet::new(),
        });
        Ok(())
    }

    /// Finalize the active stroke at the supplied release sample.
    pub fn end_stroke(&mut self, sample: CanvasPointerSample) -> Result<()> {
        if !self.stroke_active {
            anyhow::bail!("cannot end a Canvas stroke before begin_stroke");
        }

        self.send_pen_event(PenEvent::Up {
            element: sample.rnote_element(),
            modifier_keys: HashSet::new(),
        });
        self.stroke_active = false;
        Ok(())
    }

    pub fn stroke_active(&self) -> bool {
        self.stroke_active
    }

    pub fn can_undo(&self) -> bool {
        self.engine.can_undo()
    }

    pub fn can_redo(&self) -> bool {
        self.engine.can_redo()
    }

    /// Undo one completed Canvas operation. History changes are rejected while a
    /// stroke is active so HUI cannot create an ambiguous partial-stroke state.
    pub fn undo(&mut self) -> Result<bool> {
        if self.stroke_active {
            anyhow::bail!("cannot undo while a Canvas stroke is active");
        }
        if !self.engine.can_undo() {
            return Ok(false);
        }

        let _ = self.engine.undo(Instant::now());
        Ok(true)
    }

    /// Redo one completed Canvas operation. History changes are rejected while a
    /// stroke is active for the same lifecycle reason as `undo`.
    pub fn redo(&mut self) -> Result<bool> {
        if self.stroke_active {
            anyhow::bail!("cannot redo while a Canvas stroke is active");
        }
        if !self.engine.can_redo() {
            return Ok(false);
        }

        let _ = self.engine.redo(Instant::now());
        Ok(true)
    }

    /// Convenience helper for tests/importers that already have a complete stroke.
    pub fn draw_stroke(&mut self, samples: &[CanvasPointerSample]) -> Result<()> {
        if samples.len() < 2 {
            anyhow::bail!("a stroke requires at least two pointer samples");
        }

        self.begin_stroke(samples[0])?;
        for sample in &samples[1..samples.len() - 1] {
            self.update_stroke(*sample)?;
        }
        self.end_stroke(*samples.last().expect("length checked above"))
    }

    /// Current storage/document extent. This is intentionally distinct from a
    /// render frame's content/page bounds in Rnote Infinite layout.
    pub fn document_bounds(&self) -> CanvasDocumentBounds {
        CanvasDocumentBounds {
            x: self.engine.document.x,
            y: self.engine.document.y,
            width: self.engine.document.width,
            height: self.engine.document.height,
        }
    }

    /// Change the Rnote camera's viewport size without affecting document coordinates.
    pub fn set_viewport_size(&mut self, width: f64, height: f64) -> Result<()> {
        if !width.is_finite() || !height.is_finite() || width <= 0.0 || height <= 0.0 {
            anyhow::bail!("Canvas viewport size must be finite and positive");
        }
        let _ = self.engine.camera.set_size(Vector2::new(width, height));
        Ok(())
    }

    /// Set the real Rnote camera zoom. Rnote clamps supported zoom bounds itself.
    pub fn zoom_to(&mut self, zoom: f64) -> Result<()> {
        if !zoom.is_finite() || zoom <= 0.0 {
            anyhow::bail!("Canvas zoom must be finite and positive");
        }
        let _ = self.engine.camera.zoom_to(zoom);
        Ok(())
    }

    /// Pan the real Rnote camera in Canvas document coordinates.
    pub fn pan_by(&mut self, delta_x: f64, delta_y: f64) -> Result<()> {
        if !delta_x.is_finite() || !delta_y.is_finite() {
            anyhow::bail!("Canvas pan deltas must be finite");
        }
        let center = self.engine.camera.viewport_center();
        let _ = self.engine.camera.set_viewport_center(center + Vector2::new(delta_x, delta_y));
        Ok(())
    }

    pub fn set_viewport_center(&mut self, center_x: f64, center_y: f64) -> Result<()> {
        if !center_x.is_finite() || !center_y.is_finite() {
            anyhow::bail!("Canvas viewport center must be finite");
        }
        let _ = self.engine.camera.set_viewport_center(Vector2::new(center_x, center_y));
        Ok(())
    }

    pub fn viewport(&self) -> CanvasViewport {
        let center = self.engine.camera.viewport_center();
        CanvasViewport {
            center_x: center.x,
            center_y: center.y,
            zoom: self.engine.camera.zoom(),
        }
    }

    // ---- Rnote donor-parity configuration (Phase 1) ----
    //
    // Every setter below writes through to the real Rnote engine config that is
    // installed on the headless engine. Nothing is emulated or stored
    // side-channel: the values round-trip through Rnote's own save/load path.

    fn require_idle(&self) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas configuration while a stroke is active");
        }
        Ok(())
    }

    /// Select the brush ink style honoured by the Pen tool.
    pub fn set_brush_style(&mut self, style: CanvasBrushStyle) -> Result<()> {
        self.require_idle()?;
        self.brush_style = style;
        if self.tool == CanvasTool::Pen {
            self.configure_tool();
        } else {
            // Persist the preference even when another tool is active so the
            // next Pen selection honours it (Rnote remembers per-brush state).
            let mut config = self.config.write();
            config.pens_config.brush_config.style = match style {
                CanvasBrushStyle::Marker => BrushStyle::Marker,
                CanvasBrushStyle::Solid => BrushStyle::Solid,
                CanvasBrushStyle::Textured => BrushStyle::Textured,
            };
        }
        Ok(())
    }

    pub fn brush_style(&self) -> CanvasBrushStyle {
        self.brush_style
    }

    /// Select the pen-path builder used by the brush pen.
    pub fn set_brush_builder(&mut self, builder: CanvasBrushBuilder) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.brush_config.builder_type = builder.rnote_builder();
        Ok(())
    }

    pub fn brush_builder(&self) -> CanvasBrushBuilder {
        match self.config.read().pens_config.brush_config.builder_type {
            PenPathBuilderType::Simple => CanvasBrushBuilder::Simple,
            PenPathBuilderType::Curved => CanvasBrushBuilder::Curved,
            PenPathBuilderType::Modeled => CanvasBrushBuilder::Modeled,
        }
    }

    /// Set the stroke width for the currently active tool family.
    ///
    /// Mirrors Rnote's per-tool width memory: brush widths apply to the active
    /// brush style, shaper widths to the active shaper style, and the eraser
    /// tool edits the eraser width.
    pub fn set_stroke_width(&mut self, width: f64) -> Result<()> {
        self.require_idle()?;
        if !width.is_finite() {
            anyhow::bail!("Canvas stroke width must be finite");
        }
        match self.tool {
            CanvasTool::Eraser => self.set_eraser_width(width),
            CanvasTool::Shape => {
                if !(0.1..=500.0).contains(&width) {
                    anyhow::bail!("Canvas shaper stroke width out of range");
                }
                let mut config = self.config.write();
                let shaper = &mut config.pens_config.shaper_config;
                match shaper.style {
                    ShaperStyle::Smooth => {
                        shaper.smooth_options.stroke_width = width;
                        shaper.smooth_options.update_piet_stroke_style();
                    }
                    ShaperStyle::Rough => {
                        shaper.rough_options.stroke_width = width;
                    }
                }
                Ok(())
            }
            _ => {
                if !(0.1..=500.0).contains(&width) {
                    anyhow::bail!("Canvas brush stroke width out of range");
                }
                let mut config = self.config.write();
                let brush = &mut config.pens_config.brush_config;
                let active = if self.tool == CanvasTool::Highlighter {
                    BrushStyle::Marker
                } else {
                    brush.style
                };
                match active {
                    BrushStyle::Marker => {
                        brush.marker_options.stroke_width = width;
                        brush.marker_options.update_piet_stroke_style();
                    }
                    BrushStyle::Solid => {
                        brush.solid_options.stroke_width = width;
                        brush.solid_options.update_piet_stroke_style();
                    }
                    BrushStyle::Textured => {
                        brush.textured_options.stroke_width = width;
                    }
                }
                Ok(())
            }
        }
    }

    /// Current stroke width for the active tool family.
    pub fn stroke_width(&self) -> f64 {
        let config = self.config.read();
        match self.tool {
            CanvasTool::Eraser => config.pens_config.eraser_config.width,
            CanvasTool::Shape => match config.pens_config.shaper_config.style {
                ShaperStyle::Smooth => config.pens_config.shaper_config.smooth_options.stroke_width,
                ShaperStyle::Rough => config.pens_config.shaper_config.rough_options.stroke_width,
            },
            _ => {
                let brush = &config.pens_config.brush_config;
                let active = if self.tool == CanvasTool::Highlighter {
                    BrushStyle::Marker
                } else {
                    brush.style
                };
                match active {
                    BrushStyle::Marker => brush.marker_options.stroke_width,
                    BrushStyle::Solid => brush.solid_options.stroke_width,
                    BrushStyle::Textured => brush.textured_options.stroke_width,
                }
            }
        }
    }

    /// Set the stroke colour for all pens (Rnote `set_all_stroke_colors`).
    pub fn set_stroke_color(&mut self, color: CanvasRgba) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.set_all_stroke_colors(color.rnote_color());
        Ok(())
    }

    pub fn stroke_color(&self) -> CanvasRgba {
        let config = self.config.read();
        let brush = &config.pens_config.brush_config;
        let color = brush.solid_options.stroke_color
            .or(brush.marker_options.stroke_color)
            .or(brush.textured_options.stroke_color)
            .unwrap_or(Color::BLACK);
        CanvasRgba::from_rnote(color)
    }

    /// Set the fill colour for all pens (Rnote `set_all_fill_colors`).
    pub fn set_fill_color(&mut self, color: CanvasRgba) -> Result<()> {
        self.require_idle()?;
        let fill = if color.a <= 0.0 { None } else { Some(color.rnote_color()) };
        {
            let mut config = self.config.write();
            config.pens_config.brush_config.marker_options.fill_color = fill;
            config.pens_config.brush_config.solid_options.fill_color = fill;
            config.pens_config.shaper_config.smooth_options.fill_color = fill;
            config.pens_config.shaper_config.rough_options.fill_color = fill;
        }
        Ok(())
    }

    pub fn fill_color(&self) -> CanvasRgba {
        let config = self.config.read();
        match config.pens_config.brush_config.solid_options.fill_color
            .or(config.pens_config.shaper_config.smooth_options.fill_color)
        {
            Some(color) => CanvasRgba::from_rnote(color),
            None => CanvasRgba::new(0.0, 0.0, 0.0, 0.0),
        }
    }

    pub fn set_eraser_width(&mut self, width: f64) -> Result<()> {
        self.require_idle()?;
        if !width.is_finite() || !(1.0..=500.0).contains(&width) {
            anyhow::bail!("Canvas eraser width out of range");
        }
        self.config.write().pens_config.eraser_config.width = width;
        Ok(())
    }

    pub fn eraser_width(&self) -> f64 {
        self.config.read().pens_config.eraser_config.width
    }

    pub fn set_eraser_style(&mut self, style: CanvasEraserStyle) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.eraser_config.style = match style {
            CanvasEraserStyle::TrashColliding => EraserStyle::TrashCollidingStrokes,
            CanvasEraserStyle::SplitColliding => EraserStyle::SplitCollidingStrokes,
        };
        Ok(())
    }

    pub fn eraser_style(&self) -> CanvasEraserStyle {
        match self.config.read().pens_config.eraser_config.style {
            EraserStyle::TrashCollidingStrokes => CanvasEraserStyle::TrashColliding,
            EraserStyle::SplitCollidingStrokes => CanvasEraserStyle::SplitColliding,
        }
    }

    pub fn set_shaper_style(&mut self, style: CanvasShaperStyle) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.shaper_config.style = match style {
            CanvasShaperStyle::Smooth => ShaperStyle::Smooth,
            CanvasShaperStyle::Rough => ShaperStyle::Rough,
        };
        Ok(())
    }

    pub fn shaper_style(&self) -> CanvasShaperStyle {
        match self.config.read().pens_config.shaper_config.style {
            ShaperStyle::Smooth => CanvasShaperStyle::Smooth,
            ShaperStyle::Rough => CanvasShaperStyle::Rough,
        }
    }

    /// Enable/disable Rnote shape constraint snapping (1:1, H/V guides).
    pub fn set_shaper_constraints_enabled(&mut self, enabled: bool) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.shaper_config.constraints.enabled = enabled;
        Ok(())
    }

    pub fn shaper_constraints_enabled(&self) -> bool {
        self.config.read().pens_config.shaper_config.constraints.enabled
    }

    pub fn set_selector_style(&mut self, style: CanvasSelectorStyle) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.selector_config.style = match style {
            CanvasSelectorStyle::Polygon => SelectorStyle::Polygon,
            CanvasSelectorStyle::Rectangle => SelectorStyle::Rectangle,
            CanvasSelectorStyle::Single => SelectorStyle::Single,
            CanvasSelectorStyle::IntersectingPath => SelectorStyle::IntersectingPath,
        };
        Ok(())
    }

    pub fn selector_style(&self) -> CanvasSelectorStyle {
        match self.config.read().pens_config.selector_config.style {
            SelectorStyle::Polygon => CanvasSelectorStyle::Polygon,
            SelectorStyle::Rectangle => CanvasSelectorStyle::Rectangle,
            SelectorStyle::Single => CanvasSelectorStyle::Single,
            SelectorStyle::IntersectingPath => CanvasSelectorStyle::IntersectingPath,
        }
    }

    pub fn set_selector_lock_aspect(&mut self, lock: bool) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.selector_config.resize_lock_aspectratio = lock;
        Ok(())
    }

    pub fn selector_lock_aspect(&self) -> bool {
        self.config.read().pens_config.selector_config.resize_lock_aspectratio
    }

    pub fn set_tools_style(&mut self, style: CanvasToolsStyle) -> Result<()> {
        self.require_idle()?;
        self.config.write().pens_config.tools_config.style = match style {
            CanvasToolsStyle::VerticalSpace => ToolStyle::VerticalSpace,
            CanvasToolsStyle::OffsetCamera => ToolStyle::OffsetCamera,
            CanvasToolsStyle::Zoom => ToolStyle::Zoom,
            CanvasToolsStyle::Laser => ToolStyle::Laser,
        };
        Ok(())
    }

    pub fn tools_style(&self) -> CanvasToolsStyle {
        match self.config.read().pens_config.tools_config.style {
            ToolStyle::VerticalSpace => CanvasToolsStyle::VerticalSpace,
            ToolStyle::OffsetCamera => CanvasToolsStyle::OffsetCamera,
            ToolStyle::Zoom => CanvasToolsStyle::Zoom,
            ToolStyle::Laser => CanvasToolsStyle::Laser,
        }
    }

    pub fn set_typewriter_font_size(&mut self, size: f64) -> Result<()> {
        self.require_idle()?;
        if !size.is_finite() || !(1.0..=512.0).contains(&size) {
            anyhow::bail!("Canvas typewriter font size out of range");
        }
        self.config.write().pens_config.typewriter_config.text_style.font_size = size;
        Ok(())
    }

    pub fn typewriter_font_size(&self) -> f64 {
        self.config.read().pens_config.typewriter_config.text_style.font_size
    }

    pub fn set_typewriter_text_width(&mut self, width: f64) -> Result<()> {
        self.require_idle()?;
        if !width.is_finite() || width < 0.0 {
            anyhow::bail!("Canvas typewriter text width must be finite and non-negative");
        }
        self.config.write().pens_config.typewriter_config.set_text_width(width);
        Ok(())
    }

    pub fn typewriter_text_width(&self) -> f64 {
        self.config.read().pens_config.typewriter_config.text_width()
    }

    pub fn set_layout(&mut self, layout: CanvasLayout) -> Result<()> {
        self.require_idle()?;
        self.engine.document.config.layout = match layout {
            CanvasLayout::FixedSize => Layout::FixedSize,
            CanvasLayout::ContinuousVertical => Layout::ContinuousVertical,
            CanvasLayout::SemiInfinite => Layout::SemiInfinite,
            CanvasLayout::Infinite => Layout::Infinite,
        };
        // NOTE: no explicit resize here: Document::resize_to_fit_content is
        // pub(crate) in the donor, and the engine auto-expands the document on
        // the next committed stroke through its internal resize path.
        Ok(())
    }

    pub fn layout(&self) -> CanvasLayout {
        match self.engine.document.config.layout {
            Layout::FixedSize => CanvasLayout::FixedSize,
            Layout::ContinuousVertical => CanvasLayout::ContinuousVertical,
            Layout::SemiInfinite => CanvasLayout::SemiInfinite,
            Layout::Infinite => CanvasLayout::Infinite,
        }
    }

    pub fn set_background_pattern(&mut self, pattern: CanvasPattern) -> Result<()> {
        self.require_idle()?;
        self.engine.document.config.background.pattern = match pattern {
            CanvasPattern::None => PatternStyle::None,
            CanvasPattern::Lines => PatternStyle::Lines,
            CanvasPattern::Grid => PatternStyle::Grid,
            CanvasPattern::Dots => PatternStyle::Dots,
            CanvasPattern::IsometricGrid => PatternStyle::IsometricGrid,
            CanvasPattern::IsometricDots => PatternStyle::IsometricDots,
        };
        Ok(())
    }

    pub fn background_pattern(&self) -> CanvasPattern {
        match self.engine.document.config.background.pattern {
            PatternStyle::None => CanvasPattern::None,
            PatternStyle::Lines => CanvasPattern::Lines,
            PatternStyle::Grid => CanvasPattern::Grid,
            PatternStyle::Dots => CanvasPattern::Dots,
            PatternStyle::IsometricGrid => CanvasPattern::IsometricGrid,
            PatternStyle::IsometricDots => CanvasPattern::IsometricDots,
        }
    }

    pub fn set_background_color(&mut self, color: CanvasRgba) -> Result<()> {
        self.require_idle()?;
        self.engine.document.config.background.color = color.rnote_color();
        Ok(())
    }

    pub fn background_color(&self) -> CanvasRgba {
        CanvasRgba::from_rnote(self.engine.document.config.background.color)
    }

    pub fn set_pattern_color(&mut self, color: CanvasRgba) -> Result<()> {
        self.require_idle()?;
        self.engine.document.config.background.pattern_color = color.rnote_color();
        Ok(())
    }

    pub fn pattern_color(&self) -> CanvasRgba {
        CanvasRgba::from_rnote(self.engine.document.config.background.pattern_color)
    }

    pub fn set_pattern_size(&mut self, width: f64, height: f64) -> Result<()> {
        self.require_idle()?;
        if !width.is_finite() || !height.is_finite() || width <= 0.0 || height <= 0.0 {
            anyhow::bail!("Canvas pattern size must be finite and positive");
        }
        self.engine.document.config.background.pattern_size = Vector2::new(width, height);
        Ok(())
    }

    pub fn pattern_size(&self) -> (f64, f64) {
        let size = self.engine.document.config.background.pattern_size;
        (size[0], size[1])
    }

    pub fn set_format_size(&mut self, width: f64, height: f64) -> Result<()> {
        self.require_idle()?;
        if !width.is_finite() || !height.is_finite() || width <= 0.0 || height <= 0.0 {
            anyhow::bail!("Canvas format size must be finite and positive");
        }
        self.engine.document.config.format.set_width(width);
        self.engine.document.config.format.set_height(height);
        // NOTE: see set_layout — donor resize helpers are pub(crate); the
        // engine reconciles document extents on subsequent strokes/exports.
        Ok(())
    }

    pub fn format_size(&self) -> CanvasFormatSize {
        CanvasFormatSize {
            width: self.engine.document.config.format.width(),
            height: self.engine.document.config.format.height(),
            dpi: self.engine.document.config.format.dpi(),
        }
    }

    pub fn set_format_dpi(&mut self, dpi: f64) -> Result<()> {
        self.require_idle()?;
        if !dpi.is_finite() || !(1.0..=5000.0).contains(&dpi) {
            anyhow::bail!("Canvas format DPI out of range");
        }
        self.engine.document.config.format.set_dpi(dpi);
        Ok(())
    }

    pub fn set_snap_positions(&mut self, snap: bool) -> Result<()> {
        self.require_idle()?;
        self.config.write().snap_positions = snap;
        Ok(())
    }

    pub fn snap_positions(&self) -> bool {
        self.config.read().snap_positions
    }

    /// Set document export preferences (background / pattern / print optimize).
    pub fn set_export_prefs(&mut self, with_background: bool, with_pattern: bool, optimize_printing: bool) -> Result<()> {
        self.require_idle()?;
        let mut config = self.config.write();
        config.export_prefs.doc_export_prefs.with_background = with_background;
        config.export_prefs.doc_export_prefs.with_pattern = with_pattern;
        config.export_prefs.doc_export_prefs.optimize_printing = optimize_printing;
        Ok(())
    }

    pub fn export_prefs(&self) -> (bool, bool, bool) {
        let config = self.config.read();
        let prefs = &config.export_prefs.doc_export_prefs;
        (prefs.with_background, prefs.with_pattern, prefs.optimize_printing)
    }

    /// Export the whole document in the requested Rnote format.
    pub async fn export_doc_bytes(&self, format: CanvasDocExportFormat) -> Result<Vec<u8>> {
        let prefs = DocExportPrefs {
            export_format: match format {
                CanvasDocExportFormat::Svg => DocExportFormat::Svg,
                CanvasDocExportFormat::Pdf => DocExportFormat::Pdf,
                CanvasDocExportFormat::Xopp => DocExportFormat::Xopp,
            },
            ..self.config.read().export_prefs.doc_export_prefs
        };
        self.engine
            .export_doc("CakeOS Canvas".to_string(), Some(prefs))
            .await
            .context("Rnote doc export channel closed")?
            .context("Rnote doc export failed")
    }

    /// Export the current selector selection as SVG, if a selection exists.
    pub async fn export_selection_svg(&self) -> Result<Option<Vec<u8>>> {
        let prefs = SelectionExportPrefs {
            export_format: SelectionExportFormat::Svg,
            ..self.config.read().export_prefs.selection_export_prefs
        };
        self.engine
            .export_selection(Some(prefs))
            .await
            .context("Rnote selection export channel closed")?
            .context("Rnote selection export failed")
    }

    /// Produce renderer-neutral SVG bytes using the engine export path.
    /// This is the first HUI-consumable rendering proof; viewport tile exposure
    /// remains a later optimization rather than importing GTK/GSK into HUI.
    pub async fn export_svg(&self) -> Result<Vec<u8>> {
        let prefs = DocExportPrefs {
            export_format: DocExportFormat::Svg,
            ..DocExportPrefs::default()
        };

        self.engine
            .export_doc("CakeOS Canvas PoC".to_string(), Some(prefs))
            .await
            .context("Rnote SVG export channel closed")?
            .context("Rnote SVG export failed")
    }

    /// Produce the stable renderer-neutral frame consumed by HUI.
    ///
    /// Rnote's document exporter first selects the page range containing content,
    /// then `StrokeContent::gen_svg` normalizes that rectangle to a zero-based SVG
    /// viewBox. The frame therefore carries the original document-space content
    /// rectangle while the SVG bytes remain normalized. HUI subtracts the frame
    /// origin when selecting a source rectangle and keeps its viewport in document
    /// coordinates.
    pub async fn render_frame(&self) -> Result<CanvasRenderFrame> {
        let prefs = DocExportPrefs {
            export_format: DocExportFormat::Svg,
            ..DocExportPrefs::default()
        };
        let content = self.engine.extract_document_content();
        let source_bounds = content
            .bounds()
            .context("Rnote document content has no renderable bounds")?;
        let generated = content
            .gen_svg(
                prefs.with_background,
                prefs.with_pattern,
                prefs.optimize_printing,
                0.0,
            )?
            .context("Rnote document SVG generation returned no content")?;
        let bytes = add_xml_header(
            wrap_svg_root(
                generated.svg_data.as_str(),
                Some(generated.bounds),
                Some(generated.bounds),
                false,
            )
            .as_str(),
        )
        .into_bytes();
        let bounds = CanvasDocumentBounds {
            x: source_bounds.mins[0],
            y: source_bounds.mins[1],
            width: source_bounds.maxs[0] - source_bounds.mins[0],
            height: source_bounds.maxs[1] - source_bounds.mins[1],
        };

        Ok(CanvasRenderFrame {
            format: CanvasRenderFormat::Svg,
            coordinate_space: CanvasCoordinateSpace::Document,
            bounds,
            bytes,
        })
    }

    /// Save a native Rnote payload for compatibility/interchange testing.
    pub async fn save_rnote(&self) -> Result<Vec<u8>> {
        self.engine
            .save_as_rnote_bytes("canvas-poc.rnote".to_string())
            .await
            .context("Rnote save channel closed")?
            .context("Rnote save failed")
    }

    /// Commit a complete Rnote document by replacing a same-directory temporary
    /// file. A failed write leaves the previous document available for reopen.
    pub async fn save_rnote_atomically(&self, path: impl AsRef<Path>) -> Result<()> {
        let target = path.as_ref();
        let parent = target
            .parent()
            .filter(|path| !path.as_os_str().is_empty())
            .context("Canvas Rnote path must have a parent directory")?;
        let name = target
            .file_name()
            .context("Canvas Rnote path must name a file")?
            .to_string_lossy();
        fs::create_dir_all(parent).context("create Canvas Rnote parent directory")?;
        let temporary = parent.join(format!(".{name}.{}.tmp", std::process::id()));
        let bytes = self.save_rnote().await?;

        let write_result = (|| -> Result<()> {
            let mut file = File::create(&temporary).context("create temporary Canvas Rnote file")?;
            use std::io::Write;
            file.write_all(&bytes).context("write temporary Canvas Rnote file")?;
            file.sync_all().context("sync temporary Canvas Rnote file")?;
            fs::rename(&temporary, target).context("replace Canvas Rnote file")?;
            Ok(())
        })();
        if write_result.is_err() {
            let _ = fs::remove_file(&temporary);
        }
        write_result
    }

    /// Restore a fresh engine from native Rnote bytes.
    pub async fn from_rnote(bytes: Vec<u8>) -> Result<Self> {
        let snapshot = EngineSnapshot::load_from_rnote_bytes(bytes)
            .await
            .context("Rnote snapshot load failed")?;
        let mut engine = Engine::default();
        let config = EngineConfigShared::from(EngineConfig::default());
        let _ = engine.install_config(&config, None);
        let _ = engine.load_snapshot(snapshot);
        let brush_style = match config.read().pens_config.brush_config.style {
            BrushStyle::Marker => CanvasBrushStyle::Marker,
            BrushStyle::Solid => CanvasBrushStyle::Solid,
            BrushStyle::Textured => CanvasBrushStyle::Textured,
        };
        let shape = match config.read().pens_config.shaper_config.builder_type {
            ShapeBuilderType::Rectangle => CanvasShape::Rectangle,
            ShapeBuilderType::Ellipse => CanvasShape::Ellipse,
            ShapeBuilderType::Line => CanvasShape::Line,
            ShapeBuilderType::Arrow => CanvasShape::Arrow,
            ShapeBuilderType::Grid => CanvasShape::Grid,
            ShapeBuilderType::CoordSystem2D => CanvasShape::CoordSystem2D,
            ShapeBuilderType::CoordSystem3D => CanvasShape::CoordSystem3D,
            ShapeBuilderType::QuadrantCoordSystem2D => CanvasShape::QuadrantCoordSystem2D,
            ShapeBuilderType::FociEllipse => CanvasShape::FociEllipse,
            ShapeBuilderType::QuadBez => CanvasShape::QuadBez,
            ShapeBuilderType::CubBez => CanvasShape::CubBez,
            ShapeBuilderType::Polyline => CanvasShape::Polyline,
            ShapeBuilderType::Polygon => CanvasShape::Polygon,
        };
        let mut canvas = Self {
            engine,
            stroke_active: false,
            tool: CanvasTool::Pen,
            shape,
            brush_style,
            config,
        };
        canvas.configure_tool();
        Ok(canvas)
    }

    pub async fn from_rnote_file(path: impl AsRef<Path>) -> Result<Self> {
        let bytes = fs::read(path.as_ref()).context("read Canvas Rnote file")?;
        Self::from_rnote(bytes).await
    }

    /// Expose a debug-only JSON snapshot for mechanical assertions in the PoC.
    pub fn debug_state_json(&self) -> Result<String> {
        self.engine
            .export_state_as_json()
            .context("Rnote state serialization failed")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use futures::executor::block_on;

    fn sample_stroke() -> [CanvasPointerSample; 5] {
        [
            CanvasPointerSample::new(120.0, 120.0, 0.15),
            CanvasPointerSample::new(150.0, 140.0, 0.35),
            CanvasPointerSample::new(190.0, 160.0, 0.65),
            CanvasPointerSample::new(235.0, 190.0, 0.90),
            CanvasPointerSample::new(280.0, 220.0, 0.55),
        ]
    }

    fn trashed_stroke_count(canvas: &HeadlessCanvasEngine) -> usize {
        canvas
            .debug_state_json()
            .expect("debug engine state")
            .matches("\"trashed\": true")
            .count()
    }

    #[test]
    fn pressure_is_clamped_but_tilt_is_preserved_at_hui_boundary() {
        let mut sample = CanvasPointerSample::new(1.0, 2.0, 2.5);
        sample.tilt_x = 37.0;
        sample.tilt_y = -22.0;
        assert_eq!(sample.pressure, 1.0);
        assert_eq!(sample.tilt_x, 37.0);
        assert_eq!(sample.tilt_y, -22.0);
    }

    #[test]
    fn incremental_stroke_boundary_enforces_event_lifecycle() {
        let samples = sample_stroke();
        let mut canvas = HeadlessCanvasEngine::new();

        assert!(canvas.update_stroke(samples[1]).is_err());
        assert!(canvas.end_stroke(samples[1]).is_err());

        canvas.begin_stroke(samples[0]).unwrap();
        assert!(canvas.stroke_active());
        assert!(canvas.begin_stroke(samples[1]).is_err());
        assert!(canvas.set_tool(CanvasTool::Eraser).is_err());
        assert!(canvas.undo().is_err());
        assert!(canvas.redo().is_err());

        canvas.update_stroke(samples[1]).unwrap();
        canvas.update_stroke(samples[2]).unwrap();
        canvas.end_stroke(samples[3]).unwrap();
        assert!(!canvas.stroke_active());
    }

    #[test]
    fn completed_stroke_participates_in_undo_redo_history() {
        let mut canvas = HeadlessCanvasEngine::new();
        assert!(!canvas.can_undo());
        assert!(!canvas.undo().unwrap());

        canvas.draw_stroke(&sample_stroke()).unwrap();
        assert!(canvas.can_undo());

        assert!(canvas.undo().unwrap());
        assert!(canvas.can_redo());

        assert!(canvas.redo().unwrap());
        assert!(!canvas.can_redo());
        assert!(canvas.can_undo());
    }

    #[test]
    fn renderer_neutral_frame_carries_export_coordinate_metadata() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            let storage_bounds = canvas.document_bounds();

            let frame = canvas.render_frame().await.unwrap();
            assert_eq!(frame.format, CanvasRenderFormat::Svg);
            assert_eq!(frame.coordinate_space, CanvasCoordinateSpace::Document);
            assert_eq!(frame.mime_type(), "image/svg+xml");
            assert!(frame.bounds.width > 0.0);
            assert!(frame.bounds.height > 0.0);
            assert!(frame.bounds.x <= 120.0 && frame.bounds.x + frame.bounds.width >= 280.0);
            assert!(frame.bounds.y <= 120.0 && frame.bounds.y + frame.bounds.height >= 220.0);
            assert!(frame.bounds.width < storage_bounds.width);
            assert!(frame.bounds.height < storage_bounds.height);
            let svg = std::str::from_utf8(&frame.bytes).unwrap();
            assert!(svg.contains("<svg"));
            assert!(svg.contains("viewBox=\"0.000 0.000"));
            assert!(frame.bytes.len() > 200, "render frame unexpectedly empty");
        });
    }

    #[test]
    fn eraser_trashes_stroke_and_history_restores_state() {
        let samples = sample_stroke();
        let mut canvas = HeadlessCanvasEngine::new();
        canvas.draw_stroke(&samples).unwrap();
        assert_eq!(trashed_stroke_count(&canvas), 0);

        canvas.set_tool(CanvasTool::Eraser).unwrap();
        canvas.begin_stroke(samples[2]).unwrap();
        canvas.end_stroke(samples[2]).unwrap();
        assert_eq!(trashed_stroke_count(&canvas), 1, "eraser did not trash the colliding stroke");

        assert!(canvas.undo().unwrap());
        assert!(canvas.can_redo());
        assert_eq!(trashed_stroke_count(&canvas), 0, "undo did not restore the erased stroke");

        assert!(canvas.redo().unwrap());
        assert_eq!(trashed_stroke_count(&canvas), 1, "redo did not reapply the eraser state");
    }

    #[test]
    fn headless_engine_draws_exports_saves_and_reloads() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.draw_stroke(&sample_stroke()).unwrap();

            let svg_before = canvas.export_svg().await.unwrap();
            let svg_text = std::str::from_utf8(&svg_before).unwrap();
            assert!(svg_text.contains("<svg"));
            assert!(svg_before.len() > 200, "SVG unexpectedly empty");

            let native = canvas.save_rnote().await.unwrap();
            assert!(native.len() > 100, ".rnote payload unexpectedly empty");

            let restored = HeadlessCanvasEngine::from_rnote(native).await.unwrap();
            assert!(!restored.stroke_active());
            assert_eq!(restored.tool(), CanvasTool::Pen);
            let svg_after = restored.export_svg().await.unwrap();
            assert!(svg_after.len() > 200, "reloaded SVG unexpectedly empty");

            let state = restored.debug_state_json().unwrap();
            assert!(state.contains("stroke_components"));
        });
    }

    #[test]
    fn donor_parity_config_round_trips_through_real_engine_state() {
        block_on(async {
            let mut canvas = HeadlessCanvasEngine::new();

            // Brush family (Rnote brush page).
            canvas.set_brush_style(CanvasBrushStyle::Textured).unwrap();
            assert_eq!(canvas.brush_style(), CanvasBrushStyle::Textured);
            canvas.set_brush_builder(CanvasBrushBuilder::Curved).unwrap();
            assert_eq!(canvas.brush_builder(), CanvasBrushBuilder::Curved);
            canvas.set_stroke_width(7.5).unwrap();
            assert!((canvas.stroke_width() - 7.5).abs() < 1e-9);

            // Shared stroke / fill colours (Rnote colour pickers).
            canvas.set_stroke_color(CanvasRgba::new(0.9, 0.1, 0.2, 1.0)).unwrap();
            let stroke = canvas.stroke_color();
            assert!((stroke.r - 0.9).abs() < 0.02 && (stroke.g - 0.1).abs() < 0.02);
            canvas.set_fill_color(CanvasRgba::new(0.1, 0.8, 0.3, 0.6)).unwrap();
            assert!(canvas.fill_color().a > 0.5);

            // Eraser page.
            canvas.set_tool(CanvasTool::Eraser).unwrap();
            canvas.set_eraser_width(21.0).unwrap();
            assert!((canvas.eraser_width() - 21.0).abs() < 1e-9);
            canvas.set_eraser_style(CanvasEraserStyle::SplitColliding).unwrap();
            assert_eq!(canvas.eraser_style(), CanvasEraserStyle::SplitColliding);
            assert!(canvas.set_eraser_width(0.0).is_err());

            // Shaper page (all 13 donor builders stay selectable).
            canvas.set_tool(CanvasTool::Shape).unwrap();
            for shape in [
                CanvasShape::Rectangle, CanvasShape::Ellipse, CanvasShape::Line,
                CanvasShape::Arrow, CanvasShape::Grid, CanvasShape::CoordSystem2D,
                CanvasShape::CoordSystem3D, CanvasShape::QuadrantCoordSystem2D,
                CanvasShape::FociEllipse, CanvasShape::QuadBez, CanvasShape::CubBez,
                CanvasShape::Polyline, CanvasShape::Polygon,
            ] {
                canvas.set_shape(shape).unwrap();
            }
            canvas.set_shaper_style(CanvasShaperStyle::Rough).unwrap();
            assert_eq!(canvas.shaper_style(), CanvasShaperStyle::Rough);
            canvas.set_stroke_width(3.25).unwrap();
            assert!((canvas.stroke_width() - 3.25).abs() < 1e-9);
            canvas.set_shaper_constraints_enabled(true).unwrap();
            assert!(canvas.shaper_constraints_enabled());

            // Selector + tools pages.
            canvas.set_tool(CanvasTool::Selector).unwrap();
            canvas.set_selector_style(CanvasSelectorStyle::Polygon).unwrap();
            assert_eq!(canvas.selector_style(), CanvasSelectorStyle::Polygon);
            canvas.set_selector_lock_aspect(true).unwrap();
            assert!(canvas.selector_lock_aspect());
            canvas.set_tool(CanvasTool::Tools).unwrap();
            canvas.set_tools_style(CanvasToolsStyle::Laser).unwrap();
            assert_eq!(canvas.tools_style(), CanvasToolsStyle::Laser);

            // Typewriter page.
            canvas.set_tool(CanvasTool::Typewriter).unwrap();
            canvas.set_typewriter_font_size(42.0).unwrap();
            assert!((canvas.typewriter_font_size() - 42.0).abs() < 1e-9);
            assert!(canvas.set_typewriter_font_size(0.0).is_err());

            // Document page (layout / background / format / snap).
            canvas.set_layout(CanvasLayout::ContinuousVertical).unwrap();
            assert_eq!(canvas.layout(), CanvasLayout::ContinuousVertical);
            canvas.set_background_pattern(CanvasPattern::Grid).unwrap();
            assert_eq!(canvas.background_pattern(), CanvasPattern::Grid);
            canvas.set_background_color(CanvasRgba::new(1.0, 1.0, 1.0, 1.0)).unwrap();
            canvas.set_pattern_size(28.0, 28.0).unwrap();
            canvas.set_format_size(1123.0, 1587.0).unwrap();
            let format = canvas.format_size();
            assert!((format.width - 1123.0).abs() < 1e-6);
            canvas.set_format_dpi(192.0).unwrap();
            assert!((canvas.format_size().dpi - 192.0).abs() < 1e-6);
            canvas.set_snap_positions(true).unwrap();
            assert!(canvas.snap_positions());

            // Export prefs + multi-format export through the real engine.
            canvas.set_tool(CanvasTool::Pen).unwrap();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            canvas.set_export_prefs(true, true, false).unwrap();
            let svg = canvas.export_doc_bytes(CanvasDocExportFormat::Svg).await.unwrap();
            assert!(svg.len() > 200);
            let pdf = canvas.export_doc_bytes(CanvasDocExportFormat::Pdf).await.unwrap();
            assert!(pdf.len() > 200);
            let xopp = canvas.export_doc_bytes(CanvasDocExportFormat::Xopp).await.unwrap();
            assert!(xopp.len() > 200);

            // Config survives the native save/load round-trip.
            let native = canvas.save_rnote().await.unwrap();
            let restored = HeadlessCanvasEngine::from_rnote(native).await.unwrap();
            assert_eq!(restored.brush_style(), CanvasBrushStyle::Textured);
            assert!((restored.eraser_width() - 21.0).abs() < 1e-9);
        });
    }

    #[test]
    fn rnote_tools_camera_and_atomic_reopen_preserve_world_coordinates() {
        block_on(async {
            let root = std::env::temp_dir().join(format!(
                "cakeos-rnote-atomic-{}-{}",
                std::process::id(),
                SystemTime::now().duration_since(UNIX_EPOCH).unwrap().as_nanos()
            ));
            let path = root.join("canvas.rnote");
            let mut canvas = HeadlessCanvasEngine::new();
            canvas.set_viewport_size(1280.0, 720.0).unwrap();
            canvas.zoom_to(1.75).unwrap();
            canvas.pan_by(320.0, -180.0).unwrap();

            canvas.set_tool(CanvasTool::Pen).unwrap();
            canvas.draw_stroke(&sample_stroke()).unwrap();
            canvas.set_tool(CanvasTool::Highlighter).unwrap();
            canvas.draw_stroke(&[
                CanvasPointerSample::new(140.0, 170.0, 0.5),
                CanvasPointerSample::new(260.0, 170.0, 0.5),
            ]).unwrap();
            canvas.set_shape(CanvasShape::Ellipse).unwrap();
            canvas.set_tool(CanvasTool::Shape).unwrap();
            canvas.draw_stroke(&[
                CanvasPointerSample::new(300.0, 100.0, 0.5),
                CanvasPointerSample::new(380.0, 180.0, 0.5),
            ]).unwrap();
            canvas.set_tool(CanvasTool::Selector).unwrap();
            canvas.draw_stroke(&[
                CanvasPointerSample::new(100.0, 90.0, 0.5),
                CanvasPointerSample::new(410.0, 90.0, 0.5),
                CanvasPointerSample::new(410.0, 240.0, 0.5),
                CanvasPointerSample::new(100.0, 240.0, 0.5),
                CanvasPointerSample::new(100.0, 90.0, 0.5),
            ]).unwrap();

            let before = canvas.render_frame().await.unwrap();
            let viewport = canvas.viewport();
            canvas.save_rnote_atomically(&path).await.unwrap();
            assert!(path.exists());
            assert!(root.read_dir().unwrap().all(|entry| {
                !entry.unwrap().file_name().to_string_lossy().ends_with(".tmp")
            }));

            let restored = HeadlessCanvasEngine::from_rnote_file(&path).await.unwrap();
            let after = restored.render_frame().await.unwrap();
            assert_eq!(before.bounds, after.bounds);
            assert_eq!(restored.viewport(), viewport);
            let _ = fs::remove_dir_all(root);
        });
    }
}
