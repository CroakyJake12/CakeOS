#include "cakeos/present/engine.hpp"

#include <stdexcept>

namespace cakeos::present {

void PresentEngine::addSlideAfter(int slideIndex)
{
    const auto before = slides().size();
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:InsertPage", {}, true);
    if (slides().size() != before + 1U) {
        throw std::runtime_error("LibreOffice did not add the requested slide");
    }
}

void PresentEngine::duplicateSlide(int slideIndex)
{
    const auto before = slides().size();
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:DuplicatePage", {}, true);
    if (slides().size() != before + 1U) {
        throw std::runtime_error("LibreOffice did not duplicate the requested slide");
    }
}

void PresentEngine::deleteSlide(int slideIndex)
{
    const auto slideList = slides();
    if (slideList.size() <= 1U) {
        throw std::logic_error("a presentation must keep at least one slide");
    }
    setCurrentSlide(slideIndex);
    postUnoCommand(".uno:DeletePage", {}, true);
    if (slides().size() + 1U != slideList.size()) {
        throw std::runtime_error("LibreOffice did not delete the requested slide");
    }
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
