#include "cakeos/present/engine.hpp"

#include <stdexcept>

namespace cakeos::present {

void PresentEngine::addSlideAfter(int slideIndex)
{
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:InsertPage", {}, true);
}

void PresentEngine::duplicateSlide(int slideIndex)
{
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:DuplicatePage", {}, true);
}

void PresentEngine::deleteSlide(int slideIndex)
{
    const auto slideList = slides();
    if (slideList.size() <= 1U) {
        throw std::logic_error("a presentation must keep at least one slide");
    }
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:DeletePage", {}, true);
}

void PresentEngine::undo()
{
    postUnoCommand(".uno:Undo", {}, true);
}

void PresentEngine::redo()
{
    postUnoCommand(".uno:Redo", {}, true);
}

} // namespace cakeos::present
