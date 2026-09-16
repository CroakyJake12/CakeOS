use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;

use futures::executor::block_on;

use crate::{
    CanvasBrushBuilder, CanvasBrushStyle, CanvasCoordinateSpace, CanvasDocExportFormat,
    CanvasEraserStyle, CanvasLayout, CanvasPattern, CanvasPointerSample, CanvasRenderFormat,
    CanvasRgba, CanvasSelectorStyle, CanvasShaperStyle, CanvasShape, CanvasTool, CanvasToolsStyle,
    CanvasViewport, HeadlessCanvasEngine,
};

pub const CAKE_CANVAS_ABI_VERSION: u32 = 3;
/// Minimum ABI the managed bridge accepts (v2 native libraries keep working
/// for the original stroke/viewport/history/render/persist surface).
pub const CAKE_CANVAS_ABI_VERSION_MIN: u32 = 2;
pub const CAKE_CANVAS_TOOL_PEN: u32 = 0;
pub const CAKE_CANVAS_TOOL_HIGHLIGHTER: u32 = 1;
pub const CAKE_CANVAS_TOOL_ERASER: u32 = 2;
pub const CAKE_CANVAS_TOOL_SELECTOR: u32 = 3;
pub const CAKE_CANVAS_TOOL_SHAPE: u32 = 4;
pub const CAKE_CANVAS_TOOL_TYPEWRITER: u32 = 5;
pub const CAKE_CANVAS_TOOL_TOOLS: u32 = 6;
pub const CAKE_CANVAS_SHAPE_RECTANGLE: u32 = 0;
pub const CAKE_CANVAS_SHAPE_ELLIPSE: u32 = 1;
pub const CAKE_CANVAS_SHAPE_LINE: u32 = 2;
pub const CAKE_CANVAS_SHAPE_ARROW: u32 = 3;
pub const CAKE_CANVAS_SHAPE_GRID: u32 = 4;
pub const CAKE_CANVAS_SHAPE_COORD_SYSTEM_2D: u32 = 5;
pub const CAKE_CANVAS_SHAPE_COORD_SYSTEM_3D: u32 = 6;
pub const CAKE_CANVAS_SHAPE_QUADRANT_COORD_SYSTEM_2D: u32 = 7;
pub const CAKE_CANVAS_SHAPE_FOCI_ELLIPSE: u32 = 8;
pub const CAKE_CANVAS_SHAPE_QUADBEZ: u32 = 9;
pub const CAKE_CANVAS_SHAPE_CUBBEZ: u32 = 10;
pub const CAKE_CANVAS_SHAPE_POLYLINE: u32 = 11;
pub const CAKE_CANVAS_SHAPE_POLYGON: u32 = 12;
pub const CAKE_CANVAS_BRUSH_MARKER: u32 = 0;
pub const CAKE_CANVAS_BRUSH_SOLID: u32 = 1;
pub const CAKE_CANVAS_BRUSH_TEXTURED: u32 = 2;
pub const CAKE_CANVAS_BRUSH_BUILDER_SIMPLE: u32 = 0;
pub const CAKE_CANVAS_BRUSH_BUILDER_CURVED: u32 = 1;
pub const CAKE_CANVAS_BRUSH_BUILDER_MODELED: u32 = 2;
pub const CAKE_CANVAS_SHAPER_SMOOTH: u32 = 0;
pub const CAKE_CANVAS_SHAPER_ROUGH: u32 = 1;
pub const CAKE_CANVAS_ERASER_TRASH: u32 = 0;
pub const CAKE_CANVAS_ERASER_SPLIT: u32 = 1;
pub const CAKE_CANVAS_SELECTOR_POLYGON: u32 = 0;
pub const CAKE_CANVAS_SELECTOR_RECTANGLE: u32 = 1;
pub const CAKE_CANVAS_SELECTOR_SINGLE: u32 = 2;
pub const CAKE_CANVAS_SELECTOR_INTERSECTING_PATH: u32 = 3;
pub const CAKE_CANVAS_TOOLS_VERTICAL_SPACE: u32 = 0;
pub const CAKE_CANVAS_TOOLS_OFFSET_CAMERA: u32 = 1;
pub const CAKE_CANVAS_TOOLS_ZOOM: u32 = 2;
pub const CAKE_CANVAS_TOOLS_LASER: u32 = 3;
pub const CAKE_CANVAS_LAYOUT_FIXED_SIZE: u32 = 0;
pub const CAKE_CANVAS_LAYOUT_CONTINUOUS_VERTICAL: u32 = 1;
pub const CAKE_CANVAS_LAYOUT_SEMI_INFINITE: u32 = 2;
pub const CAKE_CANVAS_LAYOUT_INFINITE: u32 = 3;
pub const CAKE_CANVAS_PATTERN_NONE: u32 = 0;
pub const CAKE_CANVAS_PATTERN_LINES: u32 = 1;
pub const CAKE_CANVAS_PATTERN_GRID: u32 = 2;
pub const CAKE_CANVAS_PATTERN_DOTS: u32 = 3;
pub const CAKE_CANVAS_PATTERN_ISOMETRIC_GRID: u32 = 4;
pub const CAKE_CANVAS_PATTERN_ISOMETRIC_DOTS: u32 = 5;
pub const CAKE_CANVAS_EXPORT_SVG: u32 = 0;
pub const CAKE_CANVAS_EXPORT_PDF: u32 = 1;
pub const CAKE_CANVAS_EXPORT_XOPP: u32 = 2;
pub const CAKE_CANVAS_RENDER_FORMAT_SVG: u32 = 1;
pub const CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT: u32 = 1;

#[repr(i32)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CakeCanvasStatus {
    Ok = 0,
    NoChange = 1,
    InvalidHandle = -1,
    InvalidArgument = -2,
    InvalidState = -3,
    EngineError = -4,
    Panic = -5,
}

#[repr(C)]
#[derive(Debug, Clone, Copy)]
pub struct CakeCanvasPointerSample {
    pub x: f64,
    pub y: f64,
    pub pressure: f64,
    pub tilt_x: f64,
    pub tilt_y: f64,
}

impl CakeCanvasPointerSample {
    fn into_internal(self) -> Option<CanvasPointerSample> {
        if !self.x.is_finite()
            || !self.y.is_finite()
            || !self.pressure.is_finite()
            || !self.tilt_x.is_finite()
            || !self.tilt_y.is_finite()
        {
            return None;
        }

        let mut sample = CanvasPointerSample::new(self.x, self.y, self.pressure);
        sample.tilt_x = self.tilt_x;
        sample.tilt_y = self.tilt_y;
        Some(sample)
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct CakeCanvasBuffer {
    pub data: *mut u8,
    pub len: usize,
}

impl Default for CakeCanvasBuffer {
    fn default() -> Self {
        Self {
            data: ptr::null_mut(),
            len: 0,
        }
    }
}

#[repr(C)]
#[derive(Debug)]
pub struct CakeCanvasRenderFrame {
    pub format: u32,
    pub coordinate_space: u32,
    pub x: f64,
    pub y: f64,
    pub width: f64,
    pub height: f64,
    pub data: *mut u8,
    pub len: usize,
}

#[repr(C)]
#[derive(Debug, Default)]
pub struct CakeCanvasViewport {
    pub center_x: f64,
    pub center_y: f64,
    pub zoom: f64,
}

#[repr(C)]
#[derive(Debug, Clone, Copy, Default)]
pub struct CakeCanvasRgba {
    pub r: f64,
    pub g: f64,
    pub b: f64,
    pub a: f64,
}

impl CakeCanvasRgba {
    fn into_internal(self) -> Option<CanvasRgba> {
        if !self.r.is_finite() || !self.g.is_finite() || !self.b.is_finite() || !self.a.is_finite() {
            return None;
        }
        Some(CanvasRgba::new(self.r, self.g, self.b, self.a))
    }

    fn from_internal(color: CanvasRgba) -> Self {
        Self { r: color.r, g: color.g, b: color.b, a: color.a }
    }
}

#[repr(C)]
#[derive(Debug, Default)]
pub struct CakeCanvasFormatSize {
    pub width: f64,
    pub height: f64,
    pub dpi: f64,
}

impl Default for CakeCanvasRenderFrame {
    fn default() -> Self {
        Self {
            format: 0,
            coordinate_space: 0,
            x: 0.0,
            y: 0.0,
            width: 0.0,
            height: 0.0,
            data: ptr::null_mut(),
            len: 0,
        }
    }
}

fn guard_status(operation: impl FnOnce() -> CakeCanvasStatus) -> CakeCanvasStatus {
    catch_unwind(AssertUnwindSafe(operation)).unwrap_or(CakeCanvasStatus::Panic)
}

fn with_engine_mut<T>(
    handle: *mut c_void,
    operation: impl FnOnce(&mut HeadlessCanvasEngine) -> T,
) -> Option<T> {
    if handle.is_null() {
        return None;
    }

    let engine = unsafe { handle.cast::<HeadlessCanvasEngine>().as_mut()? };
    Some(operation(engine))
}

fn with_engine<T>(
    handle: *const c_void,
    operation: impl FnOnce(&HeadlessCanvasEngine) -> T,
) -> Option<T> {
    if handle.is_null() {
        return None;
    }

    let engine = unsafe { handle.cast::<HeadlessCanvasEngine>().as_ref()? };
    Some(operation(engine))
}

fn owned_buffer(bytes: Vec<u8>) -> CakeCanvasBuffer {
    if bytes.is_empty() {
        return CakeCanvasBuffer::default();
    }

    let boxed = bytes.into_boxed_slice();
    let len = boxed.len();
    let data = Box::into_raw(boxed).cast::<u8>();
    CakeCanvasBuffer { data, len }
}

unsafe fn release_owned_bytes(data: *mut u8, len: usize) {
    if data.is_null() || len == 0 {
        return;
    }

    let raw_slice = ptr::slice_from_raw_parts_mut(data, len);
    unsafe {
        drop(Box::from_raw(raw_slice));
    }
}

fn tool_from_abi(tool: u32) -> Option<CanvasTool> {
    match tool {
        CAKE_CANVAS_TOOL_PEN => Some(CanvasTool::Pen),
        CAKE_CANVAS_TOOL_HIGHLIGHTER => Some(CanvasTool::Highlighter),
        CAKE_CANVAS_TOOL_ERASER => Some(CanvasTool::Eraser),
        CAKE_CANVAS_TOOL_SELECTOR => Some(CanvasTool::Selector),
        CAKE_CANVAS_TOOL_SHAPE => Some(CanvasTool::Shape),
        CAKE_CANVAS_TOOL_TYPEWRITER => Some(CanvasTool::Typewriter),
        CAKE_CANVAS_TOOL_TOOLS => Some(CanvasTool::Tools),
        _ => None,
    }
}

fn shape_from_abi(shape: u32) -> Option<CanvasShape> {
    match shape {
        CAKE_CANVAS_SHAPE_RECTANGLE => Some(CanvasShape::Rectangle),
        CAKE_CANVAS_SHAPE_ELLIPSE => Some(CanvasShape::Ellipse),
        CAKE_CANVAS_SHAPE_LINE => Some(CanvasShape::Line),
        CAKE_CANVAS_SHAPE_ARROW => Some(CanvasShape::Arrow),
        CAKE_CANVAS_SHAPE_GRID => Some(CanvasShape::Grid),
        CAKE_CANVAS_SHAPE_COORD_SYSTEM_2D => Some(CanvasShape::CoordSystem2D),
        CAKE_CANVAS_SHAPE_COORD_SYSTEM_3D => Some(CanvasShape::CoordSystem3D),
        CAKE_CANVAS_SHAPE_QUADRANT_COORD_SYSTEM_2D => Some(CanvasShape::QuadrantCoordSystem2D),
        CAKE_CANVAS_SHAPE_FOCI_ELLIPSE => Some(CanvasShape::FociEllipse),
        CAKE_CANVAS_SHAPE_QUADBEZ => Some(CanvasShape::QuadBez),
        CAKE_CANVAS_SHAPE_CUBBEZ => Some(CanvasShape::CubBez),
        CAKE_CANVAS_SHAPE_POLYLINE => Some(CanvasShape::Polyline),
        CAKE_CANVAS_SHAPE_POLYGON => Some(CanvasShape::Polygon),
        _ => None,
    }
}

fn brush_style_from_abi(style: u32) -> Option<CanvasBrushStyle> {
    match style {
        CAKE_CANVAS_BRUSH_MARKER => Some(CanvasBrushStyle::Marker),
        CAKE_CANVAS_BRUSH_SOLID => Some(CanvasBrushStyle::Solid),
        CAKE_CANVAS_BRUSH_TEXTURED => Some(CanvasBrushStyle::Textured),
        _ => None,
    }
}

fn brush_style_to_abi(style: CanvasBrushStyle) -> u32 {
    match style {
        CanvasBrushStyle::Marker => CAKE_CANVAS_BRUSH_MARKER,
        CanvasBrushStyle::Solid => CAKE_CANVAS_BRUSH_SOLID,
        CanvasBrushStyle::Textured => CAKE_CANVAS_BRUSH_TEXTURED,
    }
}

fn brush_builder_from_abi(builder: u32) -> Option<CanvasBrushBuilder> {
    match builder {
        CAKE_CANVAS_BRUSH_BUILDER_SIMPLE => Some(CanvasBrushBuilder::Simple),
        CAKE_CANVAS_BRUSH_BUILDER_CURVED => Some(CanvasBrushBuilder::Curved),
        CAKE_CANVAS_BRUSH_BUILDER_MODELED => Some(CanvasBrushBuilder::Modeled),
        _ => None,
    }
}

fn brush_builder_to_abi(builder: CanvasBrushBuilder) -> u32 {
    match builder {
        CanvasBrushBuilder::Simple => CAKE_CANVAS_BRUSH_BUILDER_SIMPLE,
        CanvasBrushBuilder::Curved => CAKE_CANVAS_BRUSH_BUILDER_CURVED,
        CanvasBrushBuilder::Modeled => CAKE_CANVAS_BRUSH_BUILDER_MODELED,
    }
}

fn shaper_style_from_abi(style: u32) -> Option<CanvasShaperStyle> {
    match style {
        CAKE_CANVAS_SHAPER_SMOOTH => Some(CanvasShaperStyle::Smooth),
        CAKE_CANVAS_SHAPER_ROUGH => Some(CanvasShaperStyle::Rough),
        _ => None,
    }
}

fn shaper_style_to_abi(style: CanvasShaperStyle) -> u32 {
    match style {
        CanvasShaperStyle::Smooth => CAKE_CANVAS_SHAPER_SMOOTH,
        CanvasShaperStyle::Rough => CAKE_CANVAS_SHAPER_ROUGH,
    }
}

fn eraser_style_from_abi(style: u32) -> Option<CanvasEraserStyle> {
    match style {
        CAKE_CANVAS_ERASER_TRASH => Some(CanvasEraserStyle::TrashColliding),
        CAKE_CANVAS_ERASER_SPLIT => Some(CanvasEraserStyle::SplitColliding),
        _ => None,
    }
}

fn eraser_style_to_abi(style: CanvasEraserStyle) -> u32 {
    match style {
        CanvasEraserStyle::TrashColliding => CAKE_CANVAS_ERASER_TRASH,
        CanvasEraserStyle::SplitColliding => CAKE_CANVAS_ERASER_SPLIT,
    }
}

fn selector_style_from_abi(style: u32) -> Option<CanvasSelectorStyle> {
    match style {
        CAKE_CANVAS_SELECTOR_POLYGON => Some(CanvasSelectorStyle::Polygon),
        CAKE_CANVAS_SELECTOR_RECTANGLE => Some(CanvasSelectorStyle::Rectangle),
        CAKE_CANVAS_SELECTOR_SINGLE => Some(CanvasSelectorStyle::Single),
        CAKE_CANVAS_SELECTOR_INTERSECTING_PATH => Some(CanvasSelectorStyle::IntersectingPath),
        _ => None,
    }
}

fn selector_style_to_abi(style: CanvasSelectorStyle) -> u32 {
    match style {
        CanvasSelectorStyle::Polygon => CAKE_CANVAS_SELECTOR_POLYGON,
        CanvasSelectorStyle::Rectangle => CAKE_CANVAS_SELECTOR_RECTANGLE,
        CanvasSelectorStyle::Single => CAKE_CANVAS_SELECTOR_SINGLE,
        CanvasSelectorStyle::IntersectingPath => CAKE_CANVAS_SELECTOR_INTERSECTING_PATH,
    }
}

fn tools_style_from_abi(style: u32) -> Option<CanvasToolsStyle> {
    match style {
        CAKE_CANVAS_TOOLS_VERTICAL_SPACE => Some(CanvasToolsStyle::VerticalSpace),
        CAKE_CANVAS_TOOLS_OFFSET_CAMERA => Some(CanvasToolsStyle::OffsetCamera),
        CAKE_CANVAS_TOOLS_ZOOM => Some(CanvasToolsStyle::Zoom),
        CAKE_CANVAS_TOOLS_LASER => Some(CanvasToolsStyle::Laser),
        _ => None,
    }
}

fn tools_style_to_abi(style: CanvasToolsStyle) -> u32 {
    match style {
        CanvasToolsStyle::VerticalSpace => CAKE_CANVAS_TOOLS_VERTICAL_SPACE,
        CanvasToolsStyle::OffsetCamera => CAKE_CANVAS_TOOLS_OFFSET_CAMERA,
        CanvasToolsStyle::Zoom => CAKE_CANVAS_TOOLS_ZOOM,
        CanvasToolsStyle::Laser => CAKE_CANVAS_TOOLS_LASER,
    }
}

fn layout_from_abi(layout: u32) -> Option<CanvasLayout> {
    match layout {
        CAKE_CANVAS_LAYOUT_FIXED_SIZE => Some(CanvasLayout::FixedSize),
        CAKE_CANVAS_LAYOUT_CONTINUOUS_VERTICAL => Some(CanvasLayout::ContinuousVertical),
        CAKE_CANVAS_LAYOUT_SEMI_INFINITE => Some(CanvasLayout::SemiInfinite),
        CAKE_CANVAS_LAYOUT_INFINITE => Some(CanvasLayout::Infinite),
        _ => None,
    }
}

fn layout_to_abi(layout: CanvasLayout) -> u32 {
    match layout {
        CanvasLayout::FixedSize => CAKE_CANVAS_LAYOUT_FIXED_SIZE,
        CanvasLayout::ContinuousVertical => CAKE_CANVAS_LAYOUT_CONTINUOUS_VERTICAL,
        CanvasLayout::SemiInfinite => CAKE_CANVAS_LAYOUT_SEMI_INFINITE,
        CanvasLayout::Infinite => CAKE_CANVAS_LAYOUT_INFINITE,
    }
}

fn pattern_from_abi(pattern: u32) -> Option<CanvasPattern> {
    match pattern {
        CAKE_CANVAS_PATTERN_NONE => Some(CanvasPattern::None),
        CAKE_CANVAS_PATTERN_LINES => Some(CanvasPattern::Lines),
        CAKE_CANVAS_PATTERN_GRID => Some(CanvasPattern::Grid),
        CAKE_CANVAS_PATTERN_DOTS => Some(CanvasPattern::Dots),
        CAKE_CANVAS_PATTERN_ISOMETRIC_GRID => Some(CanvasPattern::IsometricGrid),
        CAKE_CANVAS_PATTERN_ISOMETRIC_DOTS => Some(CanvasPattern::IsometricDots),
        _ => None,
    }
}

fn pattern_to_abi(pattern: CanvasPattern) -> u32 {
    match pattern {
        CanvasPattern::None => CAKE_CANVAS_PATTERN_NONE,
        CanvasPattern::Lines => CAKE_CANVAS_PATTERN_LINES,
        CanvasPattern::Grid => CAKE_CANVAS_PATTERN_GRID,
        CanvasPattern::Dots => CAKE_CANVAS_PATTERN_DOTS,
        CanvasPattern::IsometricGrid => CAKE_CANVAS_PATTERN_ISOMETRIC_GRID,
        CanvasPattern::IsometricDots => CAKE_CANVAS_PATTERN_ISOMETRIC_DOTS,
    }
}

fn export_format_from_abi(format: u32) -> Option<CanvasDocExportFormat> {
    match format {
        CAKE_CANVAS_EXPORT_SVG => Some(CanvasDocExportFormat::Svg),
        CAKE_CANVAS_EXPORT_PDF => Some(CanvasDocExportFormat::Pdf),
        CAKE_CANVAS_EXPORT_XOPP => Some(CanvasDocExportFormat::Xopp),
        _ => None,
    }
}

fn render_format_to_abi(format: CanvasRenderFormat) -> u32 {
    match format {
        CanvasRenderFormat::Svg => CAKE_CANVAS_RENDER_FORMAT_SVG,
    }
}

fn coordinate_space_to_abi(space: CanvasCoordinateSpace) -> u32 {
    match space {
        CanvasCoordinateSpace::Document => CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT,
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_abi_version() -> u32 {
    CAKE_CANVAS_ABI_VERSION
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_new() -> *mut c_void {
    catch_unwind(AssertUnwindSafe(|| {
        Box::into_raw(Box::new(HeadlessCanvasEngine::new())).cast::<c_void>()
    }))
    .unwrap_or(ptr::null_mut())
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_free(handle: *mut c_void) {
    if handle.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        drop(Box::from_raw(handle.cast::<HeadlessCanvasEngine>()));
    }));
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_engine_from_rnote(
    data: *const u8,
    len: usize,
    out_handle: *mut *mut c_void,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_handle.is_null() || data.is_null() || len == 0 {
            return CakeCanvasStatus::InvalidArgument;
        }

        unsafe {
            *out_handle = ptr::null_mut();
        }

        let bytes = unsafe { slice::from_raw_parts(data, len) }.to_vec();
        match block_on(HeadlessCanvasEngine::from_rnote(bytes)) {
            Ok(engine) => {
                unsafe {
                    *out_handle = Box::into_raw(Box::new(engine)).cast::<c_void>();
                }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_stroke_tool(
    handle: *mut c_void,
    tool: u32,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(tool) = tool_from_abi(tool) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_tool(tool)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_shape(
    handle: *mut c_void,
    shape: u32,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(shape) = shape_from_abi(shape) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_shape(shape)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_abi_version_min() -> u32 {
    CAKE_CANVAS_ABI_VERSION_MIN
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_brush_style(handle: *mut c_void, style: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(style) = brush_style_from_abi(style) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_brush_style(style)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_brush_style(handle: *const c_void, out_style: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_style.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(style) = with_engine(handle, HeadlessCanvasEngine::brush_style) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_style = brush_style_to_abi(style); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_brush_builder(handle: *mut c_void, builder: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(builder) = brush_builder_from_abi(builder) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_brush_builder(builder)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_brush_builder(handle: *const c_void, out_builder: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_builder.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(builder) = with_engine(handle, HeadlessCanvasEngine::brush_builder) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_builder = brush_builder_to_abi(builder); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_stroke_width(handle: *mut c_void, width: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_stroke_width(width)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_stroke_width(handle: *const c_void, out_width: *mut f64) -> CakeCanvasStatus {
    guard_status(|| {
        if out_width.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(width) = with_engine(handle, HeadlessCanvasEngine::stroke_width) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_width = width; }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_stroke_color(handle: *mut c_void, color: CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(color) = color.into_internal() else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_stroke_color(color)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_stroke_color(handle: *const c_void, out_color: *mut CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        if out_color.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(color) = with_engine(handle, HeadlessCanvasEngine::stroke_color) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_color = CakeCanvasRgba::from_internal(color); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_fill_color(handle: *mut c_void, color: CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(color) = color.into_internal() else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_fill_color(color)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_fill_color(handle: *const c_void, out_color: *mut CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        if out_color.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(color) = with_engine(handle, HeadlessCanvasEngine::fill_color) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_color = CakeCanvasRgba::from_internal(color); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_eraser_width(handle: *mut c_void, width: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_eraser_width(width)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_eraser_width(handle: *const c_void, out_width: *mut f64) -> CakeCanvasStatus {
    guard_status(|| {
        if out_width.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(width) = with_engine(handle, HeadlessCanvasEngine::eraser_width) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_width = width; }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_eraser_style(handle: *mut c_void, style: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(style) = eraser_style_from_abi(style) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_eraser_style(style)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_eraser_style(handle: *const c_void, out_style: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_style.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(style) = with_engine(handle, HeadlessCanvasEngine::eraser_style) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_style = eraser_style_to_abi(style); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_shaper_style(handle: *mut c_void, style: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(style) = shaper_style_from_abi(style) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_shaper_style(style)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_shaper_style(handle: *const c_void, out_style: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_style.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(style) = with_engine(handle, HeadlessCanvasEngine::shaper_style) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_style = shaper_style_to_abi(style); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_shaper_constraints_enabled(handle: *mut c_void, enabled: u8) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_shaper_constraints_enabled(enabled != 0)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_shaper_constraints_enabled(handle: *const c_void, out_enabled: *mut u8) -> CakeCanvasStatus {
    guard_status(|| {
        if out_enabled.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(enabled) = with_engine(handle, HeadlessCanvasEngine::shaper_constraints_enabled) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_enabled = u8::from(enabled); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_selector_style(handle: *mut c_void, style: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(style) = selector_style_from_abi(style) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_selector_style(style)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_selector_style(handle: *const c_void, out_style: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_style.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(style) = with_engine(handle, HeadlessCanvasEngine::selector_style) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_style = selector_style_to_abi(style); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_selector_lock_aspect(handle: *mut c_void, lock: u8) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_selector_lock_aspect(lock != 0)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_selector_lock_aspect(handle: *const c_void, out_lock: *mut u8) -> CakeCanvasStatus {
    guard_status(|| {
        if out_lock.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(locked) = with_engine(handle, HeadlessCanvasEngine::selector_lock_aspect) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_lock = u8::from(locked); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_tools_style(handle: *mut c_void, style: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(style) = tools_style_from_abi(style) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_tools_style(style)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_tools_style(handle: *const c_void, out_style: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_style.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(style) = with_engine(handle, HeadlessCanvasEngine::tools_style) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_style = tools_style_to_abi(style); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_typewriter_font_size(handle: *mut c_void, size: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_typewriter_font_size(size)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_typewriter_font_size(handle: *const c_void, out_size: *mut f64) -> CakeCanvasStatus {
    guard_status(|| {
        if out_size.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(size) = with_engine(handle, HeadlessCanvasEngine::typewriter_font_size) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_size = size; }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_typewriter_text_width(handle: *mut c_void, width: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_typewriter_text_width(width)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_typewriter_text_width(handle: *const c_void, out_width: *mut f64) -> CakeCanvasStatus {
    guard_status(|| {
        if out_width.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(width) = with_engine(handle, HeadlessCanvasEngine::typewriter_text_width) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_width = width; }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_layout(handle: *mut c_void, layout: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(layout) = layout_from_abi(layout) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_layout(layout)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_layout(handle: *const c_void, out_layout: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_layout.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(layout) = with_engine(handle, HeadlessCanvasEngine::layout) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_layout = layout_to_abi(layout); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_background_pattern(handle: *mut c_void, pattern: u32) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(pattern) = pattern_from_abi(pattern) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_background_pattern(pattern)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_background_pattern(handle: *const c_void, out_pattern: *mut u32) -> CakeCanvasStatus {
    guard_status(|| {
        if out_pattern.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(pattern) = with_engine(handle, HeadlessCanvasEngine::background_pattern) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_pattern = pattern_to_abi(pattern); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_background_color(handle: *mut c_void, color: CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(color) = color.into_internal() else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_background_color(color)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_background_color(handle: *const c_void, out_color: *mut CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        if out_color.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(color) = with_engine(handle, HeadlessCanvasEngine::background_color) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe { *out_color = CakeCanvasRgba::from_internal(color); }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_pattern_color(handle: *mut c_void, color: CakeCanvasRgba) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(color) = color.into_internal() else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| engine.set_pattern_color(color)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_pattern_size(handle: *mut c_void, width: f64, height: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_pattern_size(width, height)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_format_size(handle: *mut c_void, width: f64, height: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_format_size(width, height)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_format_size(handle: *const c_void, out_size: *mut CakeCanvasFormatSize) -> CakeCanvasStatus {
    guard_status(|| {
        if out_size.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(size) = with_engine(handle, HeadlessCanvasEngine::format_size) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe {
            *out_size = CakeCanvasFormatSize { width: size.width, height: size.height, dpi: size.dpi };
        }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_format_dpi(handle: *mut c_void, dpi: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_format_dpi(dpi)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_snap_positions(handle: *mut c_void, snap: u8) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_snap_positions(snap != 0)) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_export_prefs(
    handle: *mut c_void,
    with_background: u8,
    with_pattern: u8,
    optimize_printing: u8,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| {
            engine.set_export_prefs(with_background != 0, with_pattern != 0, optimize_printing != 0)
        }) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_export_doc(
    handle: *const c_void,
    format: u32,
    out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_buffer.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(format) = export_format_from_abi(format) else {
            return CakeCanvasStatus::InvalidArgument;
        };
        unsafe {
            *out_buffer = CakeCanvasBuffer::default();
        }
        let Some(result) = with_engine(handle, |engine| block_on(engine.export_doc_bytes(format))) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(bytes) => {
                unsafe {
                    *out_buffer = owned_buffer(bytes);
                }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_export_selection_svg(
    handle: *const c_void,
    out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_buffer.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe {
            *out_buffer = CakeCanvasBuffer::default();
        }
        let Some(result) = with_engine(handle, |engine| block_on(engine.export_selection_svg())) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(Some(bytes)) => {
                unsafe {
                    *out_buffer = owned_buffer(bytes);
                }
                CakeCanvasStatus::Ok
            }
            Ok(None) => CakeCanvasStatus::NoChange,
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_viewport_size(
    handle: *mut c_void,
    width: f64,
    height: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_viewport_size(width, height)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_zoom_to(handle: *mut c_void, zoom: f64) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.zoom_to(zoom)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_pan_by(
    handle: *mut c_void,
    delta_x: f64,
    delta_y: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.pan_by(delta_x, delta_y)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_set_viewport_center(
    handle: *mut c_void,
    center_x: f64,
    center_y: f64,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, |engine| engine.set_viewport_center(center_x, center_y)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidArgument,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_get_viewport(
    handle: *const c_void,
    out_viewport: *mut CakeCanvasViewport,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_viewport.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        let Some(viewport) = with_engine(handle, HeadlessCanvasEngine::viewport) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        unsafe {
            *out_viewport = CakeCanvasViewport {
                center_x: viewport.center_x,
                center_y: viewport.center_y,
                zoom: viewport.zoom,
            };
        }
        CakeCanvasStatus::Ok
    })
}

fn stroke_event(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
    operation: impl FnOnce(&mut HeadlessCanvasEngine, CanvasPointerSample) -> anyhow::Result<()>,
) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(sample) = sample.into_internal() else {
            return CakeCanvasStatus::InvalidArgument;
        };
        let Some(result) = with_engine_mut(handle, |engine| operation(engine, sample)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_begin_stroke(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
) -> CakeCanvasStatus {
    stroke_event(handle, sample, HeadlessCanvasEngine::begin_stroke)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_update_stroke(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
) -> CakeCanvasStatus {
    stroke_event(handle, sample, HeadlessCanvasEngine::update_stroke)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_end_stroke(
    handle: *mut c_void,
    sample: CakeCanvasPointerSample,
) -> CakeCanvasStatus {
    stroke_event(handle, sample, HeadlessCanvasEngine::end_stroke)
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_can_undo(handle: *const c_void) -> u8 {
    with_engine(handle, HeadlessCanvasEngine::can_undo)
        .unwrap_or(false)
        .into()
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_can_redo(handle: *const c_void) -> u8 {
    with_engine(handle, HeadlessCanvasEngine::can_redo)
        .unwrap_or(false)
        .into()
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_undo(handle: *mut c_void) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, HeadlessCanvasEngine::undo) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(true) => CakeCanvasStatus::Ok,
            Ok(false) => CakeCanvasStatus::NoChange,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_redo(handle: *mut c_void) -> CakeCanvasStatus {
    guard_status(|| {
        let Some(result) = with_engine_mut(handle, HeadlessCanvasEngine::redo) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(true) => CakeCanvasStatus::Ok,
            Ok(false) => CakeCanvasStatus::NoChange,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_render_frame(
    handle: *const c_void,
    out_frame: *mut CakeCanvasRenderFrame,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_frame.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe {
            *out_frame = CakeCanvasRenderFrame::default();
        }

        let Some(result) = with_engine(handle, |engine| block_on(engine.render_frame())) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        let frame = match result {
            Ok(frame) => frame,
            Err(_) => return CakeCanvasStatus::EngineError,
        };
        let buffer = owned_buffer(frame.bytes);

        unsafe {
            *out_frame = CakeCanvasRenderFrame {
                format: render_format_to_abi(frame.format),
                coordinate_space: coordinate_space_to_abi(frame.coordinate_space),
                x: frame.bounds.x,
                y: frame.bounds.y,
                width: frame.bounds.width,
                height: frame.bounds.height,
                data: buffer.data,
                len: buffer.len,
            };
        }
        CakeCanvasStatus::Ok
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_render_frame_release(frame: *mut CakeCanvasRenderFrame) {
    if frame.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        let frame_ref = &mut *frame;
        release_owned_bytes(frame_ref.data, frame_ref.len);
        *frame_ref = CakeCanvasRenderFrame::default();
    }));
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_save_rnote(
    handle: *const c_void,
    out_buffer: *mut CakeCanvasBuffer,
) -> CakeCanvasStatus {
    guard_status(|| {
        if out_buffer.is_null() {
            return CakeCanvasStatus::InvalidArgument;
        }
        unsafe {
            *out_buffer = CakeCanvasBuffer::default();
        }

        let Some(result) = with_engine(handle, |engine| block_on(engine.save_rnote())) else {
            return CakeCanvasStatus::InvalidHandle;
        };
        match result {
            Ok(bytes) => {
                unsafe {
                    *out_buffer = owned_buffer(bytes);
                }
                CakeCanvasStatus::Ok
            }
            Err(_) => CakeCanvasStatus::EngineError,
        }
    })
}

#[unsafe(no_mangle)]
pub extern "C" fn cake_canvas_buffer_release(buffer: *mut CakeCanvasBuffer) {
    if buffer.is_null() {
        return;
    }

    let _ = catch_unwind(AssertUnwindSafe(|| unsafe {
        let buffer_ref = &mut *buffer;
        release_owned_bytes(buffer_ref.data, buffer_ref.len);
        *buffer_ref = CakeCanvasBuffer::default();
    }));
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample(x: f64, y: f64, pressure: f64) -> CakeCanvasPointerSample {
        CakeCanvasPointerSample {
            x,
            y,
            pressure,
            tilt_x: 0.0,
            tilt_y: 0.0,
        }
    }

    #[test]
    fn native_bridge_draws_renders_saves_and_reloads() {
        let handle = cake_canvas_engine_new();
        assert!(!handle.is_null());
        assert_eq!(cake_canvas_abi_version(), 3);
        assert_eq!(cake_canvas_abi_version_min(), 2);

        assert_eq!(cake_canvas_begin_stroke(handle, sample(120.0, 120.0, 0.2)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_update_stroke(handle, sample(180.0, 155.0, 0.6)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(240.0, 200.0, 0.8)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_can_undo(handle), 1);

        assert_eq!(
            cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_HIGHLIGHTER),
            CakeCanvasStatus::Ok
        );
        assert_eq!(cake_canvas_begin_stroke(handle, sample(120.0, 180.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(240.0, 180.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_shape(handle, CAKE_CANVAS_SHAPE_ELLIPSE), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_SHAPE), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_begin_stroke(handle, sample(270.0, 100.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(340.0, 170.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_viewport_size(handle, 1280.0, 720.0), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_zoom_to(handle, 1.5), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_pan_by(handle, 100.0, -50.0), CakeCanvasStatus::Ok);
        let mut viewport = CakeCanvasViewport::default();
        assert_eq!(cake_canvas_get_viewport(handle, &mut viewport), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_viewport_center(handle, viewport.center_x, viewport.center_y), CakeCanvasStatus::Ok);

        let mut frame = CakeCanvasRenderFrame::default();
        assert_eq!(cake_canvas_render_frame(handle, &mut frame), CakeCanvasStatus::Ok);
        assert_eq!(frame.format, CAKE_CANVAS_RENDER_FORMAT_SVG);
        assert_eq!(frame.coordinate_space, CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT);
        assert!(frame.width > 0.0 && frame.height > 0.0);
        assert!(frame.len > 200);
        let svg = unsafe { slice::from_raw_parts(frame.data, frame.len) };
        assert!(std::str::from_utf8(svg).unwrap().contains("<svg"));
        cake_canvas_render_frame_release(&mut frame);
        assert!(frame.data.is_null());
        assert_eq!(frame.len, 0);

        let mut native = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_save_rnote(handle, &mut native), CakeCanvasStatus::Ok);
        assert!(native.len > 100);

        let mut restored = ptr::null_mut();
        assert_eq!(
            cake_canvas_engine_from_rnote(native.data, native.len, &mut restored),
            CakeCanvasStatus::Ok
        );
        assert!(!restored.is_null());

        let mut restored_frame = CakeCanvasRenderFrame::default();
        assert_eq!(
            cake_canvas_render_frame(restored, &mut restored_frame),
            CakeCanvasStatus::Ok
        );
        assert!(restored_frame.len > 200);

        cake_canvas_render_frame_release(&mut restored_frame);
        cake_canvas_buffer_release(&mut native);
        cake_canvas_engine_free(restored);
        cake_canvas_engine_free(handle);
    }

    #[test]
    fn native_bridge_v3_parity_config_export_and_validation() {
        let handle = cake_canvas_engine_new();
        assert!(!handle.is_null());

        // New tools + full shape set (v2 numbering preserved for the first four).
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_TYPEWRITER), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_TOOLS), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_PEN), CakeCanvasStatus::Ok);
        for shape in [
            CAKE_CANVAS_SHAPE_RECTANGLE, CAKE_CANVAS_SHAPE_ELLIPSE, CAKE_CANVAS_SHAPE_LINE,
            CAKE_CANVAS_SHAPE_ARROW, CAKE_CANVAS_SHAPE_GRID, CAKE_CANVAS_SHAPE_COORD_SYSTEM_2D,
            CAKE_CANVAS_SHAPE_COORD_SYSTEM_3D, CAKE_CANVAS_SHAPE_QUADRANT_COORD_SYSTEM_2D,
            CAKE_CANVAS_SHAPE_FOCI_ELLIPSE, CAKE_CANVAS_SHAPE_QUADBEZ, CAKE_CANVAS_SHAPE_CUBBEZ,
            CAKE_CANVAS_SHAPE_POLYLINE, CAKE_CANVAS_SHAPE_POLYGON,
        ] {
            assert_eq!(cake_canvas_set_shape(handle, shape), CakeCanvasStatus::Ok);
        }
        assert_eq!(cake_canvas_set_shape(handle, 999), CakeCanvasStatus::InvalidArgument);

        // Brush / stroke / fill.
        assert_eq!(cake_canvas_set_brush_style(handle, CAKE_CANVAS_BRUSH_TEXTURED), CakeCanvasStatus::Ok);
        let mut brush_style = 99u32;
        assert_eq!(cake_canvas_get_brush_style(handle, &mut brush_style), CakeCanvasStatus::Ok);
        assert_eq!(brush_style, CAKE_CANVAS_BRUSH_TEXTURED);
        assert_eq!(cake_canvas_set_brush_builder(handle, CAKE_CANVAS_BRUSH_BUILDER_CURVED), CakeCanvasStatus::Ok);
        let mut brush_builder = 99u32;
        assert_eq!(cake_canvas_get_brush_builder(handle, &mut brush_builder), CakeCanvasStatus::Ok);
        assert_eq!(brush_builder, CAKE_CANVAS_BRUSH_BUILDER_CURVED);
        assert_eq!(cake_canvas_set_stroke_width(handle, 7.5), CakeCanvasStatus::Ok);
        let mut width = 0.0;
        assert_eq!(cake_canvas_get_stroke_width(handle, &mut width), CakeCanvasStatus::Ok);
        assert!((width - 7.5).abs() < 1e-9);
        assert_eq!(cake_canvas_set_stroke_width(handle, -1.0), CakeCanvasStatus::InvalidArgument);
        let red = CakeCanvasRgba { r: 0.9, g: 0.1, b: 0.2, a: 1.0 };
        assert_eq!(cake_canvas_set_stroke_color(handle, red), CakeCanvasStatus::Ok);
        let mut got = CakeCanvasRgba::default();
        assert_eq!(cake_canvas_get_stroke_color(handle, &mut got), CakeCanvasStatus::Ok);
        assert!((got.r - 0.9).abs() < 0.02);
        assert_eq!(cake_canvas_set_fill_color(handle, CakeCanvasRgba { r: 0.0, g: 0.0, b: 0.0, a: 0.0 }), CakeCanvasStatus::Ok);
        let nan_color = CakeCanvasRgba { r: f64::NAN, g: 0.0, b: 0.0, a: 1.0 };
        assert_eq!(cake_canvas_set_stroke_color(handle, nan_color), CakeCanvasStatus::InvalidArgument);

        // Eraser / shaper / selector / tools / typewriter.
        assert_eq!(cake_canvas_set_eraser_width(handle, 21.0), CakeCanvasStatus::Ok);
        let mut eraser_width = 0.0;
        assert_eq!(cake_canvas_get_eraser_width(handle, &mut eraser_width), CakeCanvasStatus::Ok);
        assert!((eraser_width - 21.0).abs() < 1e-9);
        assert_eq!(cake_canvas_set_eraser_style(handle, CAKE_CANVAS_ERASER_SPLIT), CakeCanvasStatus::Ok);
        let mut eraser_style = 99u32;
        assert_eq!(cake_canvas_get_eraser_style(handle, &mut eraser_style), CakeCanvasStatus::Ok);
        assert_eq!(eraser_style, CAKE_CANVAS_ERASER_SPLIT);
        assert_eq!(cake_canvas_set_shaper_style(handle, CAKE_CANVAS_SHAPER_ROUGH), CakeCanvasStatus::Ok);
        let mut shaper_style = 99u32;
        assert_eq!(cake_canvas_get_shaper_style(handle, &mut shaper_style), CakeCanvasStatus::Ok);
        assert_eq!(shaper_style, CAKE_CANVAS_SHAPER_ROUGH);
        assert_eq!(cake_canvas_set_shaper_constraints_enabled(handle, 1), CakeCanvasStatus::Ok);
        let mut constraints = 0u8;
        assert_eq!(cake_canvas_get_shaper_constraints_enabled(handle, &mut constraints), CakeCanvasStatus::Ok);
        assert_eq!(constraints, 1);
        assert_eq!(cake_canvas_set_selector_style(handle, CAKE_CANVAS_SELECTOR_POLYGON), CakeCanvasStatus::Ok);
        let mut selector_style = 99u32;
        assert_eq!(cake_canvas_get_selector_style(handle, &mut selector_style), CakeCanvasStatus::Ok);
        assert_eq!(selector_style, CAKE_CANVAS_SELECTOR_POLYGON);
        assert_eq!(cake_canvas_set_selector_lock_aspect(handle, 1), CakeCanvasStatus::Ok);
        let mut lock_aspect = 0u8;
        assert_eq!(cake_canvas_get_selector_lock_aspect(handle, &mut lock_aspect), CakeCanvasStatus::Ok);
        assert_eq!(lock_aspect, 1);
        assert_eq!(cake_canvas_set_tools_style(handle, CAKE_CANVAS_TOOLS_LASER), CakeCanvasStatus::Ok);
        let mut tools_style = 99u32;
        assert_eq!(cake_canvas_get_tools_style(handle, &mut tools_style), CakeCanvasStatus::Ok);
        assert_eq!(tools_style, CAKE_CANVAS_TOOLS_LASER);
        assert_eq!(cake_canvas_set_typewriter_font_size(handle, 42.0), CakeCanvasStatus::Ok);
        let mut font_size = 0.0;
        assert_eq!(cake_canvas_get_typewriter_font_size(handle, &mut font_size), CakeCanvasStatus::Ok);
        assert!((font_size - 42.0).abs() < 1e-9);
        assert_eq!(cake_canvas_set_typewriter_text_width(handle, 820.0), CakeCanvasStatus::Ok);
        let mut text_width = 0.0;
        assert_eq!(cake_canvas_get_typewriter_text_width(handle, &mut text_width), CakeCanvasStatus::Ok);
        assert!((text_width - 820.0).abs() < 1e-9);
        assert_eq!(cake_canvas_set_typewriter_font_size(handle, 0.0), CakeCanvasStatus::InvalidArgument);

        // Document page.
        assert_eq!(cake_canvas_set_layout(handle, CAKE_CANVAS_LAYOUT_CONTINUOUS_VERTICAL), CakeCanvasStatus::Ok);
        let mut layout = 99u32;
        assert_eq!(cake_canvas_get_layout(handle, &mut layout), CakeCanvasStatus::Ok);
        assert_eq!(layout, CAKE_CANVAS_LAYOUT_CONTINUOUS_VERTICAL);
        assert_eq!(cake_canvas_set_background_pattern(handle, CAKE_CANVAS_PATTERN_GRID), CakeCanvasStatus::Ok);
        let mut pattern = 99u32;
        assert_eq!(cake_canvas_get_background_pattern(handle, &mut pattern), CakeCanvasStatus::Ok);
        assert_eq!(pattern, CAKE_CANVAS_PATTERN_GRID);
        assert_eq!(cake_canvas_set_background_color(handle, CakeCanvasRgba { r: 1.0, g: 1.0, b: 1.0, a: 1.0 }), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_format_size(handle, 1123.0, 1587.0), CakeCanvasStatus::Ok);
        let mut format = CakeCanvasFormatSize::default();
        assert_eq!(cake_canvas_get_format_size(handle, &mut format), CakeCanvasStatus::Ok);
        assert!((format.width - 1123.0).abs() < 1e-6);
        assert_eq!(cake_canvas_set_format_dpi(handle, 192.0), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_snap_positions(handle, 1), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_set_export_prefs(handle, 1, 1, 0), CakeCanvasStatus::Ok);

        // A stroke is needed before doc export produces content.
        assert_eq!(cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_PEN), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_begin_stroke(handle, sample(120.0, 120.0, 0.5)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(240.0, 200.0, 0.5)), CakeCanvasStatus::Ok);
        let mut svg = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_export_doc(handle, CAKE_CANVAS_EXPORT_SVG, &mut svg), CakeCanvasStatus::Ok);
        assert!(svg.len > 200);
        cake_canvas_buffer_release(&mut svg);
        let mut pdf = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_export_doc(handle, CAKE_CANVAS_EXPORT_PDF, &mut pdf), CakeCanvasStatus::Ok);
        assert!(pdf.len > 200);
        cake_canvas_buffer_release(&mut pdf);
        // No selection yet -> honest NoChange, not an error.
        let mut selection = CakeCanvasBuffer::default();
        assert_eq!(cake_canvas_export_selection_svg(handle, &mut selection), CakeCanvasStatus::NoChange);
        assert_eq!(cake_canvas_export_doc(handle, 999, &mut selection), CakeCanvasStatus::InvalidArgument);

        cake_canvas_engine_free(handle);
    }

    #[test]
    fn native_bridge_rejects_bad_handles_arguments_and_mid_stroke_changes() {
        let valid = sample(10.0, 10.0, 0.5);
        assert_eq!(
            cake_canvas_begin_stroke(ptr::null_mut(), valid),
            CakeCanvasStatus::InvalidHandle
        );

        let handle = cake_canvas_engine_new();
        assert_eq!(
            cake_canvas_set_stroke_tool(handle, 999),
            CakeCanvasStatus::InvalidArgument
        );
        assert_eq!(cake_canvas_begin_stroke(handle, valid), CakeCanvasStatus::Ok);
        assert_eq!(
            cake_canvas_set_stroke_tool(handle, CAKE_CANVAS_TOOL_ERASER),
            CakeCanvasStatus::InvalidState
        );
        assert_eq!(cake_canvas_set_shape(handle, CAKE_CANVAS_SHAPE_ARROW), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_zoom_to(handle, -1.0), CakeCanvasStatus::InvalidArgument);
        assert_eq!(cake_canvas_undo(handle), CakeCanvasStatus::InvalidState);
        assert_eq!(cake_canvas_end_stroke(handle, valid), CakeCanvasStatus::Ok);

        let invalid = CakeCanvasPointerSample {
            x: f64::NAN,
            ..valid
        };
        assert_eq!(
            cake_canvas_begin_stroke(handle, invalid),
            CakeCanvasStatus::InvalidArgument
        );
        cake_canvas_engine_free(handle);
    }
}
