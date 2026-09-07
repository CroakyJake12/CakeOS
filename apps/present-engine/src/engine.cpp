#include "cakeos/present/engine.hpp"

#include <LibreOfficeKit/LibreOfficeKit.hxx>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>

#include <charconv>
#include <chrono>
#include <cmath>
#include <cctype>
#include <filesystem>
#include <limits>
#include <stdexcept>
#include <system_error>
#include <utility>

#if defined(__linux__)
#include <unistd.h>
#endif

namespace cakeos::present {
namespace {

std::string requireNonEmpty(std::string_view value, const char* name)
{
    if (value.empty()) {
        throw std::invalid_argument(std::string(name) + " must not be empty");
    }
    return std::string(value);
}

std::string percentEncodePath(std::string_view value)
{
    static constexpr char Hex[] = "0123456789ABCDEF";
    std::string encoded;
    encoded.reserve(value.size());

    for (const unsigned char ch : value) {
        const bool safe =
            (ch >= 'a' && ch <= 'z') ||
            (ch >= 'A' && ch <= 'Z') ||
            (ch >= '0' && ch <= '9') ||
            ch == '-' || ch == '_' || ch == '.' || ch == '~' || ch == '/';
        if (safe) {
            encoded.push_back(static_cast<char>(ch));
        } else {
            encoded.push_back('%');
            encoded.push_back(Hex[(ch >> 4U) & 0x0FU]);
            encoded.push_back(Hex[ch & 0x0FU]);
        }
    }
    return encoded;
}

std::string makePrivateProfileUrl()
{
    const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
#if defined(__linux__)
    const auto pid = static_cast<long long>(::getpid());
#else
    const auto pid = 0LL;
#endif
    auto path = std::filesystem::temp_directory_path() /
        ("cakeos-present-" + std::to_string(pid) + "-" + std::to_string(stamp));
    std::filesystem::create_directories(path);
    return "file://" + percentEncodePath(path.generic_string());
}

long parsePositiveLongField(std::string_view json, std::string_view field)
{
    const std::string key = "\"" + std::string(field) + "\"";
    const auto keyPosition = json.find(key);
    if (keyPosition == std::string_view::npos) {
        throw std::runtime_error("LibreOfficeKit part metadata is missing " + std::string(field));
    }

    auto valuePosition = json.find(':', keyPosition + key.size());
    if (valuePosition == std::string_view::npos) {
        throw std::runtime_error("LibreOfficeKit returned malformed part metadata");
    }
    ++valuePosition;
    while (valuePosition < json.size() &&
           std::isspace(static_cast<unsigned char>(json[valuePosition])) != 0) {
        ++valuePosition;
    }

    long value = 0;
    const char* begin = json.data() + valuePosition;
    const char* end = json.data() + json.size();
    const auto result = std::from_chars(begin, end, value);
    if (result.ec != std::errc{} || result.ptr == begin || value <= 0) {
        throw std::runtime_error("LibreOfficeKit returned an invalid " + std::string(field));
    }
    return value;
}

long mm100ToTwips(long value)
{
    if (value <= 0) {
        throw std::runtime_error("LibreOfficeKit returned an invalid page dimension");
    }

    // Impress stores page dimensions in hundredths of a millimetre. Core's
    // getPartSize() converts (dimension + 1) to TWIPs; use the same ratio here.
    const long double twips =
        (static_cast<long double>(value) + 1.0L) * 72.0L / 127.0L;
    if (twips > static_cast<long double>(std::numeric_limits<long>::max())) {
        throw std::overflow_error("presentation page dimension is too large");
    }
    return static_cast<long>(std::llround(twips));
}

int toLokKeyType(KeyEventType type)
{
    switch (type) {
    case KeyEventType::Input:
        return LOK_KEYEVENT_KEYINPUT;
    case KeyEventType::Up:
        return LOK_KEYEVENT_KEYUP;
    }
    throw std::invalid_argument("unsupported key event type");
}

int toLokMouseType(MouseEventType type)
{
    switch (type) {
    case MouseEventType::ButtonDown:
        return LOK_MOUSEEVENT_MOUSEBUTTONDOWN;
    case MouseEventType::ButtonUp:
        return LOK_MOUSEEVENT_MOUSEBUTTONUP;
    case MouseEventType::Move:
        return LOK_MOUSEEVENT_MOUSEMOVE;
    }
    throw std::invalid_argument("unsupported mouse event type");
}

PixelFormat toPixelFormat(int tileMode)
{
    switch (tileMode) {
    case LOK_TILEMODE_RGBA:
        return PixelFormat::Rgba;
    case LOK_TILEMODE_BGRA:
        return PixelFormat::Bgra;
    default:
        throw std::runtime_error("LibreOfficeKit returned an unsupported tile format");
    }
}

} // namespace

struct PresentEngine::Impl {
    explicit Impl(EngineOptions value)
        : options(std::move(value))
    {
        if (options.libreOfficeProgramPath.empty()) {
            throw std::invalid_argument("libreOfficeProgramPath must not be empty");
        }
        if (options.userProfileUrl.empty()) {
            options.userProfileUrl = makePrivateProfileUrl();
            ownsProfile = true;
        }

        office.reset(lok::lok_cpp_init(
            options.libreOfficeProgramPath.c_str(),
            options.userProfileUrl.c_str()));
        if (!office) {
            throw std::runtime_error(
                "failed to initialise LibreOfficeKit from " + options.libreOfficeProgramPath);
        }
    }

    ~Impl()
    {
        document.reset();
        office.reset();
        if (ownsProfile) {
            std::error_code ignored;
            constexpr std::string_view prefix{"file://"};
            if (options.userProfileUrl.rfind(prefix, 0) == 0 &&
                options.userProfileUrl.find('%') == std::string::npos) {
                std::filesystem::remove_all(options.userProfileUrl.substr(prefix.size()), ignored);
            }
        }
    }

    static void callbackThunk(int type, const char* payload, void* data)
    {
        auto* self = static_cast<Impl*>(data);
        if (self == nullptr || !self->eventCallback) {
            return;
        }
        self->eventCallback(EngineEvent{type, payload == nullptr ? std::string{} : std::string(payload)});
    }

    [[nodiscard]] std::runtime_error lastError(std::string_view prefix) const
    {
        std::string message(prefix);
        if (!office) {
            return std::runtime_error(message);
        }
        char* raw = office->getError();
        if (raw != nullptr) {
            if (*raw != '\0') {
                message += ": ";
                message += raw;
            }
            office->freeError(raw);
        }
        return std::runtime_error(message);
    }

    void requireDocument() const
    {
        if (!document) {
            throw std::logic_error("no presentation is open");
        }
    }

    void requireSlideIndex(int slideIndex) const
    {
        requireDocument();
        const int count = document->getParts();
        if (slideIndex < 0 || slideIndex >= count) {
            throw std::out_of_range("slide index is outside the presentation");
        }
    }

    std::string takeString(char* raw) const
    {
        if (raw == nullptr) {
            return {};
        }
        std::string value(raw);
        office->freeError(raw);
        return value;
    }

    std::string partInfo(int slideIndex) const
    {
        requireSlideIndex(slideIndex);
        LibreOfficeKitDocument* rawDocument = document->get();
        if (!LIBREOFFICEKIT_DOCUMENT_HAS(rawDocument, getPartInfo) ||
            rawDocument->pClass->getPartInfo == nullptr) {
            throw std::runtime_error("LibreOfficeKit runtime does not expose slide metadata");
        }
        return takeString(rawDocument->pClass->getPartInfo(rawDocument, slideIndex));
    }

    EngineOptions options;
    bool ownsProfile{false};
    std::unique_ptr<lok::Office> office;
    std::unique_ptr<lok::Document> document;
    EventCallback eventCallback;
};

PresentEngine::PresentEngine(EngineOptions options)
    : impl_(std::make_unique<Impl>(std::move(options)))
{
}

PresentEngine::~PresentEngine() = default;
PresentEngine::PresentEngine(PresentEngine&&) noexcept = default;
PresentEngine& PresentEngine::operator=(PresentEngine&&) noexcept = default;

void PresentEngine::open(std::string_view documentPathOrUrl)
{
    const auto url = pathToFileUrl(requireNonEmpty(documentPathOrUrl, "documentPathOrUrl"));
    auto next = std::unique_ptr<lok::Document>(impl_->office->documentLoad(url.c_str()));
    if (!next) {
        throw impl_->lastError("failed to open presentation");
    }
    if (next->getDocumentType() != LOK_DOCTYPE_PRESENTATION) {
        throw std::runtime_error("the selected document is not a presentation");
    }

    next->initializeForRendering();
    next->setPartMode(LOK_PARTMODE_SLIDES);
    next->registerCallback(&Impl::callbackThunk, impl_.get());
    impl_->document = std::move(next);
}

void PresentEngine::close() noexcept
{
    impl_->document.reset();
}

bool PresentEngine::isOpen() const noexcept
{
    return static_cast<bool>(impl_->document);
}

std::vector<SlideInfo> PresentEngine::slides() const
{
    impl_->requireDocument();
    const int count = impl_->document->getParts();
    std::vector<SlideInfo> result;
    result.reserve(static_cast<std::size_t>(count));
    for (int index = 0; index < count; ++index) {
        result.push_back(SlideInfo{
            index,
            impl_->takeString(impl_->document->getPartName(index)),
            impl_->takeString(impl_->document->getPartHash(index))
        });
    }
    return result;
}

int PresentEngine::currentSlide() const
{
    impl_->requireDocument();
    return impl_->document->getPart();
}

void PresentEngine::setCurrentSlide(int slideIndex)
{
    impl_->requireSlideIndex(slideIndex);
    impl_->document->setPart(slideIndex);
}

SlideExtent PresentEngine::slideExtent(int slideIndex) const
{
    const std::string info = impl_->partInfo(slideIndex);
    if (info.empty()) {
        throw std::runtime_error("LibreOfficeKit returned no metadata for the requested slide");
    }

    const long widthMm100 = parsePositiveLongField(info, "width");
    const long heightMm100 = parsePositiveLongField(info, "height");
    return SlideExtent{mm100ToTwips(widthMm100), mm100ToTwips(heightMm100)};
}

void PresentEngine::moveSlide(int fromIndex, int toIndex)
{
    impl_->requireSlideIndex(fromIndex);
    impl_->requireSlideIndex(toIndex);
    if (fromIndex == toIndex) {
        return;
    }

    LibreOfficeKitDocument* rawDocument = impl_->document->get();
    if (!LIBREOFFICEKIT_DOCUMENT_HAS(rawDocument, selectPart) ||
        rawDocument->pClass->selectPart == nullptr ||
        !LIBREOFFICEKIT_DOCUMENT_HAS(rawDocument, moveSelectedParts) ||
        rawDocument->pClass->moveSelectedParts == nullptr) {
        throw std::runtime_error("LibreOfficeKit runtime does not expose slide reordering");
    }

    const int count = impl_->document->getParts();
    impl_->document->setPart(fromIndex);
    for (int index = 0; index < count; ++index) {
        rawDocument->pClass->selectPart(rawDocument, index, 0);
    }
    rawDocument->pClass->selectPart(rawDocument, fromIndex, 1);
    rawDocument->pClass->moveSelectedParts(rawDocument, toIndex, false);
    impl_->document->setPart(toIndex);
}

RenderedTile PresentEngine::renderTile(const TileRequest& request)
{
    impl_->requireSlideIndex(request.slideIndex);
    if (request.pixelWidth <= 0 || request.pixelHeight <= 0 ||
        request.tileWidthTwips <= 0 || request.tileHeightTwips <= 0) {
        throw std::invalid_argument("tile dimensions must be positive");
    }

    const auto width = static_cast<std::size_t>(request.pixelWidth);
    const auto height = static_cast<std::size_t>(request.pixelHeight);
    if (height > std::numeric_limits<std::size_t>::max() / 4U ||
        width > std::numeric_limits<std::size_t>::max() / (height * 4U)) {
        throw std::overflow_error("requested tile buffer is too large");
    }

    setCurrentSlide(request.slideIndex);
    RenderedTile tile;
    tile.pixelWidth = request.pixelWidth;
    tile.pixelHeight = request.pixelHeight;
    tile.pixelFormat = toPixelFormat(impl_->document->getTileMode());
    tile.pixels.resize(width * height * 4U);

    impl_->document->paintTile(
        tile.pixels.data(),
        request.pixelWidth,
        request.pixelHeight,
        request.tileXTwips,
        request.tileYTwips,
        request.tileWidthTwips,
        request.tileHeightTwips);
    return tile;
}

void PresentEngine::postKeyEvent(KeyEventType type, int charCode, int keyCode)
{
    impl_->requireDocument();
    impl_->document->postKeyEvent(toLokKeyType(type), charCode, keyCode);
}

void PresentEngine::postMouseEvent(
    MouseEventType type,
    int xTwips,
    int yTwips,
    int clickCount,
    int buttons,
    int modifiers)
{
    impl_->requireDocument();
    if (clickCount < 0) {
        throw std::invalid_argument("clickCount must not be negative");
    }
    impl_->document->postMouseEvent(
        toLokMouseType(type), xTwips, yTwips, clickCount, buttons, modifiers);
}

void PresentEngine::postUnoCommand(
    std::string_view command,
    std::string_view jsonArguments,
    bool notifyWhenFinished)
{
    impl_->requireDocument();
    const auto commandValue = requireNonEmpty(command, "command");
    const std::string arguments(jsonArguments);
    impl_->document->postUnoCommand(
        commandValue.c_str(),
        arguments.empty() ? nullptr : arguments.c_str(),
        notifyWhenFinished);
}

void PresentEngine::saveAs(
    std::string_view destinationPathOrUrl,
    std::string_view format,
    std::string_view filterOptions)
{
    impl_->requireDocument();
    const auto url = pathToFileUrl(requireNonEmpty(destinationPathOrUrl, "destinationPathOrUrl"));
    const std::string formatValue(format);
    const std::string optionsValue(filterOptions);
    if (!impl_->document->saveAs(
            url.c_str(),
            formatValue.empty() ? nullptr : formatValue.c_str(),
            optionsValue.empty() ? nullptr : optionsValue.c_str())) {
        throw impl_->lastError("failed to save presentation");
    }
}

void PresentEngine::setEventCallback(EventCallback callback)
{
    impl_->eventCallback = std::move(callback);
}

std::string PresentEngine::pathToFileUrl(std::string_view pathOrUrl)
{
    const std::string value(pathOrUrl);
    if (value.find("://") != std::string::npos) {
        return value;
    }
    const auto absolute = std::filesystem::absolute(std::filesystem::path(value)).lexically_normal();
    return "file://" + percentEncodePath(absolute.generic_string());
}

} // namespace cakeos::present
