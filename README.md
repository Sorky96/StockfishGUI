# MoveMentor Chess

A chess training app built with Avalonia for manual play, PGN review, engine-assisted analysis, and local coaching notes.

## Features

- manual move entry on the board
- legal move validation, including castling and promotion
- undo support
- PGN import
- step-by-step replay of imported moves
- external engine top-move suggestions (`MultiPV`)
- evaluation bar showing which side is better and by how much
- board rotation

## Requirements

- Windows
- .NET 8 SDK
- optional UCI chess engine executable placed next to the app at runtime
- piece images from the `Images` folder copied to the output directory

## External engine setup

This repository does not include `stockfish.exe`.

MoveMentor Chess can use a local UCI-compatible engine for analysis. During development, `stockfish.exe` is the default executable name expected by the app; download Stockfish only from the official website and follow its license terms:

- [Stockfish Download](https://stockfishchess.org/download/)

For most Windows x64 systems, the recommended build is usually the `AVX2` version if your CPU supports it.

After downloading:

1. Extract the archive.
2. Place `stockfish.exe` in the application output directory, for example:
   `MoveMentorChess.App\bin\Debug\net8.0-windows\`
3. If the engine is missing, the app still starts, but analysis and evaluation stay disabled until `stockfish.exe` is available.

## Local LLM advice setup

The project now supports a fully local `llama.cpp` advice runtime for short coaching explanations.

Recommended setup:

- runtime: `llama-cli.exe`
- model family: `Qwen2.5-3B-Instruct-GGUF`
- recommended file: `qwen2.5-3b-instruct-q4_k_m.gguf`

Supported locations:

- `MoveMentorChess.App\bin\Debug\net8.0-windows\llama-cli.exe`
- `MoveMentorChess.App\bin\Debug\net8.0-windows\Models\qwen2.5-3b-instruct-q4_k_m.gguf`
- `.\llama.cpp\llama-cli.exe`
- `.\llama.cpp\models\qwen2.5-3b-instruct-q4_k_m.gguf`
- `.\tools\llama.cpp\llama-cli.exe`
- `.\tools\llama.cpp\models\qwen2.5-3b-instruct-q4_k_m.gguf`

The analysis window shows the current advice-runtime status and includes a `Test Advice Model` button for a smoke test.

For the complete English setup guide, see [LOCAL_LLM_SETUP.md](LOCAL_LLM_SETUP.md).

## Run

```powershell
dotnet build .\MoveMentorChess.sln
dotnet run --project .\MoveMentorChess.App\MoveMentorChess.App.csproj
```

If the app is already running, a normal `dotnet build` may fail to overwrite `MoveMentorChess.App.exe`. In that case, close the app first or build to a different output directory.

## Project structure

- `MoveMentorChess.App/Views/MainWindow.axaml` - main Avalonia window
- `MoveMentorChess.App/ViewModels/MainWindowViewModel.cs` - main UI orchestration, import/replay, and analysis commands
- `MoveMentorChessServices/Engine/StockfishEngine.cs` - communication with Stockfish
- `MoveMentorChessServices/Images` - piece assets copied to the output directory

## Notes

- `Tracker.cs` still exists in the project for OCR-related work, but the current import flow uses PGN files. Might be used for importing chess positions.

