# MoveMentor Chess

A chess training app built with Avalonia for manual play, PGN review, engine-assisted analysis, local coaching notes, player profiles, and opening training.

## Features

- manual move entry on the board
- legal move validation, including castling and promotion
- undo support
- PGN import
- batch PGN file import with player detection
- step-by-step replay of imported moves
- Stockfish-assisted game analysis and saved analysis history
- external engine top-move suggestions (`MultiPV`)
- evaluation bar showing which side is better and by how much
- move-quality labels for analysis:
  `Best`, `Excellent`, `Good`, `Inaccuracy`, `Mistake`, and `Blunder`
- planned future move-quality label: `Brilliant`
- Player Coach profile with recurring mistakes, trend signals, MoveMentor estimated strength, and weekly training plan
- Opening Trainer with daily recommendations, guided study, hints, session results, and next actions
- Opening Coverage dashboard for repertoire coverage, due review, weak branches, missing ECO signals, and direct practice launch
- local LLM advice runtime with heuristic fallback
- board rotation

## Requirements

- Windows
- .NET 8 SDK
- optional UCI chess engine executable placed next to the app at runtime for analysis
- optional local `llama.cpp` runtime and GGUF model for richer coaching text
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

The project supports a fully local `llama.cpp` advice runtime for short coaching explanations. If the runtime or model is missing, the app falls back to local heuristic coaching text.

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

## Product flow

The current product loop is:

1. Import PGN games.
2. Analyze games with Stockfish.
3. Review saved analyses and coaching advice.
4. Open Player Coach to see recurring mistakes, trend, estimated strength, and a weekly plan.
5. Use Opening Coverage to inspect repertoire coverage and launch practice from weak or due lines.
6. Complete Opening Trainer sessions so results can feed future recommendations and profile priorities.

`MoveMentor estimated strength` is a coaching estimate based on local analysis signals and available game metadata. It is not an official chess rating.

## Run

```powershell
dotnet build .\MoveMentorChess.sln
dotnet run --project .\MoveMentorChess.App\MoveMentorChess.App.csproj
```

If the app is already running, a normal `dotnet build` may fail to overwrite `MoveMentorChess.App.exe`. In that case, close the app first or build to a different output directory.

## Project structure

- `MoveMentorChess.App/Views/MainWindow.axaml` - main Avalonia window
- `MoveMentorChess.App/ViewModels/MainWindowViewModel.cs` - main UI orchestration, import/replay, and analysis commands
- `MoveMentorChess.App/Views/OpeningCoverageWindow.axaml` - repertoire coverage dashboard
- `MoveMentorChess.App/ViewModels/OpeningTrainerWindowViewModel.cs` - opening trainer UI state and session orchestration
- `MoveMentorChess.Analysis` - analysis services, mistake classification, advice generation, and local runtime integration
- `MoveMentorChess.Engine/StockfishEngine.cs` - communication with Stockfish
- `MoveMentorChess.Profiles` - player profile and strength estimation services
- `MoveMentorChess.Training` - training plans, opening trainer, coverage, recommendations, telemetry, and next actions
- `MoveMentorChess.Persistence` - SQLite store, migrations, analysis cache, opening data, and training history
- `MoveMentorChessServices.Tests` - service and persistence regression tests

## Data reset and migration policy

The SQLite database separates authoritative user data from derived analysis data.

Authoritative data should be preserved across analysis model changes:

- imported PGN records in `imported_games`
- opening seed/tree data in the opening tables
- opening training history in `opening_training_session_results`
- opening review items and scheduled training actions
- manual advice feedback in `move_advice_feedbacks`

Derived data can be rebuilt and is versioned with the `derived_analysis_data_version` metadata key:

- serialized `GameAnalysisResult` payloads in `analysis_results`
- normalized move analysis rows in `analysis_moves`
- analysis window state in `analysis_window_states`

When `SqliteAnalysisStore.CurrentDerivedAnalysisDataVersion` changes, startup clears only the derived tables above and records the new version. Schema migrations that need broader cleanup should add an explicit policy and test before deleting authoritative data.

## Notes

- `Tracker.cs` still exists in the project for OCR-related work, but the main import flow uses PGN files.
- The larger Avalonia windows are feature-rich and should be split into smaller renderers/view models before adding more large UI flows.

