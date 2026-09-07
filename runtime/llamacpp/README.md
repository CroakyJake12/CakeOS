# Haven llama.cpp runtime — Slice 0

This directory implements the first production boundary for local GGUF inference. It does **not** bundle a model, download a model, install packages, modify the approved VM, or expose llama.cpp directly to HUI.

## Evidence state

- Upstream `ggml-org/llama.cpp` v0.4.0 provenance: **inspected and pinned**.
- Haven broker/model admission code: **implemented**.
- Repository unit tests: **implemented; CI result must be checked before calling tested**.
- Pinned llama.cpp source: **not copied into this repository**.
- llama.cpp CPU binary: **not built by this branch yet**.
- Model inference: **not runtime-proven**.
- Approved Ubuntu VM: **unchanged**.
- GPU backends: **not built, tested, benchmarked, or runtime-proven**.

## Boundary

```text
HUI
  |
  | Haven API over $XDG_RUNTIME_DIR/haven/inference.sock (0600)
  v
broker.py
  |
  | private Unix socket
  v
llama-server (single model, --no-ui, --parallel 1)
  |
  v
verified content-addressed GGUF
```

The systemd user service restricts the broker and its worker to `AF_UNIX`, denies network access, makes the home directory read-only, gives write access only to the runtime directory, and grants read-only access to the Haven model store. This means Slice 0 deliberately does not proxy Ollama; the existing Ollama client can continue to operate as a separate provider until a later Haven provider broker is introduced.

## HUI-facing API

- `GET /health`
- `GET /v1/models`
- `POST /v1/models/{id}/load`
- `POST /v1/models/{id}/unload`
- `POST /v1/chat/completions` (streaming; requires `request_id`)
- `POST /v1/requests/{request_id}/cancel`

The API is Haven-owned. Upstream llama-server endpoints are an internal implementation detail.

## Model store

Default root: `$XDG_DATA_HOME/haven/models` (or `~/.local/share/haven/models`).

```text
models/
  blobs/sha256/<prefix>/<digest>.gguf
  manifests/<catalogue-id>.json
```

Before a model is started, Slice 0 checks:

1. the manifest cannot escape the content-addressed blob root;
2. the blob exists and has the declared byte size;
3. its header has GGUF magic;
4. its GGUF version is 2 or 3 and is allowed by the manifest;
5. its SHA-256 matches the manifest.

A future catalogue/downloader must stage downloads elsewhere and atomically adopt them only after these checks plus model-license review. GGUF compatibility is never treated as redistribution permission.

## Upstream build

`upstream.lock.json` pins v0.4.0 to commit `5266f24da75dc449bd56cbed7addb9c8e4a6a73e`. `build-cpu.sh` refuses any other checkout and configures the Slice 0 CPU build with RPC, CUDA, HIP, Vulkan, SYCL, OpenCL, and OpenVINO disabled.

The build script intentionally performs no clone or package installation. Set `LLAMA_SOURCE` to an already approved checkout and `LLAMA_BUILD_DIR` to an out-of-tree directory before running it.

## Packaging target

Expected installed files:

```text
/usr/lib/haven/inference/broker.py
/usr/lib/haven/inference/gguf.py
/usr/lib/haven/llama.cpp/llama-server
/usr/lib/systemd/user/haven-inference-broker.service
```

The model store remains user-owned and outside the OS image.

## Ollama migration

Slice 0 does not remove or mutate Ollama. The stable long-term HUI concept is a provider-qualified model ID (for example `ollama:qwen...` versus `llamacpp:<catalogue-id>`). During migration, HUI can retain its existing Ollama path while opting individual validated models into this llama.cpp broker. Ollama retirement is not part of this slice.

## Next acceptance gates

1. CI passes for this branch.
2. Build pinned v0.4.0 CPU source in an isolated Linux builder and record compiler/CMake versions, build logs, test results, binary SHA-256, dynamic dependencies, and licence notices.
3. Only with a separately approved permissively licensed GGUF, run the model admission and inference smoke tests.
4. Only after that, stage the package into the preserved approved Ubuntu VM and gather runtime evidence.
5. Add accelerator backends independently; no backend inherits a `runtime-proven` label from CPU.
