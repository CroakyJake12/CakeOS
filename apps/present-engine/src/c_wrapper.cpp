#include <cakeos/present/engine.hpp>
#include <vector>
#include <string>
#include <cstring>
#include <memory>

extern "C" {

using namespace cakeos::present;

struct COpaqueEngine;
using EngineHandle = COpaqueEngine*;

struct CSlideInfo {
    int index;
    char* name;
    char* hash;
};

struct CSlideExtent {
    long widthTwips;
    long heightTwips;
};

struct CElementSnapshot {
    int slideIndex;
    int objectIndex;
    char** textArray;
    int textCount;
};

struct CTileRequest {
    int slideIndex;
    int pixelWidth;
    int pixelHeight;
    int tileXTwips;
    int tileYTwips;
    int tileWidthTwips;
    int tileHeightTwips;
};

struct CRenderedTile {
    int pixelWidth;
    int pixelHeight;
    int pixelFormat;
    unsigned char* pixels;
    int pixelsLength;
};

struct CEngineEvent {
    int upstreamType;
    char* payload;
};

using CEventCallback = void(*)(CEngineEvent*);

struct COptions {
    const char* libreOfficeProgramPath;
    const char* userProfileUrl;
};

static std::string toString(const char* s) { return s ? s : ""; }

static char* strdup_c(const std::string& s) {
    char* p = static_cast<char*>(std::malloc(s.size() + 1));
    if (p) std::memcpy(p, s.c_str(), s.size() + 1);
    return p;
}

static void free_c(void* p) { std::free(p); }

EngineHandle present_engine_create(const COptions* options) {
    if (!options) return nullptr;
    EngineOptions opts;
    opts.libreOfficeProgramPath = toString(options->libreOfficeProgramPath);
    opts.userProfileUrl = toString(options->userProfileUrl);
    auto engine = std::make_unique<PresentEngine>(opts);
    return reinterpret_cast<EngineHandle>(engine.release());
}

void present_engine_destroy(EngineHandle handle) {
    if (handle) delete reinterpret_cast<PresentEngine*>(handle);
}

void present_engine_open(EngineHandle handle, const char* pathOrUrl) {
    if (handle && pathOrUrl)
        reinterpret_cast<PresentEngine*>(handle)->open(pathOrUrl);
}

void present_engine_close(EngineHandle handle) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->close();
}

int present_engine_is_open(EngineHandle handle) {
    return handle ? reinterpret_cast<PresentEngine*>(handle)->isOpen() : 0;
}

int present_engine_slides_count(EngineHandle handle) {
    if (!handle) return 0;
    return static_cast<int>(reinterpret_cast<PresentEngine*>(handle)->slides().size());
}

void present_engine_get_slide(EngineHandle handle, int index, CSlideInfo* out) {
    if (!handle || !out) return;
    auto& engine = *reinterpret_cast<PresentEngine*>(handle);
    auto slides = engine.slides();
    if (index < 0 || index >= static_cast<int>(slides.size())) return;
    auto& s = slides[index];
    out->index = s.index;
    out->name = strdup_c(s.name);
    out->hash = strdup_c(s.hash);
}

int present_engine_current_slide(EngineHandle handle) {
    if (!handle) return -1;
    return reinterpret_cast<PresentEngine*>(handle)->currentSlide();
}

void present_engine_set_current_slide(EngineHandle handle, int slideIndex) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->setCurrentSlide(slideIndex);
}

void present_engine_slide_extent(EngineHandle handle, int slideIndex, CSlideExtent* out) {
    if (!handle || !out) return;
    auto extent = reinterpret_cast<PresentEngine*>(handle)->slideExtent(slideIndex);
    out->widthTwips = extent.widthTwips;
    out->heightTwips = extent.heightTwips;
}

int present_engine_supports_element_snapshots(EngineHandle handle) {
    if (!handle) return 0;
    return reinterpret_cast<PresentEngine*>(handle)->supportsElementSnapshots() ? 1 : 0;
}

int present_engine_element_snapshot_count(EngineHandle handle, const char* documentPathOrUrl, int slideIndex) {
    if (!handle || !documentPathOrUrl) return 0;
    try {
        auto& engine = *reinterpret_cast<PresentEngine*>(handle);
        auto snapshots = engine.elementSnapshot(documentPathOrUrl, slideIndex);
        return static_cast<int>(snapshots.size());
    } catch (...) {
        return 0;
    }
}

void present_engine_get_element_snapshot(EngineHandle handle, const char* documentPathOrUrl, int slideIndex, int index, CElementSnapshot* out) {
    if (!handle || !documentPathOrUrl || !out) return;
    try {
        auto& engine = *reinterpret_cast<PresentEngine*>(handle);
        auto snapshots = engine.elementSnapshot(documentPathOrUrl, slideIndex);
        if (index < 0 || index >= static_cast<int>(snapshots.size())) return;
        auto& s = snapshots[index];
        out->slideIndex = s.slideIndex;
        out->objectIndex = s.objectIndex;
        out->textCount = static_cast<int>(s.text.size());
        out->textArray = static_cast<char**>(std::malloc(s.text.size() * sizeof(char*)));
        for (size_t i = 0; i < s.text.size(); ++i) {
            out->textArray[i] = strdup_c(s.text[i]);
        }
    } catch (...) {
        out->textCount = 0;
        out->textArray = nullptr;
    }
}

void present_engine_select_element(EngineHandle handle, const char* snapshotPathOrUrl, int slideIndex, int objectIndex) {
    if (!handle || !snapshotPathOrUrl) return;
    reinterpret_cast<PresentEngine*>(handle)->selectElement(snapshotPathOrUrl, slideIndex, objectIndex);
}

void present_engine_clear_element_selection(EngineHandle handle, int slideIndex, int objectIndex) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->clearElementSelection(slideIndex, objectIndex);
}

int present_engine_replace_element_text(EngineHandle handle, const char* snapshotPathOrUrl, int slideIndex, int objectIndex, const char* text) {
    if (!handle || !snapshotPathOrUrl || !text) return 0;
    try {
        return reinterpret_cast<PresentEngine*>(handle)->replaceElementText(snapshotPathOrUrl, slideIndex, objectIndex, text) ? 1 : 0;
    } catch (...) {
        return 0;
    }
}

void present_engine_add_slide_after(EngineHandle handle, int slideIndex) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->addSlideAfter(slideIndex);
}

void present_engine_duplicate_slide(EngineHandle handle, int slideIndex) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->duplicateSlide(slideIndex);
}

void present_engine_delete_slide(EngineHandle handle, int slideIndex) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->deleteSlide(slideIndex);
}

void present_engine_move_slide(EngineHandle handle, int fromIndex, int toIndex) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->moveSlide(fromIndex, toIndex);
}

void present_engine_undo(EngineHandle handle) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->undo();
}

void present_engine_redo(EngineHandle handle) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->redo();
}

void present_engine_render_tile(EngineHandle handle, CTileRequest* request, CRenderedTile* out) {
    if (!handle || !request || !out) return;
    try {
        TileRequest req;
        req.slideIndex = request->slideIndex;
        req.pixelWidth = request->pixelWidth;
        req.pixelHeight = request->pixelHeight;
        req.tileXTwips = request->tileXTwips;
        req.tileYTwips = request->tileYTwips;
        req.tileWidthTwips = request->tileWidthTwips;
        req.tileHeightTwips = request->tileHeightTwips;
        auto tile = reinterpret_cast<PresentEngine*>(handle)->renderTile(req);
        out->pixelWidth = tile.pixelWidth;
        out->pixelHeight = tile.pixelHeight;
        out->pixelFormat = static_cast<int>(tile.pixelFormat);
        out->pixelsLength = static_cast<int>(tile.pixels.size());
        out->pixels = static_cast<unsigned char*>(std::malloc(tile.pixels.size()));
        if (out->pixels)
            std::memcpy(out->pixels, tile.pixels.data(), tile.pixels.size());
    } catch (...) {
        out->pixelWidth = 0;
        out->pixelHeight = 0;
        out->pixels = nullptr;
        out->pixelsLength = 0;
    }
}

void present_engine_post_key_event(EngineHandle handle, int type, int charCode, int keyCode) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->postKeyEvent(
            static_cast<KeyEventType>(type), charCode, keyCode);
}

void present_engine_post_mouse_event(EngineHandle handle, int type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers) {
    if (handle)
        reinterpret_cast<PresentEngine*>(handle)->postMouseEvent(
            static_cast<MouseEventType>(type), xTwips, yTwips, clickCount, buttons, modifiers);
}

void present_engine_post_uno_command(EngineHandle handle, const char* command, const char* jsonArguments, int notifyWhenFinished) {
    if (!handle || !command) return;
    reinterpret_cast<PresentEngine*>(handle)->postUnoCommand(command, jsonArguments ? jsonArguments : "{}", notifyWhenFinished != 0);
}

void present_engine_save_as(EngineHandle handle, const char* destinationPathOrUrl, const char* format, const char* filterOptions) {
    if (!handle || !destinationPathOrUrl) return;
    reinterpret_cast<PresentEngine*>(handle)->saveAs(destinationPathOrUrl, format ? format : "", filterOptions ? filterOptions : "");
}

void present_engine_set_event_callback(EngineHandle handle, CEventCallback callback) {
    if (!handle) return;
    auto engine = reinterpret_cast<PresentEngine*>(handle);
    if (callback) {
        engine->setEventCallback([callback](const EngineEvent& evt) {
            CEngineEvent cevt;
            cevt.upstreamType = evt.upstreamType;
            cevt.payload = strdup_c(evt.payload);
            callback(&cevt);
            free_c(cevt.payload);
        });
    } else {
        engine->setEventCallback(nullptr);
    }
}

const char* present_engine_path_to_file_url(const char* pathOrUrl) {
    if (!pathOrUrl) return nullptr;
    auto result = PresentEngine::pathToFileUrl(pathOrUrl);
    return strdup_c(result);
}

}