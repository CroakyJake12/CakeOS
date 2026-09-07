#include "cakeos/present/engine.hpp"

#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <nlohmann/json.hpp>

#include <array>
#include <charconv>
#include <cstdint>
#include <iostream>
#include <limits>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace {

using Json = nlohmann::json;

constexpr std::uint32_t ProtocolVersion = 1;
constexpr std::uint32_t MaxMetadataBytes = 1024U * 1024U;
constexpr std::uint32_t MaxPayloadBytes = 64U * 1024U * 1024U;
constexpr int MaxRenderDimension = 4096;
constexpr std::uint64_t MaxRenderPixels = 16U * 1024U * 1024U;
constexpr std::size_t MaxQueuedEvents = 256U;

struct Frame {
    std::string metadata;
    std::vector<std::uint8_t> payload;
};

struct EventQueue {
    std::vector<Json> events;
    bool overflowed{false};
};

struct ElementSnapshotState {
    std::string path;
    bool fresh{false};

    void reset()
    {
        path.clear();
        fresh = false;
    }
};

std::uint32_t decodeU32Le(const std::array<std::uint8_t, 8>& header, std::size_t offset)
{
    return static_cast<std::uint32_t>(header[offset]) |
        (static_cast<std::uint32_t>(header[offset + 1U]) << 8U) |
        (static_cast<std::uint32_t>(header[offset + 2U]) << 16U) |
        (static_cast<std::uint32_t>(header[offset + 3U]) << 24U);
}

void encodeU32Le(std::array<std::uint8_t, 8>& header, std::size_t offset, std::uint32_t value)
{
    header[offset] = static_cast<std::uint8_t>(value & 0xFFU);
    header[offset + 1U] = static_cast<std::uint8_t>((value >> 8U) & 0xFFU);
    header[offset + 2U] = static_cast<std::uint8_t>((value >> 16U) & 0xFFU);
    header[offset + 3U] = static_cast<std::uint8_t>((value >> 24U) & 0xFFU);
}

void readExact(std::istream& input, char* destination, std::size_t size)
{
    if (size == 0U) {
        return;
    }
    input.read(destination, static_cast<std::streamsize>(size));
    if (static_cast<std::size_t>(input.gcount()) != size) {
        throw std::runtime_error("truncated Present worker frame");
    }
}

bool readFrame(std::istream& input, Frame& frame)
{
    std::array<std::uint8_t, 8> header{};
    input.read(reinterpret_cast<char*>(header.data()), static_cast<std::streamsize>(header.size()));
    const auto bytesRead = static_cast<std::size_t>(input.gcount());
    if (bytesRead == 0U && input.eof()) {
        return false;
    }
    if (bytesRead != header.size()) {
        throw std::runtime_error("truncated Present worker frame header");
    }

    const std::uint32_t metadataSize = decodeU32Le(header, 0U);
    const std::uint32_t payloadSize = decodeU32Le(header, 4U);
    if (metadataSize == 0U || metadataSize > MaxMetadataBytes) {
        throw std::runtime_error("Present worker metadata frame exceeds protocol limits");
    }
    if (payloadSize > MaxPayloadBytes) {
        throw std::runtime_error("Present worker binary frame exceeds protocol limits");
    }

    frame.metadata.assign(metadataSize, '\0');
    readExact(input, frame.metadata.data(), frame.metadata.size());
    frame.payload.resize(payloadSize);
    if (!frame.payload.empty()) {
        readExact(input, reinterpret_cast<char*>(frame.payload.data()), frame.payload.size());
    }
    return true;
}

void writeFrame(std::ostream& output, const Json& metadata, const std::vector<std::uint8_t>& payload = {})
{
    const std::string serialized = metadata.dump();
    if (serialized.empty() || serialized.size() > MaxMetadataBytes || payload.size() > MaxPayloadBytes) {
        throw std::runtime_error("Present worker response exceeds protocol limits");
    }

    std::array<std::uint8_t, 8> header{};
    encodeU32Le(header, 0U, static_cast<std::uint32_t>(serialized.size()));
    encodeU32Le(header, 4U, static_cast<std::uint32_t>(payload.size()));
    output.write(reinterpret_cast<const char*>(header.data()), static_cast<std::streamsize>(header.size()));
    output.write(serialized.data(), static_cast<std::streamsize>(serialized.size()));
    if (!payload.empty()) {
        output.write(reinterpret_cast<const char*>(payload.data()), static_cast<std::streamsize>(payload.size()));
    }
    output.flush();
    if (!output) {
        throw std::runtime_error("failed to write Present worker response");
    }
}

Json responseBase(const Json& id, bool ok)
{
    return Json{{"id", id}, {"ok", ok}, {"protocolVersion", ProtocolVersion}};
}

Json success(const Json& id, Json result = Json::object())
{
    Json response = responseBase(id, true);
    response["result"] = std::move(result);
    return response;
}

Json failure(const Json& id, const std::string& message)
{
    Json response = responseBase(id, false);
    response["error"] = Json{{"code", "operation_failed"}, {"message", message}};
    return response;
}

Json eventEnvelope(std::string_view name, Json data = Json::object())
{
    return Json{
        {"event", name},
        {"protocolVersion", ProtocolVersion},
        {"data", std::move(data)}
    };
}

void queueEvent(EventQueue& queue, Json event)
{
    if (queue.events.size() >= MaxQueuedEvents) {
        queue.overflowed = true;
        return;
    }
    queue.events.push_back(std::move(event));
}

std::optional<long> parseLong(std::string_view value)
{
    while (!value.empty() && value.front() == ' ') {
        value.remove_prefix(1U);
    }
    while (!value.empty() && value.back() == ' ') {
        value.remove_suffix(1U);
    }
    if (value.empty()) {
        return std::nullopt;
    }

    long parsed = 0;
    const auto result = std::from_chars(value.data(), value.data() + value.size(), parsed);
    if (result.ec != std::errc{} || result.ptr != value.data() + value.size()) {
        return std::nullopt;
    }
    return parsed;
}

std::vector<long> parseCommaSeparatedLongs(std::string_view payload)
{
    std::vector<long> values;
    std::size_t offset = 0U;
    while (offset <= payload.size()) {
        const auto comma = payload.find(',', offset);
        const auto end = comma == std::string_view::npos ? payload.size() : comma;
        const auto parsed = parseLong(payload.substr(offset, end - offset));
        if (!parsed.has_value()) {
            return {};
        }
        values.push_back(*parsed);
        if (comma == std::string_view::npos) {
            break;
        }
        offset = comma + 1U;
    }
    return values;
}

std::optional<Json> mapLibreOfficeEvent(const cakeos::present::EngineEvent& event)
{
    switch (event.upstreamType) {
    case LOK_CALLBACK_INVALIDATE_TILES: {
        if (event.payload == "EMPTY" || event.payload.empty()) {
            return eventEnvelope("canvasInvalidated", Json{{"all", true}, {"source", "libreoffice"}});
        }
        const auto values = parseCommaSeparatedLongs(event.payload);
        if (values.size() < 4U) {
            return eventEnvelope("canvasInvalidated", Json{{"all", true}, {"source", "libreoffice"}});
        }
        return eventEnvelope("canvasInvalidated", Json{
            {"all", false},
            {"source", "libreoffice"},
            {"rectTwips", Json{
                {"x", values[0]},
                {"y", values[1]},
                {"width", values[2]},
                {"height", values[3]}
            }}
        });
    }
    case LOK_CALLBACK_SET_PART: {
        const auto slideIndex = parseLong(event.payload);
        if (!slideIndex.has_value() || *slideIndex < 0 || *slideIndex > std::numeric_limits<int>::max()) {
            return std::nullopt;
        }
        return eventEnvelope("slideChanged", Json{{"slideIndex", static_cast<int>(*slideIndex)}});
    }
    case LOK_CALLBACK_CONTEXT_CHANGED: {
        const auto separator = event.payload.find(' ');
        const std::string context = separator == std::string::npos
            ? event.payload
            : event.payload.substr(separator + 1U);
        if (context.empty()) {
            return std::nullopt;
        }
        return eventEnvelope("editingContextChanged", Json{{"context", context}});
    }
    case LOK_CALLBACK_ERROR: {
        Json data{{"classification", "error"}, {"message", "LibreOffice reported an error"}};
        try {
            const Json upstream = Json::parse(event.payload);
            if (upstream.contains("classification") && upstream.at("classification").is_string()) {
                data["classification"] = upstream.at("classification");
            }
            if (upstream.contains("kind") && upstream.at("kind").is_string()) {
                data["kind"] = upstream.at("kind");
            }
            if (upstream.contains("code") && upstream.at("code").is_number_integer()) {
                data["code"] = upstream.at("code");
            }
            if (upstream.contains("message") && upstream.at("message").is_string()) {
                data["message"] = upstream.at("message");
            }
        } catch (const Json::exception&) {
            // Keep the stable fallback instead of leaking an unparsed upstream payload.
        }
        return eventEnvelope("engineError", std::move(data));
    }
    default:
        return std::nullopt;
    }
}

void queueMutationEvents(EventQueue& queue, const cakeos::present::PresentEngine& engine, std::string_view reason)
{
    Json changed{{"reason", reason}};
    Json repaint{{"all", true}, {"source", "semantic"}, {"reason", reason}};
    if (engine.isOpen()) {
        const int slideIndex = engine.currentSlide();
        changed["slideIndex"] = slideIndex;
        repaint["slideIndex"] = slideIndex;
    }
    queueEvent(queue, eventEnvelope("documentChanged", std::move(changed)));
    queueEvent(queue, eventEnvelope("canvasInvalidated", std::move(repaint)));
}

void flushEvents(std::ostream& output, EventQueue& queue)
{
    if (queue.overflowed) {
        writeFrame(output, eventEnvelope("canvasInvalidated", Json{
            {"all", true},
            {"source", "worker"},
            {"reason", "eventQueueOverflow"}
        }));
    }
    for (const auto& event : queue.events) {
        writeFrame(output, event);
    }
    queue.events.clear();
    queue.overflowed = false;
}

int checkedSlideTwips(long value)
{
    if (value <= 0 || value > std::numeric_limits<int>::max()) {
        throw std::overflow_error("slide extent cannot be represented by the tiled-rendering API");
    }
    return static_cast<int>(value);
}

void validateRenderSize(int width, int height)
{
    if (width <= 0 || height <= 0 || width > MaxRenderDimension || height > MaxRenderDimension) {
        throw std::invalid_argument("render dimensions must be between 1 and 4096 pixels");
    }
    const auto pixels = static_cast<std::uint64_t>(width) * static_cast<std::uint64_t>(height);
    if (pixels > MaxRenderPixels) {
        throw std::invalid_argument("render request exceeds the worker pixel budget");
    }
}

Json slideListResult(const cakeos::present::PresentEngine& engine)
{
    Json slides = Json::array();
    for (const auto& slide : engine.slides()) {
        slides.push_back(Json{
            {"index", slide.index},
            {"name", slide.name},
            {"hash", slide.hash}
        });
    }
    return Json{{"slides", std::move(slides)}};
}

Json elementListResult(
    const cakeos::present::PresentEngine& engine,
    const ElementSnapshotState& snapshot,
    int slideIndex)
{
    if (!engine.isOpen()) {
        throw std::logic_error("no presentation is open");
    }
    if (!engine.supportsElementSnapshots()) {
        throw std::runtime_error(
            "this LibreOfficeKit runtime does not support semantic element snapshots");
    }
    if (!snapshot.fresh || snapshot.path.empty()) {
        throw std::runtime_error(
            "element inventory is stale after unsaved mutations; save the presentation before requesting listElements");
    }

    // Validate against the live document as well as the saved snapshot.
    (void)engine.slideExtent(slideIndex);
    Json elements = Json::array();
    for (const auto& element : engine.elementSnapshot(snapshot.path, slideIndex)) {
        elements.push_back(Json{
            {"ref", "slide:" + std::to_string(element.slideIndex) + "/object:" + std::to_string(element.objectIndex)},
            {"slideIndex", element.slideIndex},
            {"objectIndex", element.objectIndex},
            {"referenceStability", "snapshot-only"},
            {"text", element.text}
        });
    }

    return Json{
        {"slideIndex", slideIndex},
        {"snapshotFresh", true},
        {"referenceStability", "snapshot-only"},
        {"elements", std::move(elements)}
    };
}

Json helloResult(const cakeos::present::PresentEngine& engine)
{
    Json capabilities = Json::array({
        "open",
        "close",
        "listSlides",
        "slideExtent",
        "renderSlide",
        "addSlideAfter",
        "duplicateSlide",
        "deleteSlide",
        "moveSlide",
        "undo",
        "redo",
        "saveAs"
    });
    if (engine.supportsElementSnapshots()) {
        capabilities.push_back("listElements");
    }

    return Json{
        {"engine", "cakeos-present"},
        {"protocolVersion", ProtocolVersion},
        {"framing", "u32le-json-length,u32le-binary-length,json,binary"},
        {"pixelTransport", "inline-binary-v1"},
        {"eventTransport", "framed-json-v1"},
        {"events", Json::array({
            "canvasInvalidated",
            "documentChanged",
            "slideChanged",
            "editingContextChanged",
            "engineError"
        })},
        {"capabilities", std::move(capabilities)}
    };
}

} // namespace

int main()
{
    try {
        cakeos::present::PresentEngine engine;
        EventQueue eventQueue;
        ElementSnapshotState elementSnapshot;
        engine.setEventCallback([&eventQueue](const cakeos::present::EngineEvent& event) {
            if (const auto mapped = mapLibreOfficeEvent(event); mapped.has_value()) {
                queueEvent(eventQueue, *mapped);
            }
        });

        Frame frame;
        while (readFrame(std::cin, frame)) {
            Json id = nullptr;
            bool shouldQuit = false;
            try {
                if (!frame.payload.empty()) {
                    throw std::invalid_argument("request binary payloads are not supported by protocol v1");
                }

                const Json request = Json::parse(frame.metadata);
                if (!request.is_object()) {
                    throw std::invalid_argument("request metadata must be a JSON object");
                }
                if (request.contains("id")) {
                    id = request.at("id");
                }
                const std::string operation = request.at("op").get<std::string>();

                if (operation == "hello") {
                    writeFrame(std::cout, success(id, helloResult(engine)));
                } else if (operation == "open") {
                    const std::string path = request.at("path").get<std::string>();
                    engine.open(path);
                    elementSnapshot.path = path;
                    elementSnapshot.fresh = true;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "close") {
                    engine.close();
                    elementSnapshot.reset();
                    writeFrame(std::cout, success(id));
                } else if (operation == "listSlides") {
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "listElements") {
                    writeFrame(std::cout, success(id, elementListResult(
                        engine,
                        elementSnapshot,
                        request.at("slideIndex").get<int>())));
                } else if (operation == "slideExtent") {
                    const int slideIndex = request.at("slideIndex").get<int>();
                    const auto extent = engine.slideExtent(slideIndex);
                    writeFrame(std::cout, success(id, Json{
                        {"slideIndex", slideIndex},
                        {"widthTwips", extent.widthTwips},
                        {"heightTwips", extent.heightTwips}
                    }));
                } else if (operation == "renderSlide") {
                    const int slideIndex = request.at("slideIndex").get<int>();
                    const int pixelWidth = request.at("pixelWidth").get<int>();
                    const int pixelHeight = request.at("pixelHeight").get<int>();
                    validateRenderSize(pixelWidth, pixelHeight);

                    const auto extent = engine.slideExtent(slideIndex);
                    cakeos::present::TileRequest renderRequest;
                    renderRequest.slideIndex = slideIndex;
                    renderRequest.pixelWidth = pixelWidth;
                    renderRequest.pixelHeight = pixelHeight;
                    renderRequest.tileWidthTwips = checkedSlideTwips(extent.widthTwips);
                    renderRequest.tileHeightTwips = checkedSlideTwips(extent.heightTwips);
                    const auto tile = engine.renderTile(renderRequest);
                    const char* pixelFormat = tile.pixelFormat == cakeos::present::PixelFormat::Rgba
                        ? "RGBA"
                        : "BGRA";
                    writeFrame(std::cout, success(id, Json{
                        {"slideIndex", slideIndex},
                        {"pixelWidth", tile.pixelWidth},
                        {"pixelHeight", tile.pixelHeight},
                        {"pixelFormat", pixelFormat},
                        {"bytesPerPixel", 4}
                    }), tile.pixels);
                } else if (operation == "addSlideAfter") {
                    engine.addSlideAfter(request.at("slideIndex").get<int>());
                    elementSnapshot.fresh = false;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                    queueMutationEvents(eventQueue, engine, operation);
                } else if (operation == "duplicateSlide") {
                    engine.duplicateSlide(request.at("slideIndex").get<int>());
                    elementSnapshot.fresh = false;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                    queueMutationEvents(eventQueue, engine, operation);
                } else if (operation == "deleteSlide") {
                    engine.deleteSlide(request.at("slideIndex").get<int>());
                    elementSnapshot.fresh = false;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                    queueMutationEvents(eventQueue, engine, operation);
                } else if (operation == "moveSlide") {
                    engine.moveSlide(
                        request.at("fromIndex").get<int>(),
                        request.at("toIndex").get<int>());
                    elementSnapshot.fresh = false;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                    queueMutationEvents(eventQueue, engine, operation);
                } else if (operation == "undo") {
                    engine.undo();
                    elementSnapshot.fresh = false;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                    queueMutationEvents(eventQueue, engine, operation);
                } else if (operation == "redo") {
                    engine.redo();
                    elementSnapshot.fresh = false;
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                    queueMutationEvents(eventQueue, engine, operation);
                } else if (operation == "saveAs") {
                    const std::string path = request.at("path").get<std::string>();
                    engine.saveAs(
                        path,
                        request.value("format", std::string{}),
                        request.value("filterOptions", std::string{}));
                    elementSnapshot.path = path;
                    elementSnapshot.fresh = true;
                    writeFrame(std::cout, success(id));
                } else if (operation == "quit") {
                    writeFrame(std::cout, success(id));
                    shouldQuit = true;
                } else {
                    throw std::invalid_argument("unknown Present worker operation: " + operation);
                }
            } catch (const std::exception& error) {
                writeFrame(std::cout, failure(id, error.what()));
            }

            flushEvents(std::cout, eventQueue);
            if (shouldQuit) {
                break;
            }
        }
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present-worker fatal error: " << error.what() << '\n';
        return 1;
    }
}
