# StockifhsGUI

A simple Windows Forms chess GUI for playing moves manually with Stockfish analysis.

## Features

- manual move entry on the board
- legal move validation, including castling and promotion
- undo support
- PGN import
- step-by-step replay of imported moves
- Stockfish top-move suggestions (`MultiPV`)
- evaluation bar showing which side is better and by how much
- board rotation

## Requirements

- Windows
- .NET 8 SDK
- Stockfish executable placed next to the app at runtime
- piece images from the `Images` folder copied to the output directory

## Stockfish setup

This repository does not include `stockfish.exe`.

Download Stockfish from the official website:

- [Stockfish Download](https://stockfishchess.org/download/)

For most Windows x64 systems, the recommended build is usually the `AVX2` version if your CPU supports it.

After downloading:

1. Extract the archive.
2. Place `stockfish.exe` in the application output directory, for example:
   `StockifhsGUI\bin\Debug\net8.0-windows\`
3. If the engine is missing, the app still starts, but analysis and evaluation stay disabled until `stockfish.exe` is available.

## Local LLM advice setup

The project now supports a fully local `llama.cpp` advice runtime for short coaching explanations.

Recommended setup:

- runtime: `llama-cli.exe`
- model family: `Qwen2.5-3B-Instruct-GGUF`
- recommended file: `qwen2.5-3b-instruct-q4_k_m.gguf`

Supported locations:

- `StockifhsGUI\bin\Debug\net8.0-windows\llama-cli.exe`
- `StockifhsGUI\bin\Debug\net8.0-windows\Models\qwen2.5-3b-instruct-q4_k_m.gguf`
- `.\llama.cpp\llama-cli.exe`
- `.\llama.cpp\models\qwen2.5-3b-instruct-q4_k_m.gguf`
- `.\tools\llama.cpp\llama-cli.exe`
- `.\tools\llama.cpp\models\qwen2.5-3b-instruct-q4_k_m.gguf`

The analysis window shows the current advice-runtime status and includes a `Test Advice Model` button for a smoke test.

For the complete English setup guide, see [LOCAL_LLM_SETUP.md](LOCAL_LLM_SETUP.md).

## Run

```powershell
dotnet build .\StockifhsGUI.sln
dotnet run --project .\StockifhsGUI\StockifhsGUI.csproj
```

If the app is already running, a normal `dotnet build` may fail to overwrite `StockifhsGUI.exe`. In that case, close the app first or build to a different output directory.

## Project structure

- `StockifhsGUI/UI/Forms/MainForm.cs` - main window, layout orchestration, engine summary and board hosting
- `StockifhsGUI/UI/Forms/MainForm.Import.cs` - undo support and PGN import/replay
- `StockifhsGUI/StockfishEngine.cs` - communication with Stockfish
- `StockifhsGUI/PromotionForm.cs` - promotion dialog
- `StockifhsGUI/Images` - piece assets copied to the output directory

## Notes

- `Tracker.cs` still exists in the project for OCR-related work, but the current import flow uses PGN files.
- The project name contains a typo (`StockifhsGUI`) and has been kept as-is to match the existing solution structure.
