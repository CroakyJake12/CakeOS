#include "cakeos/present/engine.hpp"

#include <array>
#include <iostream>
#include <stdexcept>
#include <string>
#include <string_view>

namespace {

template <std::size_t N>
void requireOrder(
    const cakeos::present::PresentEngine& engine,
    const std::array<std::string_view, N>& expected,
    const char* stage)
{
    const auto slides = engine.slides();
    if (slides.size() != expected.size()) {
        throw std::runtime_error(
            std::string(stage) + ": expected " + std::to_string(expected.size()) +
            " slides, got " + std::to_string(slides.size()));
    }

    for (std::size_t index = 0; index < expected.size(); ++index) {
        if (slides[index].name != expected[index]) {
            throw std::runtime_error(
                std::string(stage) + ": expected slide " + std::to_string(index) +
                " to be '" + std::string(expected[index]) + "', got '" + slides[index].name + "'");
        }
    }
}

constexpr std::array InitialOrder{
    std::string_view{"Alpha"},
    std::string_view{"Beta"},
    std::string_view{"Gamma"}
};

constexpr std::array MovedOrder{
    std::string_view{"Beta"},
    std::string_view{"Gamma"},
    std::string_view{"Alpha"}
};

} // namespace

int main(int argc, char** argv)
{
    if (argc != 3) {
        std::cerr << "Usage: cakeos-present-engine-reorder-smoke <input.odp> <output.odp>\n";
        return 2;
    }

    try {
        cakeos::present::PresentEngine engine;
        engine.open(argv[1]);
        requireOrder(engine, InitialOrder, "initial order");

        engine.moveSlide(0, 2);
        requireOrder(engine, MovedOrder, "move first slide to end");
        if (engine.currentSlide() != 2) {
            throw std::runtime_error("moved slide did not become the current slide at its destination");
        }

        engine.undo();
        requireOrder(engine, InitialOrder, "undo reorder");

        engine.redo();
        requireOrder(engine, MovedOrder, "redo reorder");

        engine.saveAs(argv[2], "odp");
        engine.close();
        engine.open(argv[2]);
        requireOrder(engine, MovedOrder, "saved reorder reopen");

        std::cout << "slide_reorder=passed\n";
        std::cout << "slide_order=Beta,Gamma,Alpha\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present-engine reorder smoke failed: " << error.what() << '\n';
        return 1;
    }
}
