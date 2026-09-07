use std::ffi::c_void;
use std::panic::{catch_unwind, AssertUnwindSafe};
use std::ptr;
use std::slice;

use futures::executor::block_on;

use crate::{
    CanvasCoordinateSpace, CanvasPointerSample, CanvasRenderFormat, CanvasStrokeTool,
    HeadlessCanvasEngine,
};

pub const CAKE_CANVAS_ABI_VERSION: u32 = 1;
pub const CAKE_CANVAS_TOOL_PEN: u32 = 0;
pub const CAKE_CANVAS_TOOL_ERASER: u32 = 1;
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

fn tool_from_abi(tool: u32) -> Option<CanvasStrokeTool> {
    match tool {
        CAKE_CANVAS_TOOL_PEN => Some(CanvasStrokeTool::Pen),
        CAKE_CANVAS_TOOL_ERASER => Some(CanvasStrokeTool::Eraser),
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
        let Some(result) = with_engine_mut(handle, |engine| engine.set_stroke_tool(tool)) else {
            return CakeCanvasStatus::InvalidHandle;
        };

        match result {
            Ok(()) => CakeCanvasStatus::Ok,
            Err(_) => CakeCanvasStatus::InvalidState,
        }
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
        assert_eq!(cake_canvas_abi_version(), 1);

        assert_eq!(cake_canvas_begin_stroke(handle, sample(120.0, 120.0, 0.2)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_update_stroke(handle, sample(180.0, 155.0, 0.6)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_end_stroke(handle, sample(240.0, 200.0, 0.8)), CakeCanvasStatus::Ok);
        assert_eq!(cake_canvas_can_undo(handle), 1);

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
