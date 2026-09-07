cmake_minimum_required(VERSION 3.20)
project(cakeos_present_engine LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

find_path(LIBREOFFICEKIT_INCLUDE_DIR
    NAMES LibreOfficeKit/LibreOfficeKit.hxx
    PATH_SUFFIXES libreoffice
)

if(NOT LIBREOFFICEKIT_INCLUDE_DIR)
    message(FATAL_ERROR "LibreOfficeKit headers not found. Install libreofficekit-dev.")
endif()

find_package(nlohmann_json 3.2.0 REQUIRED)

function(cakeos_enable_strict_warnings target)
    target_compile_options(${target} PRIVATE
        -Wall
        -Wextra
        -Wpedantic
        -Werror
    )
endfunction()

add_library(cakeos-present-engine STATIC
    src/engine.cpp
    src/semantic.cpp
)

target_include_directories(cakeos-present-engine
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${LIBREOFFICEKIT_INCLUDE_DIR}
)

target_compile_definitions(cakeos-present-engine PRIVATE LOK_USE_UNSTABLE_API)
target_link_libraries(cakeos-present-engine PRIVATE ${CMAKE_DL_LIBS})
cakeos_enable_strict_warnings(cakeos-present-engine)

add_executable(cakeos-present-worker
    src/worker.cpp
)
target_include_directories(cakeos-present-worker PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_compile_definitions(cakeos-present-worker PRIVATE LOK_USE_UNSTABLE_API)
target_link_libraries(cakeos-present-worker PRIVATE
    cakeos-present-engine
    nlohmann_json::nlohmann_json
)
cakeos_enable_strict_warnings(cakeos-present-worker)

add_executable(cakeos-present-engine-smoke
    src/main.cpp
)
target_include_directories(cakeos-present-engine-smoke PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_link_libraries(cakeos-present-engine-smoke PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-smoke)

add_executable(cakeos-present-engine-lifecycle-smoke
    tests/lifecycle_smoke.cpp
)
target_link_libraries(cakeos-present-engine-lifecycle-smoke PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-lifecycle-smoke)

enable_testing()
add_executable(cakeos-present-engine-contract-tests
    tests/contract_tests.cpp
)
target_link_libraries(cakeos-present-engine-contract-tests PRIVATE cakeos-present-engine)
cakeos_enable_strict_warnings(cakeos-present-engine-contract-tests)
add_test(NAME present-engine-contract COMMAND cakeos-present-engine-contract-tests)
