#include "cakeos/present/engine.hpp"

#include <nlohmann/json.hpp>

#include <array>
#include <cstdint>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

using Json = nlohmann::json;

constexpr std::uint32_t ProtocolVersion = 1;
constexpr std::uint32_t MaxMetadataBytes = 1024U * 1024U;
constexpr std::uint32_t MaxPayloadBytes = 64U * 1024U * 1024U;
constexpr int MaxRenderDimension = 4096;
constexpr std::uint64_t MaxRenderPixels = 16U * 1024U * 1024U;

struct Frame {
    std::string metadata;
    std::vector<std::uint8_t> payload;
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
        readExact(
            input,
            reinterpret_cast<char*>(frame.payload.data()),
            frame.payload.size());
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
        output.write(
            reinterpret_cast<const char*>(payload.data()),
            static_cast<std::streamsize>(payload.size()));
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

Json helloResult()
{
    return Json{
        {"engine", "cakeos-present"},
        {"protocolVersion", ProtocolVersion},
        {"framing", "u32le-json-length,u32le-binary-length,json,binary"},
        {"pixelTransport", "inline-binary-v1"},
        {"capabilities", Json::array({
            "open",
            "close",
            "listSlides",
            "slideExtent",
            "renderSlide",
            "addSlideAfter",
            "duplicateSlide",
            "deleteSlide",
            "undo",
            "redo",
            "saveAs"
        })}
    };
}

} // namespace

int main()
{
    try {
        cakeos::present::PresentEngine engine;
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
                    writeFrame(std::cout, success(id, helloResult()));
                } else if (operation == "open") {
                    engine.open(request.at("path").get<std::string>());
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "close") {
                    engine.close();
                    writeFrame(std::cout, success(id));
                } else if (operation == "listSlides") {
                    writeFrame(std::cout, success(id, slideListResult(engine)));
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
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "duplicateSlide") {
                    engine.duplicateSlide(request.at("slideIndex").get<int>());
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "deleteSlide") {
                    engine.deleteSlide(request.at("slideIndex").get<int>());
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "undo") {
                    engine.undo();
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "redo") {
                    engine.redo();
                    writeFrame(std::cout, success(id, slideListResult(engine)));
                } else if (operation == "saveAs") {
                    engine.saveAs(
                        request.at("path").get<std::string>(),
                        request.value("format", std::string{}),
                        request.value("filterOptions", std::string{}));
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
