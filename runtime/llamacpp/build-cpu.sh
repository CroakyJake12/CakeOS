#!/bin/sh
set -eu

EXPECTED_SHA='5266f24da75dc449bd56cbed7addb9c8e4a6a73e'
SOURCE=${LLAMA_SOURCE:-}
BUILD=${LLAMA_BUILD_DIR:-}

if [ -z "$SOURCE" ] || [ -z "$BUILD" ]; then
  echo 'Set LLAMA_SOURCE to an existing llama.cpp checkout and LLAMA_BUILD_DIR to an empty/out-of-tree build directory.' >&2
  exit 64
fi
if [ ! -d "$SOURCE/.git" ]; then
  echo 'LLAMA_SOURCE is not a Git checkout.' >&2
  exit 65
fi
ACTUAL_SHA=$(git -C "$SOURCE" rev-parse HEAD)
if [ "$ACTUAL_SHA" != "$EXPECTED_SHA" ]; then
  echo "Refusing unpinned llama.cpp source: expected $EXPECTED_SHA, got $ACTUAL_SHA" >&2
  exit 66
fi

cmake -S "$SOURCE" -B "$BUILD" \
  -DCMAKE_BUILD_TYPE=Release \
  -DLLAMA_BUILD_SERVER=ON \
  -DLLAMA_BUILD_EXAMPLES=OFF \
  -DLLAMA_BUILD_TESTS=ON \
  -DGGML_CPU=ON \
  -DGGML_RPC=OFF \
  -DGGML_CUDA=OFF \
  -DGGML_HIP=OFF \
  -DGGML_VULKAN=OFF \
  -DGGML_SYCL=OFF \
  -DGGML_OPENCL=OFF \
  -DGGML_OPENVINO=OFF
cmake --build "$BUILD" --parallel
ctest --test-dir "$BUILD" --output-on-failure
