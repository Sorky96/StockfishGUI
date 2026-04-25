# Local LLM Setup

MoveMentor Chess can generate short chess advice with a fully local `llama.cpp` runtime.

The app supports two runtime modes (auto-detected):

| Mode | Binary | Speed | How it works |
|------|--------|-------|--------------|
| **Server (recommended)** | `llama-server.exe` | ~0.5–2s per move | Model loads once; requests via HTTP on `127.0.0.1` |
| **CLI (fallback)** | `llama-cli.exe` | ~10–30s per move | New process per request; model reloaded each time |

- recommended model family: `Qwen2.5-3B-Instruct-GGUF`
- recommended file for a balanced Windows CPU setup: `qwen2.5-3b-instruct-q4_k_m.gguf`

This keeps the project local-only, predictable, and easy to support.

## Why this model

The current recommendation is based on three practical constraints:

- it is available in GGUF format, which `llama.cpp` supports directly
- the 3B size is realistic for local Windows setups
- the `Q4_K_M` quantization keeps the file relatively small while remaining useful for short structured outputs

## What to install

1. Download `llama.cpp` for Windows and extract **`llama-server.exe`** (recommended) or `llama-cli.exe`.
2. Download the model file:
   `qwen2.5-3b-instruct-q4_k_m.gguf`
3. Place the files in one of the supported layouts below.

## Supported file layouts

Option A: next to the app

```text
MoveMentorChess/
  MoveMentorChess.App/bin/Debug/net8.0-windows/
    llama-server.exe    (and/or llama-cli.exe)
    Models/
      qwen2.5-3b-instruct-q4_k_m.gguf
```

Option B: dedicated `llama.cpp` folder

```text
MoveMentorChess/
  llama.cpp/
    llama-server.exe    (and/or llama-cli.exe)
    models/
      qwen2.5-3b-instruct-q4_k_m.gguf
```

Option C: dedicated tools folder

```text
MoveMentorChess/
  tools/
    llama.cpp/
      llama-server.exe    (and/or llama-cli.exe)
      models/
        qwen2.5-3b-instruct-q4_k_m.gguf
```

The app also recognizes `MoveMentorChessServices-advice.gguf` and `MoveMentorChessServices-advice-q4_k_m.gguf`, but renaming is not required for the recommended Qwen file.

## How the server mode works

When `llama-server.exe` is detected:

1. The app starts `llama-server` as a child process on first analysis request.
2. The server binds to `127.0.0.1` on an automatically chosen free port — **no network exposure**.
3. The model is loaded into memory **once**.
4. Each advice request is a fast HTTP POST (`/completion`) — typically under 2 seconds.
5. The server process is automatically killed when the app exits.

When only `llama-cli.exe` is available, the app falls back to starting a new process per request (slower, but still functional).

## Optional environment overrides

If you want to keep the binary or the model elsewhere, you can set:

- `MoveMentorChessServices_LLAMA_CPP_SERVER_PATH` — path to `llama-server.exe`
- `MoveMentorChessServices_LLAMA_CPP_CLI_PATH` — path to `llama-cli.exe`
- `MoveMentorChessServices_LLAMA_CPP_MODEL_PATH` — path to the `.gguf` model file
- `MoveMentorChessServices_LLAMA_CPP_MAX_TOKENS` — max tokens per response (default: 96)
- `MoveMentorChessServices_LLAMA_CPP_CONTEXT_SIZE` — context window size (default: 2048)
- `MoveMentorChessServices_LLAMA_CPP_TIMEOUT_MS` — per-request timeout in ms (default: 15000 for server, 120000 for cli)
- `MoveMentorChessServices_LLAMA_SERVER_PORT` — fixed port for `llama-server` (default: auto)
- `MoveMentorChessServices_LLAMA_SERVER_STARTUP_TIMEOUT_MS` — max wait for server to become healthy (default: 60000)

## Verify inside the app

1. Open an imported game analysis window.
2. Look at the advice model status banner.
3. Click `Test Advice Model`.

If the runtime is ready, the app will run a smoke test and show a sample short advice response.

## Notes

- If the local LLM is not ready, MoveMentor Chess falls back to the heuristic advice generator.
- The chess engine analysis still uses `stockfish.exe`; the local LLM is only used for human-readable coaching text.
- The current prompt and parser are tuned for short structured JSON output, not open-ended chat.
- The server listens only on `127.0.0.1` and is never exposed to the network.
