#ifndef CAKEOS_CANVAS_H
#define CAKEOS_CANVAS_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CAKE_CANVAS_ABI_VERSION 3
#define CAKE_CANVAS_ABI_VERSION_MIN 2
#define CAKE_CANVAS_TOOL_PEN 0
#define CAKE_CANVAS_TOOL_HIGHLIGHTER 1
#define CAKE_CANVAS_TOOL_ERASER 2
#define CAKE_CANVAS_TOOL_SELECTOR 3
#define CAKE_CANVAS_TOOL_SHAPE 4
#define CAKE_CANVAS_TOOL_TYPEWRITER 5
#define CAKE_CANVAS_TOOL_TOOLS 6
#define CAKE_CANVAS_SHAPE_RECTANGLE 0
#define CAKE_CANVAS_SHAPE_ELLIPSE 1
#define CAKE_CANVAS_SHAPE_LINE 2
#define CAKE_CANVAS_SHAPE_ARROW 3
#define CAKE_CANVAS_SHAPE_GRID 4
#define CAKE_CANVAS_SHAPE_COORD_SYSTEM_2D 5
#define CAKE_CANVAS_SHAPE_COORD_SYSTEM_3D 6
#define CAKE_CANVAS_SHAPE_QUADRANT_COORD_SYSTEM_2D 7
#define CAKE_CANVAS_SHAPE_FOCI_ELLIPSE 8
#define CAKE_CANVAS_SHAPE_QUADBEZ 9
#define CAKE_CANVAS_SHAPE_CUBBEZ 10
#define CAKE_CANVAS_SHAPE_POLYLINE 11
#define CAKE_CANVAS_SHAPE_POLYGON 12
#define CAKE_CANVAS_BRUSH_MARKER 0
#define CAKE_CANVAS_BRUSH_SOLID 1
#define CAKE_CANVAS_BRUSH_TEXTURED 2
#define CAKE_CANVAS_BRUSH_BUILDER_SIMPLE 0
#define CAKE_CANVAS_BRUSH_BUILDER_CURVED 1
#define CAKE_CANVAS_BRUSH_BUILDER_MODELED 2
#define CAKE_CANVAS_SHAPER_SMOOTH 0
#define CAKE_CANVAS_SHAPER_ROUGH 1
#define CAKE_CANVAS_ERASER_TRASH 0
#define CAKE_CANVAS_ERASER_SPLIT 1
#define CAKE_CANVAS_SELECTOR_POLYGON 0
#define CAKE_CANVAS_SELECTOR_RECTANGLE 1
#define CAKE_CANVAS_SELECTOR_SINGLE 2
#define CAKE_CANVAS_SELECTOR_INTERSECTING_PATH 3
#define CAKE_CANVAS_TOOLS_VERTICAL_SPACE 0
#define CAKE_CANVAS_TOOLS_OFFSET_CAMERA 1
#define CAKE_CANVAS_TOOLS_ZOOM 2
#define CAKE_CANVAS_TOOLS_LASER 3
#define CAKE_CANVAS_LAYOUT_FIXED_SIZE 0
#define CAKE_CANVAS_LAYOUT_CONTINUOUS_VERTICAL 1
#define CAKE_CANVAS_LAYOUT_SEMI_INFINITE 2
#define CAKE_CANVAS_LAYOUT_INFINITE 3
#define CAKE_CANVAS_PATTERN_NONE 0
#define CAKE_CANVAS_PATTERN_LINES 1
#define CAKE_CANVAS_PATTERN_GRID 2
#define CAKE_CANVAS_PATTERN_DOTS 3
#define CAKE_CANVAS_PATTERN_ISOMETRIC_GRID 4
#define CAKE_CANVAS_PATTERN_ISOMETRIC_DOTS 5
#define CAKE_CANVAS_EXPORT_SVG 0
#define CAKE_CANVAS_EXPORT_PDF 1
#define CAKE_CANVAS_EXPORT_XOPP 2
#define CAKE_CANVAS_RENDER_FORMAT_SVG 1
#define CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT 1

typedef int32_t CakeCanvasStatus;

#define CAKE_CANVAS_STATUS_OK 0
#define CAKE_CANVAS_STATUS_NO_CHANGE 1
#define CAKE_CANVAS_STATUS_INVALID_HANDLE -1
#define CAKE_CANVAS_STATUS_INVALID_ARGUMENT -2
#define CAKE_CANVAS_STATUS_INVALID_STATE -3
#define CAKE_CANVAS_STATUS_ENGINE_ERROR -4
#define CAKE_CANVAS_STATUS_PANIC -5

typedef struct {
    double x;
    double y;
    double pressure;
    double tilt_x;
    double tilt_y;
} CakeCanvasPointerSample;

typedef struct {
    double r;
    double g;
    double b;
    double a;
} CakeCanvasRgba;

typedef struct {
    double width;
    double height;
    double dpi;
} CakeCanvasFormatSize;

typedef struct {
    uint8_t* data;
    size_t len;
} CakeCanvasBuffer;

typedef struct {
    uint32_t format;
    uint32_t coordinate_space;
    double x;
    double y;
    double width;
    double height;
    uint8_t* data;
    size_t len;
} CakeCanvasRenderFrame;

typedef struct {
    double center_x;
    double center_y;
    double zoom;
} CakeCanvasViewport;

uint32_t cake_canvas_abi_version(void);
uint32_t cake_canvas_abi_version_min(void);
void* cake_canvas_engine_new(void);
void cake_canvas_engine_free(void* handle);

CakeCanvasStatus cake_canvas_engine_from_rnote(
    const uint8_t* data,
    size_t len,
    void** out_handle
);

CakeCanvasStatus cake_canvas_set_stroke_tool(void* handle, uint32_t tool);
CakeCanvasStatus cake_canvas_set_shape(void* handle, uint32_t shape);
/* v3 donor-parity surface (requires a native library built at ABI >= 3). */
CakeCanvasStatus cake_canvas_set_brush_style(void* handle, uint32_t style);
CakeCanvasStatus cake_canvas_get_brush_style(const void* handle, uint32_t* out_style);
CakeCanvasStatus cake_canvas_set_brush_builder(void* handle, uint32_t builder);
CakeCanvasStatus cake_canvas_get_brush_builder(const void* handle, uint32_t* out_builder);
CakeCanvasStatus cake_canvas_set_stroke_width(void* handle, double width);
CakeCanvasStatus cake_canvas_get_stroke_width(const void* handle, double* out_width);
CakeCanvasStatus cake_canvas_set_stroke_color(void* handle, CakeCanvasRgba color);
CakeCanvasStatus cake_canvas_get_stroke_color(const void* handle, CakeCanvasRgba* out_color);
CakeCanvasStatus cake_canvas_set_fill_color(void* handle, CakeCanvasRgba color);
CakeCanvasStatus cake_canvas_get_fill_color(const void* handle, CakeCanvasRgba* out_color);
CakeCanvasStatus cake_canvas_set_eraser_width(void* handle, double width);
CakeCanvasStatus cake_canvas_get_eraser_width(const void* handle, double* out_width);
CakeCanvasStatus cake_canvas_set_eraser_style(void* handle, uint32_t style);
CakeCanvasStatus cake_canvas_get_eraser_style(const void* handle, uint32_t* out_style);
CakeCanvasStatus cake_canvas_set_shaper_style(void* handle, uint32_t style);
CakeCanvasStatus cake_canvas_get_shaper_style(const void* handle, uint32_t* out_style);
CakeCanvasStatus cake_canvas_set_shaper_constraints_enabled(void* handle, uint8_t enabled);
CakeCanvasStatus cake_canvas_get_shaper_constraints_enabled(const void* handle, uint8_t* out_enabled);
CakeCanvasStatus cake_canvas_set_selector_style(void* handle, uint32_t style);
CakeCanvasStatus cake_canvas_get_selector_style(const void* handle, uint32_t* out_style);
CakeCanvasStatus cake_canvas_set_selector_lock_aspect(void* handle, uint8_t lock_aspect);
CakeCanvasStatus cake_canvas_get_selector_lock_aspect(const void* handle, uint8_t* out_lock_aspect);
CakeCanvasStatus cake_canvas_set_tools_style(void* handle, uint32_t style);
CakeCanvasStatus cake_canvas_get_tools_style(const void* handle, uint32_t* out_style);
CakeCanvasStatus cake_canvas_set_typewriter_font_size(void* handle, double size);
CakeCanvasStatus cake_canvas_get_typewriter_font_size(const void* handle, double* out_size);
CakeCanvasStatus cake_canvas_set_typewriter_text_width(void* handle, double width);
CakeCanvasStatus cake_canvas_get_typewriter_text_width(const void* handle, double* out_width);
CakeCanvasStatus cake_canvas_set_layout(void* handle, uint32_t layout);
CakeCanvasStatus cake_canvas_get_layout(const void* handle, uint32_t* out_layout);
CakeCanvasStatus cake_canvas_set_background_pattern(void* handle, uint32_t pattern);
CakeCanvasStatus cake_canvas_get_background_pattern(const void* handle, uint32_t* out_pattern);
CakeCanvasStatus cake_canvas_set_background_color(void* handle, CakeCanvasRgba color);
CakeCanvasStatus cake_canvas_get_background_color(const void* handle, CakeCanvasRgba* out_color);
CakeCanvasStatus cake_canvas_set_pattern_color(void* handle, CakeCanvasRgba color);
CakeCanvasStatus cake_canvas_set_pattern_size(void* handle, double width, double height);
CakeCanvasStatus cake_canvas_set_format_size(void* handle, double width, double height);
CakeCanvasStatus cake_canvas_get_format_size(const void* handle, CakeCanvasFormatSize* out_size);
CakeCanvasStatus cake_canvas_set_format_dpi(void* handle, double dpi);
CakeCanvasStatus cake_canvas_set_snap_positions(void* handle, uint8_t snap);
CakeCanvasStatus cake_canvas_set_export_prefs(void* handle, uint8_t with_background, uint8_t with_pattern, uint8_t optimize_printing);
CakeCanvasStatus cake_canvas_export_doc(const void* handle, uint32_t format, CakeCanvasBuffer* out_buffer);
CakeCanvasStatus cake_canvas_export_selection_svg(const void* handle, CakeCanvasBuffer* out_buffer);
CakeCanvasStatus cake_canvas_set_viewport_size(void* handle, double width, double height);
CakeCanvasStatus cake_canvas_zoom_to(void* handle, double zoom);
CakeCanvasStatus cake_canvas_pan_by(void* handle, double delta_x, double delta_y);
CakeCanvasStatus cake_canvas_set_viewport_center(void* handle, double center_x, double center_y);
CakeCanvasStatus cake_canvas_get_viewport(const void* handle, CakeCanvasViewport* out_viewport);

CakeCanvasStatus cake_canvas_begin_stroke(void* handle, CakeCanvasPointerSample sample);
CakeCanvasStatus cake_canvas_update_stroke(void* handle, CakeCanvasPointerSample sample);
CakeCanvasStatus cake_canvas_end_stroke(void* handle, CakeCanvasPointerSample sample);

uint8_t cake_canvas_can_undo(const void* handle);
uint8_t cake_canvas_can_redo(const void* handle);

CakeCanvasStatus cake_canvas_undo(void* handle);
CakeCanvasStatus cake_canvas_redo(void* handle);

CakeCanvasStatus cake_canvas_render_frame(const void* handle, CakeCanvasRenderFrame* out_frame);
void cake_canvas_render_frame_release(CakeCanvasRenderFrame* frame);

CakeCanvasStatus cake_canvas_save_rnote(const void* handle, CakeCanvasBuffer* out_buffer);
void cake_canvas_buffer_release(CakeCanvasBuffer* buffer);

#ifdef __cplusplus
}
#endif

#endif /* CAKEOS_CANVAS_H */
