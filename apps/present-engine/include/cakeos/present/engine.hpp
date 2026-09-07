#pragma once

#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <string_view>
#include <vector>

namespace cakeos::present {

struct EngineOptions {
    std::string libreOfficeProgramPath{ "/usr/lib/libreoffice/program" };
    std::string userProfileUrl;
};

struct SlideInfo {
    int index{};
    std::string name;
    std::string hash;
};

struct TileRequest {
    int slideIndex{};
    int pixelWidth{};
    int pixelHeight{};
    int tileXTwips{};
    int tileYTwips{};
    int tileWidthTwips{};
    int tileHeightTwips{};
};

struct RenderedTile {
    int pixelWidth{};
    int pixelHeight{};
    int tileMode{};
    std::vector<std::uint8_t> pixels;
};

struct EngineEvent {
    int type{};
    std::string payload;
};

using EventCallback = std::function<void(const EngineEvent&)>;

class PresentEngine final {
public:
    explicit PresentEngine(EngineOptions options = {});
    ~PresentEngine();

    PresentEngine(const PresentEngine&) = delete;
    PresentEngine& operator=(const PresentEngine&) = delete;
    PresentEngine(PresentEngine&&) noexcept;
    PresentEngine& operator=(PresentEngine&&) noexcept;

    void open(std::string_view documentPathOrUrl);
    void close() noexcept;
    [[nodiscard]] bool isOpen() const noexcept;

    [[nodiscard]] std::vector<SlideInfo> slides() const;
    [[nodiscard]] int currentSlide() const;
    void setCurrentSlide(int slideIndex);

    [[nodiscard]] RenderedTile renderTile(const TileRequest& request);

    void postKeyEvent(int type, int charCode, int keyCode);
    void postMouseEvent(int type, int xTwips, int yTwips, int clickCount, int buttons, int modifiers);
    void postUnoCommand(std::string_view command, std::string_view jsonArguments = {}, bool notifyWhenFinished = false);

    void saveAs(std::string_view destinationPathOrUrl, std::string_view format = {}, std::string_view filterOptions = {});

    [[nodiscard]] std::string presentationInfo() const;
    void setEventCallback(EventCallback callback);

    [[nodiscard]] static std::string pathToFileUrl(std::string_view pathOrUrl);

private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};

} // namespace cakeos::present
