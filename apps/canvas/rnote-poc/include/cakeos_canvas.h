#ifndef CAKEOS_CANVAS_H
#define CAKEOS_CANVAS_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CAKE_CANVAS_ABI_VERSION 1
#define CAKE_CANVAS_TOOL_PEN 0
#define CAKE_CANVAS_TOOL_ERASER 1
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

uint32_t cake_canvas_abi_version(void);
void* cake_canvas_engine_new(void);
void cake_canvas_engine_free(void* handle);

CakeCanvasStatus cake_canvas_engine_from_rnote(
    const uint8_t* data,
    size_t len,
    void** out_handle
);

CakeCanvasStatus cake_canvas_set_stroke_tool(void* handle, uint32_t tool);

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