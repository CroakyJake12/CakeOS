#include <cakeos/present/engine.hpp>
#include <LibreOfficeKit/LibreOfficeKit.hxx>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <vector>
#include <string>
#include <cstring>
#include <memory>

extern "C" {

using namespace cakeos::present;

struct COpaqueWriteEngine;
using WriteEngineHandle = COpaqueWriteEngine*;

struct CDocumentInfo {
    char* path;
    char* title;
    int pageCount;
    long size;
};

struct CPageInfo {
    int pageNumber;
    int widthTwips;
    int heightTwips;
};

struct CCursorPosition {
    int page;
    int paragraph;
    int offset;
};

struct CFormat {
    int bold;
    int italic;
    int underline;
    char* fontFamily;
    int fontSize;
    char* color;
};

struct CSelection {
    CCursorPosition start;
    CCursorPosition end;
};

using CWriteEventCallback = void(*)(int type, char* payload);

static std::string toString(const char* s) { return s ? s : ""; }

static char* strdup_c(const std::string& s) {
    char* p = static_cast<char*>(std::malloc(s.size() + 1));
    if (p) std::memcpy(p, s.c_str(), s.size() + 1);
    return p;
}

static void free_c(void* p) { std::free(p); }

class WriteEngineWrapper {
public:
    explicit WriteEngineWrapper(const char* libreOfficeProgramPath, const char* userProfileUrl) {
        EngineOptions opts;
        opts.libreOfficeProgramPath = toString(libreOfficeProgramPath);
        opts.userProfileUrl = toString(userProfileUrl);
        engine_ = std::make_unique<PresentEngine>(opts);
        // Initialize LibreOfficeKit for Writer
        lok::Office* office = lok::Office::create(opts.libreOfficeProgramPath.c_str());
        if (office) {
            office_ = office;
            office_->setDocumentLoadedCallback([](int type, const char* payload, void* data) {
                auto self = static_cast<WriteEngineWrapper*>(data);
                if (self->callback_) {
                    char* p = strdup_c(payload ? payload : "");
                    self->callback_(type, p);
                }
            }, this);
        }
    }

    ~WriteEngineWrapper() {
        if (office_) {
            lok::Office::destroy(office_);
        }
    }

    void open(const char* pathOrUrl) {
        if (office_) office_->loadDocument(pathOrUrl);
    }

    void close() {
        if (office_) office_->closeDocument();
    }

    int isOpen() const {
        return office_ && office_->getDocumentType() != LOK_DOCTYPE_INVALID ? 1 : 0;
    }

    void getDocumentInfo(CDocumentInfo* info) {
        if (!office_) return;
        info->path = strdup_c(office_->getDocumentPath());
        info->title = strdup_c(office_->getTitle());
        info->pageCount = office_->getParts();
        info->size = 0; // Not directly available
    }

    int getPageCount() {
        if (!office_) return 0;
        return office_->getParts();
    }

    void getPageInfo(int index, CPageInfo* info) {
        if (!office_) return;
        long width, height;
        office_->getPartPageRectangles(index, &width, &height);
        info->pageNumber = index;
        info->widthTwips = static_cast<int>(width);
        info->heightTwips = static_cast<int>(height);
    }

    char* getText(const CCursorPosition* start, const CCursorPosition* end) {
        if (!office_) return strdup_c("");
        // Simplified - would need proper text extraction from LOK
        return strdup_c("");
    }

    void setText(const CCursorPosition* position, const char* text) {
        if (!office_ || !text) return;
        // Simplified - would need proper text insertion
    }

    void insertText(const CCursorPosition* position, const char* text) {
        if (!office_ || !text) return;
        // Simplified
    }

    void deleteText(const CCursorPosition* start, const CCursorPosition* end) {
        if (!office_) return;
        // Simplified
    }

    void applyFormat(const CCursorPosition* start, const CCursorPosition* end, const CFormat* format) {
        if (!office_ || !format) return;
        // Simplified - would use LOK formatting
    }

    void save() {
        if (office_) office_->save();
    }

    void saveAs(const char* destinationPathOrUrl, const char* format) {
        if (office_) office_->saveAs(destinationPathOrUrl, format);
    }

    void print() {
        if (office_) office_->print();
    }

    void undo() {
        if (office_) office_->postUnoCommand(".uno:Undo", "{}", false);
    }

    void redo() {
        if (office_) office_->postUnoCommand(".uno:Redo", "{}", false);
    }

    void setCursor(const CCursorPosition* position) {
        if (!office_) return;
        // Simplified
    }

    void getCursor(CCursorPosition* position) {
        if (!office_) return;
        position->page = 0;
        position->paragraph = 0;
        position->offset = 0;
    }

    void setSelection(const CCursorPosition* start, const CCursorPosition* end) {
        if (!office_) return;
        // Simplified
    }

    int getSelection(CCursorPosition* start, CCursorPosition* end) {
        if (!office_) return 0;
        start->page = 0; start->paragraph = 0; start->offset = 0;
        end->page = 0; end->paragraph = 0; end->offset = 0;
        return 0;
    }

    void setEventCallback(CWriteEventCallback callback) {
        callback_ = callback;
    }

private:
    std::unique_ptr<PresentEngine> engine_;
    lok::Office* office_ = nullptr;
    CWriteEventCallback callback_ = nullptr;
};

WriteEngineHandle write_engine_create(const char* libreOfficeProgramPath, const char* userProfileUrl) {
    if (!libreOfficeProgramPath) return nullptr;
    auto wrapper = std::make_unique<WriteEngineWrapper>(libreOfficeProgramPath, userProfileUrl);
    return reinterpret_cast<WriteEngineHandle>(wrapper.release());
}

void write_engine_destroy(WriteEngineHandle handle) {
    if (handle) delete reinterpret_cast<WriteEngineWrapper*>(handle);
}

void write_engine_open(WriteEngineHandle handle, const char* pathOrUrl) {
    if (handle && pathOrUrl) reinterpret_cast<WriteEngineWrapper*>(handle)->open(pathOrUrl);
}

void write_engine_close(WriteEngineHandle handle) {
    if (handle) reinterpret_cast<WriteEngineWrapper*>(handle)->close();
}

int write_engine_is_open(WriteEngineHandle handle) {
    return handle ? reinterpret_cast<WriteEngineWrapper*>(handle)->isOpen() : 0;
}

void write_engine_get_document_info(WriteEngineHandle handle, CDocumentInfo* info) {
    if (handle && info) reinterpret_cast<WriteEngineWrapper*>(handle)->getDocumentInfo(info);
}

int write_engine_get_page_count(WriteEngineHandle handle) {
    return handle ? reinterpret_cast<WriteEngineWrapper*>(handle)->getPageCount() : 0;
}

void write_engine_get_page_info(WriteEngineHandle handle, int index, CPageInfo* info) {
    if (handle && info) reinterpret_cast<WriteEngineWrapper*>(handle)->getPageInfo(index, info);
}

char* write_engine_get_text(WriteEngineHandle handle, const CCursorPosition* start, const CCursorPosition* end) {
    if (handle && start && end) return reinterpret_cast<WriteEngineWrapper*>(handle)->getText(start, end);
    return strdup_c("");
}

void write_engine_set_text(WriteEngineHandle handle, const CCursorPosition* position, const char* text) {
    if (handle && position && text) reinterpret_cast<WriteEngineWrapper*>(handle)->setText(position, text);
}

void write_engine_insert_text(WriteEngineHandle handle, const CCursorPosition* position, const char* text) {
    if (handle && position && text) reinterpret_cast<WriteEngineWrapper*>(handle)->insertText(position, text);
}

void write_engine_delete_text(WriteEngineHandle handle, const CCursorPosition* start, const CCursorPosition* end) {
    if (handle && start && end) reinterpret_cast<WriteEngineWrapper*>(handle)->deleteText(start, end);
}

void write_engine_apply_format(WriteEngineHandle handle, const CCursorPosition* start, const CCursorPosition* end, const CFormat* format) {
    if (handle && start && end && format) reinterpret_cast<WriteEngineWrapper*>(handle)->applyFormat(start, end, format);
}

void write_engine_save(WriteEngineHandle handle) {
    if (handle) reinterpret_cast<WriteEngineWrapper*>(handle)->save();
}

void write_engine_save_as(WriteEngineHandle handle, const char* destinationPathOrUrl, const char* format) {
    if (handle && destinationPathOrUrl) reinterpret_cast<WriteEngineWrapper*>(handle)->saveAs(destinationPathOrUrl, format);
}

void write_engine_print(WriteEngineHandle handle) {
    if (handle) reinterpret_cast<WriteEngineWrapper*>(handle)->print();
}

void write_engine_undo(WriteEngineHandle handle) {
    if (handle) reinterpret_cast<WriteEngineWrapper*>(handle)->undo();
}

void write_engine_redo(WriteEngineHandle handle) {
    if (handle) reinterpret_cast<WriteEngineWrapper*>(handle)->redo();
}

void write_engine_set_cursor(WriteEngineHandle handle, const CCursorPosition* position) {
    if (handle && position) reinterpret_cast<WriteEngineWrapper*>(handle)->setCursor(position);
}

void write_engine_get_cursor(WriteEngineHandle handle, CCursorPosition* position) {
    if (handle && position) reinterpret_cast<WriteEngineWrapper*>(handle)->getCursor(position);
}

void write_engine_set_selection(WriteEngineHandle handle, const CCursorPosition* start, const CCursorPosition* end) {
    if (handle && start && end) reinterpret_cast<WriteEngineWrapper*>(handle)->setSelection(start, end);
}

int write_engine_get_selection(WriteEngineHandle handle, CCursorPosition* start, CCursorPosition* end) {
    if (handle && start && end) return reinterpret_cast<WriteEngineWrapper*>(handle)->getSelection(start, end);
    return 0;
}

void write_engine_set_event_callback(WriteEngineHandle handle, CWriteEventCallback callback) {
    if (handle) reinterpret_cast<WriteEngineWrapper*>(handle)->setEventCallback(callback);
}

void write_engine_free_string(char* ptr) {
    if (ptr) free_c(ptr);
}

}