#include "cakeos/present/engine.hpp"

#include <LibreOfficeKit/LibreOfficeKit.hxx>
#include <LibreOfficeKit/LibreOfficeKitEnums.h>
#include <nlohmann/json.hpp>

#include <filesystem>
#include <iostream>
#include <memory>
#include <stdexcept>
#include <string>

namespace {

using Json = nlohmann::json;

bool containsString(const Json& value, const std::string& expected)
{
    if (value.is_string()) {
        return value.get<std::string>() == expected;
    }
    if (value.is_array()) {
        for (const auto& child : value) {
            if (containsString(child, expected)) {
                return true;
            }
        }
    } else if (value.is_object()) {
        for (const auto& [key, child] : value.items()) {
            (void)key;
            if (containsString(child, expected)) {
                return true;
            }
        }
    }
    return false;
}

std::string takeCommandValues(lok::Office& office, lok::Document& document, const char* command)
{
    char* raw = document.getCommandValues(command);
    if (raw == nullptr) {
        return {};
    }
    std::string result(raw);
    office.freeError(raw);
    return result;
}

} // namespace

int main(int argc, char** argv)
{
    if (argc != 2) {
        std::cerr << "Usage: cakeos-present-structure-probe <input.odp>\n";
        return 2;
    }

    try {
        const auto profile = std::filesystem::temp_directory_path() / "cakeos-present-structure-probe-profile";
        std::error_code ignored;
        std::filesystem::remove_all(profile, ignored);
        std::filesystem::create_directories(profile);
        const std::string profileUrl = cakeos::present::PresentEngine::pathToFileUrl(profile.string());

        std::unique_ptr<lok::Office> office(lok::lok_cpp_init(
            "/usr/lib/libreoffice/program",
            profileUrl.c_str()));
        if (!office) {
            throw std::runtime_error("failed to initialise LibreOfficeKit");
        }

        const std::string documentUrl = cakeos::present::PresentEngine::pathToFileUrl(argv[1]);
        std::unique_ptr<lok::Document> document(office->documentLoad(documentUrl.c_str()));
        if (!document || document->getDocumentType() != LOK_DOCTYPE_PRESENTATION) {
            throw std::runtime_error("failed to open presentation through LibreOfficeKit");
        }
        document->initializeForRendering();
        document->setPartMode(LOK_PARTMODE_SLIDES);

        std::string structureText = takeCommandValues(
            *office,
            *document,
            ".uno:ExtractDocumentStructure?filter=slides");
        std::string commandVariant = "filtered";
        if (structureText.empty()) {
            structureText = takeCommandValues(*office, *document, ".uno:ExtractDocumentStructure");
            commandVariant = "unfiltered";
        }
        if (structureText.empty()) {
            throw std::runtime_error("ExtractDocumentStructure returned no data through LibreOfficeKit");
        }

        const Json structure = Json::parse(structureText);
        if (!structure.contains("Slides") || !structure.at("Slides").is_object()) {
            throw std::runtime_error("document structure has no Slides object");
        }
        const Json& slides = structure.at("Slides");
        if (!slides.contains("Slide 0")) {
            throw std::runtime_error("document structure has no Slide 0");
        }
        const Json& slide0 = slides.at("Slide 0");
        if (!slide0.contains("Objects") || !slide0.at("Objects").is_object()) {
            throw std::runtime_error("Slide 0 has no semantic Objects inventory");
        }
        const auto objectCount = slide0.at("Objects").size();
        if (objectCount == 0U) {
            throw std::runtime_error("Slide 0 semantic Objects inventory is empty");
        }
        if (!containsString(slide0.at("Objects"), "Alpha")) {
            throw std::runtime_error("semantic object inventory does not expose the Alpha title text");
        }

        std::cout << "structure_inventory=passed\n";
        std::cout << "structure_command_variant=" << commandVariant << '\n';
        std::cout << "structure_slide0_objects=" << objectCount << '\n';
        std::cout << "structure_text=Alpha\n";

        document.reset();
        office.reset();
        std::filesystem::remove_all(profile, ignored);
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "present structure probe failed: " << error.what() << '\n';
        return 1;
    }
}
