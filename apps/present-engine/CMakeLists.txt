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

add_library(cakeos-present-engine STATIC
    src/engine.cpp
)

target_include_directories(cakeos-present-engine
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE
        ${LIBREOFFICEKIT_INCLUDE_DIR}
)

target_compile_definitions(cakeos-present-engine PRIVATE LOK_USE_UNSTABLE_API)

target_compile_options(cakeos-present-engine PRIVATE
    -Wall
    -Wextra
    -Wpedantic
    -Werror
)

target_link_libraries(cakeos-present-engine PRIVATE ${CMAKE_DL_LIBS})

add_executable(cakeos-present-engine-smoke
    src/main.cpp
)

target_include_directories(cakeos-present-engine-smoke PRIVATE ${LIBREOFFICEKIT_INCLUDE_DIR})
target_link_libraries(cakeos-present-engine-smoke PRIVATE cakeos-present-engine)

enable_testing()
add_executable(cakeos-present-engine-contract-tests
    tests/contract_tests.cpp
)
target_link_libraries(cakeos-present-engine-contract-tests PRIVATE cakeos-present-engine)
add_test(NAME present-engine-contract COMMAND cakeos-present-engine-contract-tests)
