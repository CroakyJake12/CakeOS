#ifndef CAKEOS_CANVAS_H
#define CAKEOS_CANVAS_H

#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CAKE_CANVAS_ABI_VERSION 1u
#define CAKE_CANVAS_TOOL_PEN 0u
#define CAKE_CANVAS_TOOL_ERASER 1u
#define CAKE_CANVAS_RENDER_FORMAT_SVG 1u
#define CAKE_CANVAS_COORDINATE_SPACE_DOCUMENT 1u

typedef void CakeCanvasEngine;

typedef enum CakeCanvasStatus {
    CAKE_CANVAS_STATUS_OK = 0,
    CAKE_CANVAS_STATUS_NO_CHANGE = 1,
    CAKE_CANVAS_STATUS_INVALID_HANDLE = -1,
    CAKE_CANVAS_STATUS_INVALID_ARGUMENT = -2,
    CAKE_CANVAS_STATUS_INVALID_STATE = -3,
    CAKE_CANVAS_STATUS_ENGINE_ERROR = -4,
    CAKE_CANVAS_STATUS_PANIC = -5
} CakeCanvasStatus;

typedef struct CakeCanvasPointerSample {
    double x;
    double y;
    double pressure;
    double tilt_x;
    double tilt_y;
} CakeCanvasPointerSample;

typedef struct CakeCanvasBuffer {
    uint8_t *data;
    size_t len;
} CakeCanvasBuffer;

typedef struct CakeCanvasRenderFrame {
    uint32_t format;
    uint32_t coordinate_space;
    double x;
    double y;
    double width;
    double height;
    uint8_t *data;
    size_t len;
} CakeCanvasRenderFrame;

/* Returns the ABI version implemented by the loaded native library. */
uint32_t cake_canvas_abi_version(void);

/* Engine handles are opaque and must be released exactly once. */
CakeCanvasEngine *cake_canvas_engine_new(void);
void cake_canvas_engine_free(CakeCanvasEngine *handle);

/* Restores a fresh engine from native Rnote bytes. The input is copied. */
CakeCanvasStatus cake_canvas_engine_from_rnote(
    const uint8_t *data,
    size_t len,
    CakeCanvasEngine **out_handle);

/* Tool changes and history changes are rejected while a stroke is active. */
CakeCanvasStatus cake_canvas_set_stroke_tool(CakeCanvasEngine *handle, uint32_t tool);
CakeCanvasStatus cake_canvas_begin_stroke(CakeCanvasEngine *handle, CakeCanvasPointerSample sample);
CakeCanvasStatus cake_canvas_update_stroke(CakeCanvasEngine *handle, CakeCanvasPointerSample sample);
CakeCanvasStatus cake_canvas_end_stroke(CakeCanvasEngine *handle, CakeCanvasPointerSample sample);

uint8_t cake_canvas_can_undo(const CakeCanvasEngine *handle);
uint8_t cake_canvas_can_redo(const CakeCanvasEngine *handle);
CakeCanvasStatus cake_canvas_undo(CakeCanvasEngine *handle);
CakeCanvasStatus cake_canvas_redo(CakeCanvasEngine *handle);

/*
 * The first renderer-neutral frame format is whole-document SVG
 * (image/svg+xml) in Canvas document coordinates. The library owns the returned
 * byte allocation until cake_canvas_render_frame_release is called.
 */
CakeCanvasStatus cake_canvas_render_frame(
    const CakeCanvasEngine *handle,
    CakeCanvasRenderFrame *out_frame);
void cake_canvas_render_frame_release(CakeCanvasRenderFrame *frame);

/* Native .rnote payloads are interchange/engine data, not the Cake document model. */
CakeCanvasStatus cake_canvas_save_rnote(
    const CakeCanvasEngine *handle,
    CakeCanvasBuffer *out_buffer);
void cake_canvas_buffer_release(CakeCanvasBuffer *buffer);

#ifdef __cplusplus
}
#endif

#endif /* CAKEOS_CANVAS_H */
