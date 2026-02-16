# Cookies / CrosswordGO

Auto-generated project documentation. Last update: `2026-02-16 20:40`.

## Overview

- Mobile crossword game runtime in `Assets/WordChef`, plus an editor-side crossword generator in `Assets/QtCrossword`.
- Runtime supports JSON-driven crossword layouts (`CrosswordConfigs`) and optional bonus words (`HiddenWords`).
- The Qt generator can build variants, apply constraints, manage hidden words, and save level JSON files.
- Additional editor utilities convert CWZ files and configure gold line visual assets.

## Environment

- Unity editor: `2022.3.27f1`
- Key scenes: `Assets/Scenes/SampleScene.unity, Assets/WordChef/_Scenes/Home.unity, Assets/WordChef/_Scenes/Main.unity, Assets/WordChef/_Scenes/SelectLevel.unity, Assets/WordChef/_Scenes/SelectWorld.unity`

### Package Snapshot

- `com.unity.collab-proxy`: `2.3.1`
- `com.unity.feature.2d`: `2.0.0`
- `com.unity.feature.mobile`: `1.0.0`
- `com.unity.ide.rider`: `3.0.28`
- `com.unity.ide.visualstudio`: `2.0.22`
- `com.unity.ide.vscode`: `1.2.5`
- `com.unity.test-framework`: `1.1.33`
- `com.unity.textmeshpro`: `3.0.6`
- `com.unity.timeline`: `1.7.6`
- `com.unity.ugui`: `1.0.0`
- `com.unity.visualscripting`: `1.9.4`

## Repository Layout

- `Assets/WordChef`: game runtime, scenes, prefabs, resources, word lists.
- `Assets/QtCrossword`: crossword generation models + editor window.
- `LevelsOrg`: source CWZ levels (input for converter).
- `Utils/WorldChef`: utility scripts (including this README generator).
- `Docs/Screenshots`: optional screenshots auto-embedded into this README.

## Runtime Flow

1. `MainController` loads `GameLevel` and falls back to JSON crossword configs if needed.
2. `Pan` builds center letters from unique letters in current crossword JSON.
3. `WordRegion` builds a shared-cell crossword grid from virtual coordinates.
4. `LineDrawer` handles drag input, previews text, and evaluates submitted words.
5. Valid answers reveal cells; hidden words grant reward points and persist collected state.

## Level Data Format

`CrosswordData` schema (`Assets/WordChef/_Scripts/Main/CrosswordConfig.cs`):

```json
{
  "CrosswordConfigs": [
    { "Answer": "star", "XPos": 2, "YPos": 0, "Direction": 1 }
  ],
  "HiddenWords": ["taser", "rate", "seat"]
}
```

- Sample level: `Assets/WordChef/Resources/World_0/SubWorld_0/LevelCW_0.json`
- Crossword entries: `5`
- Hidden words: `10`
- Unique crossword letters: `5`
- Hidden preview: `ate, east, era, est, rat, rate, rest, seat, sta, taser`

## Qt Crossword Generator

Main window: `Tools/Qt Crossword Generator`.

- Input modes: manual words, constrained generation, input-only override.
- Variant operations: generate, navigate, rotate, save/load JSON.
- Hidden words: auto-add from unique letters, manual add via input + `+`, remove via per-word `-`.
- Hidden-word auto-search timeout and resume cursor prevent UI hangs.
- Debug dropdown currently includes `Erase Save` (clears save data and caches).

### Feature Flags Detected

- `debug_erase_save`: `ON`
- `hidden_word_timeout`: `ON`
- `hidden_words_schema`: `ON`
- `manual_hidden_input`: `ON`
- `reverse_hidden_match`: `ON`

## Editor Menus

- `Superpow/Clear all playerprefs` -> `Assets/WordChef/Common/Scripts/Editor/SuperpowWindowEditor.cs`
- `Superpow/Credit balance (ruby, coin..)` -> `Assets/WordChef/Common/Scripts/Editor/SuperpowWindowEditor.cs`
- `Superpow/Set balance to 0` -> `Assets/WordChef/Common/Scripts/Editor/SuperpowWindowEditor.cs`
- `Superpow/Unlock all levels` -> `Assets/WordChef/Common/Scripts/Editor/SuperpowWindowEditor.cs`
- `Tools/Convert CWZ Levels` -> `Assets/WordChef/_Scripts/Utils/CwzConverter.cs`
- `Tools/Line Drawer/Setup Gold Line Visuals` -> `Assets/WordChef/_Scripts/Utils/LineDrawerVisualSetup.cs`
- `Tools/MyTool/Create My Scriptable Object` -> `Assets/WordChef/Common/Scripts/Editor/MakeScriptableObject.cs`
- `Tools/Qt Crossword Generator` -> `Assets/QtCrossword/Editor/CrosswordToolWindow.cs`
- `Tools/Saad Khawaja/Instant High-Res Screenshot` -> `Assets/WordChef/Common/Scripts/Editor/Instant Screenshot/ScreenshotTaker.cs`
- `Tools/Word Uniqueness Checker` -> `Assets/WordChef/_Scripts/Utils/WordUniquenessTool.cs`

## Word List Stats (English)

- `3_letters`: entries `306`, unique `306` (`Assets/WordChef/10000 English Words/3_letters.txt`)
- `4_letters`: entries `631`, unique `631` (`Assets/WordChef/10000 English Words/4_letters.txt`)
- `5_letters`: entries `117`, unique `117` (`Assets/WordChef/10000 English Words/5_letters.txt`)
- `6_letters`: entries `1509`, unique `1509` (`Assets/WordChef/10000 English Words/6_letters.txt`)
- `7_letters`: entries `1466`, unique `1466` (`Assets/WordChef/10000 English Words/7_letters.txt`)

## Key Files

- `Assets/QtCrossword/Editor/CrosswordToolWindow.cs`: Main editor tool for crossword generation, JSON IO, hidden words, debug menu.
- `Assets/QtCrossword/Scripts/CrosswordGenerator.cs`: Crossword variant generation and constrained word picker.
- `Assets/QtCrossword/Scripts/CrosswordModels.cs`: Crossword data models used by generator/editor.
- `Assets/WordChef/_Scripts/Controller/MainController.cs`: Runtime bootstrap and level loading fallback from JSON.
- `Assets/WordChef/_Scripts/Main/WordRegion.cs`: Runtime crossword grid, answer checks, hidden word reward flow.
- `Assets/WordChef/_Scripts/Main/LineDrawer.cs`: Input path drawing, allowed-next logic, gold/shimmer/stars visuals.
- `Assets/WordChef/_Scripts/Main/Pan.cs`: Bottom ring letters built from crossword unique letters.
- `Assets/WordChef/_Scripts/Main/CrosswordConfig.cs`: JSON schema: CrosswordConfigs + HiddenWords.
- `Assets/WordChef/_Scripts/Main/CrosswordLoader.cs`: Resources JSON loading helpers.
- `Assets/WordChef/_Scripts/Utils/CwzConverter.cs`: CWZ to JSON converter utility.
- `Assets/WordChef/_Scripts/Utils/LineDrawerVisualSetup.cs`: Material/texture setup automation for line visuals.
- `Assets/WordChef/_Scripts/Prefs.cs`: Save keys and persisted gameplay progress.

## Screenshots

No screenshots found yet.

Add screenshots to `Docs/Screenshots` with names like:
- `qt_generator_main.png` (Qt Crossword Generator main window)
- `qt_generator_hidden_words.png` (Hidden words strip (+ input, remove))
- `qt_generator_debug_menu.png` (Debug menu with Erase Save)
- `runtime_main_gameplay.png` (Main gameplay scene with board and pan)
- `runtime_gold_line_visuals.png` (Gold line, shimmer, stars visual effect)

After adding images, regenerate README to auto-embed them.

## Regenerate This README

```bash
python Utils/WorldChef/generate_project_readme.py
```

Optional flags:

- `--project-root <path>`
- `--output <path-to-readme>`
- `--screenshots-dir <path-to-screenshot-folder>`
- `--stdout`
