//! Approved migration proof of concept for a renderer-neutral Rnote core.
//!
//! This crate deliberately depends on `rnote-engine` with default features disabled.
//! It must not enable Rnote's `ui` feature or depend on GTK/Libadwaita.

pub mod ffi;

use std::collections::HashSet;
use std::time::Instant;

use anyhow::{Context, Result};
use nalgebra::Vector2;
use rnote_compose::penevent::PenEvent;
use rnote_compose::penpath::Element;
use rnote_compose::utils::{add_xml_header, wrap_svg_root};
use rnote_engine::engine::export::{DocExportFormat, DocExportPrefs};
use rnote_engine::engine::EngineSnapshot;
use rnote_engine::pens::PenMode;
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

/// Stroke tools proven at the CakeOS-to-Rnote boundary.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Default)]
pub enum CanvasStrokeTool {
    #[default]
    Pen,
    Eraser,
}

impl CanvasStrokeTool {
    fn rnote_pen_mode(self) -> PenMode {
        match self {
            Self::Pen => PenMode::Pen,
            Self::Eraser => PenMode::Eraser,
        }
    }
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
    stroke_tool: CanvasStrokeTool,
}

impl Default for HeadlessCanvasEngine {
    fn default() -> Self {
        Self::new()
    }
}

impl HeadlessCanvasEngine {
    pub fn new() -> Self {
        let mut engine = Engine::default();
        // Initialize the currently configured pen while keeping the `ui` feature off.
        let _ = engine.reinstall_pen_current_style();
        Self {
            engine,
            stroke_active: false,
            stroke_tool: CanvasStrokeTool::Pen,
        }
    }

    fn send_pen_event(&mut self, event: PenEvent) {
        let _ = self.engine.handle_pen_event(
            event,
            Some(self.stroke_tool.rnote_pen_mode()),
            Instant::now(),
        );
    }

    pub fn stroke_tool(&self) -> CanvasStrokeTool {
        self.stroke_tool
    }

    /// Change the tool used by subsequent stroke events. Tool changes are kept
    /// outside an active stroke so one gesture cannot change interpretation midway.
    pub fn set_stroke_tool(&mut self, tool: CanvasStrokeTool) -> Result<()> {
        if self.stroke_active {
            anyhow::bail!("cannot change Canvas stroke tool while a stroke is active");
        }
        self.stroke_tool = tool;
        Ok(())
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

    /// Restore a fresh engine from native Rnote bytes.
    pub async fn from_rnote(bytes: Vec<u8>) -> Result<Self> {
        let snapshot = EngineSnapshot::load_from_rnote_bytes(bytes)
            .await
            .context("Rnote snapshot load failed")?;
        let mut engine = Engine::default();
        let _ = engine.load_snapshot(snapshot);
        Ok(Self {
            engine,
            stroke_active: false,
            stroke_tool: CanvasStrokeTool::Pen,
        })
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
        assert!(canvas.set_stroke_tool(CanvasStrokeTool::Eraser).is_err());
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

        canvas.set_stroke_tool(CanvasStrokeTool::Eraser).unwrap();
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
            assert_eq!(restored.stroke_tool(), CanvasStrokeTool::Pen);
            let svg_after = restored.export_svg().await.unwrap();
            assert!(svg_after.len() > 200, "reloaded SVG unexpectedly empty");

            let state = restored.debug_state_json().unwrap();
            assert!(state.contains("stroke_components"));
        });
    }
}
