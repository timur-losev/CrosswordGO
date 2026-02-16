#!/usr/bin/env python3
"""
Generate a project-level README.md for the Cookies / CrosswordGO repo.

The generator collects:
- Unity and package metadata
- Scene and editor menu inventory
- Word list stats
- Sample level JSON stats
- Key architecture files and feature flags
- Screenshot embeds from Docs/Screenshots
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
from pathlib import Path
from typing import Dict, Iterable, List, Sequence, Tuple


MENU_ITEM_RE = re.compile(r'\[MenuItem\("([^"]+)"\)\]')
TOKEN_SPLIT_RE = re.compile(r"[^A-Za-z0-9]+")


KEY_FILES: Sequence[Tuple[str, str]] = (
    ("Assets/QtCrossword/Editor/CrosswordToolWindow.cs", "Main editor tool for crossword generation, JSON IO, hidden words, debug menu."),
    ("Assets/QtCrossword/Scripts/CrosswordGenerator.cs", "Crossword variant generation and constrained word picker."),
    ("Assets/QtCrossword/Scripts/CrosswordModels.cs", "Crossword data models used by generator/editor."),
    ("Assets/WordChef/_Scripts/Controller/MainController.cs", "Runtime bootstrap and level loading fallback from JSON."),
    ("Assets/WordChef/_Scripts/Main/WordRegion.cs", "Runtime crossword grid, answer checks, hidden word reward flow."),
    ("Assets/WordChef/_Scripts/Main/LineDrawer.cs", "Input path drawing, allowed-next logic, gold/shimmer/stars visuals."),
    ("Assets/WordChef/_Scripts/Main/Pan.cs", "Bottom ring letters built from crossword unique letters."),
    ("Assets/WordChef/_Scripts/Main/CrosswordConfig.cs", "JSON schema: CrosswordConfigs + HiddenWords."),
    ("Assets/WordChef/_Scripts/Main/CrosswordLoader.cs", "Resources JSON loading helpers."),
    ("Assets/WordChef/_Scripts/Utils/CwzConverter.cs", "CWZ to JSON converter utility."),
    ("Assets/WordChef/_Scripts/Utils/LineDrawerVisualSetup.cs", "Material/texture setup automation for line visuals."),
    ("Assets/WordChef/_Scripts/Prefs.cs", "Save keys and persisted gameplay progress."),
)


EXPECTED_SCREENSHOTS: Sequence[Tuple[str, str]] = (
    ("qt_generator_main.png", "Qt Crossword Generator main window"),
    ("qt_generator_hidden_words.png", "Hidden words strip (+ input, remove)"),
    ("qt_generator_debug_menu.png", "Debug menu with Erase Save"),
    ("runtime_main_gameplay.png", "Main gameplay scene with board and pan"),
    ("runtime_gold_line_visuals.png", "Gold line, shimmer, stars visual effect"),
)


def read_text(path: Path) -> str:
    if not path.exists():
        return ""
    return path.read_text(encoding="utf-8", errors="ignore")


def parse_unity_version(project_root: Path) -> str:
    text = read_text(project_root / "ProjectSettings/ProjectVersion.txt")
    for line in text.splitlines():
        line = line.strip()
        if line.startswith("m_EditorVersion:"):
            return line.split(":", 1)[1].strip()
    return "Unknown"


def parse_packages(project_root: Path) -> List[Tuple[str, str]]:
    manifest_path = project_root / "Packages/manifest.json"
    if not manifest_path.exists():
        return []

    try:
        manifest = json.loads(read_text(manifest_path))
    except json.JSONDecodeError:
        return []

    deps = manifest.get("dependencies", {})
    result: List[Tuple[str, str]] = []
    for name, version in deps.items():
        if name.startswith("com.unity.modules."):
            continue
        result.append((str(name), str(version)))
    return sorted(result, key=lambda x: x[0].lower())


def collect_scenes(project_root: Path) -> List[str]:
    scenes = sorted(project_root.glob("Assets/**/*.unity"))
    return [to_rel(project_root, scene) for scene in scenes]


def collect_menu_items(project_root: Path) -> List[Tuple[str, List[str]]]:
    menu_to_files: Dict[str, List[str]] = {}
    for cs_path in project_root.glob("Assets/**/*.cs"):
        text = read_text(cs_path)
        matches = MENU_ITEM_RE.findall(text)
        if not matches:
            continue
        rel = to_rel(project_root, cs_path)
        for menu in matches:
            if menu.startswith("Tools/") or menu.startswith("Superpow/"):
                menu_to_files.setdefault(menu, []).append(rel)

    rows: List[Tuple[str, List[str]]] = []
    for menu, files in menu_to_files.items():
        rows.append((menu, sorted(set(files))))
    rows.sort(key=lambda x: x[0].lower())
    return rows


def tokenize_words(raw_text: str) -> List[str]:
    tokens: List[str] = []
    for part in TOKEN_SPLIT_RE.split(raw_text):
        token = part.strip().upper()
        if token:
            tokens.append(token)
    return tokens


def collect_word_list_stats(project_root: Path) -> List[Tuple[int, int, int, str]]:
    stats: List[Tuple[int, int, int, str]] = []
    folder = project_root / "Assets/WordChef/10000 English Words"
    for length in (3, 4, 5, 6, 7):
        path = folder / f"{length}_letters.txt"
        if not path.exists():
            stats.append((length, 0, 0, to_rel(project_root, path)))
            continue

        tokens = tokenize_words(read_text(path))
        filtered = [w for w in tokens if len(w) == length]
        unique = sorted(set(filtered))
        stats.append((length, len(filtered), len(unique), to_rel(project_root, path)))
    return stats


def parse_sample_level(project_root: Path) -> Dict[str, object]:
    sample_path = project_root / "Assets/WordChef/Resources/World_0/SubWorld_0/LevelCW_0.json"
    result: Dict[str, object] = {
        "path": to_rel(project_root, sample_path),
        "exists": sample_path.exists(),
        "crossword_count": 0,
        "hidden_count": 0,
        "unique_letters": 0,
        "hidden_preview": [],
    }
    if not sample_path.exists():
        return result

    try:
        data = json.loads(read_text(sample_path))
    except json.JSONDecodeError:
        return result

    configs = data.get("CrosswordConfigs") or []
    hidden = data.get("HiddenWords") or []
    answers = [str(x.get("Answer", "")).upper() for x in configs if isinstance(x, dict)]
    letters = set("".join(answers))
    letters = {ch for ch in letters if "A" <= ch <= "Z"}

    result["crossword_count"] = len(configs)
    result["hidden_count"] = len(hidden)
    result["unique_letters"] = len(letters)
    result["hidden_preview"] = [str(x) for x in hidden[:10]]
    return result


def feature_flags(project_root: Path) -> Dict[str, bool]:
    tool = read_text(project_root / "Assets/QtCrossword/Editor/CrosswordToolWindow.cs")
    word_region = read_text(project_root / "Assets/WordChef/_Scripts/Main/WordRegion.cs")
    schema = read_text(project_root / "Assets/WordChef/_Scripts/Main/CrosswordConfig.cs")
    return {
        "hidden_words_schema": "HiddenWords" in schema,
        "manual_hidden_input": "hiddenWordInput" in tool and "TryAddManualHiddenWord" in tool,
        "hidden_word_timeout": "HiddenWordSearchTimeoutSeconds" in tool,
        "debug_erase_save": "Erase Save" in tool and "OnEraseSaveDebugClicked" in tool,
        "reverse_hidden_match": "ResolveHiddenWord" in word_region,
    }


def collect_screenshots(project_root: Path, screenshots_dir: Path) -> List[str]:
    if not screenshots_dir.exists():
        return []
    exts = {".png", ".jpg", ".jpeg", ".webp", ".gif"}
    files = [
        path for path in screenshots_dir.rglob("*")
        if path.is_file() and path.suffix.lower() in exts
    ]
    files.sort(key=lambda p: p.name.lower())
    return [to_rel(project_root, path) for path in files]


def to_rel(project_root: Path, path: Path) -> str:
    try:
        return path.relative_to(project_root).as_posix()
    except ValueError:
        return path.as_posix()


def build_readme(
    project_root: Path,
    unity_version: str,
    packages: Sequence[Tuple[str, str]],
    scenes: Sequence[str],
    menu_items: Sequence[Tuple[str, List[str]]],
    word_stats: Sequence[Tuple[int, int, int, str]],
    level_info: Dict[str, object],
    flags: Dict[str, bool],
    screenshots: Sequence[str],
) -> str:
    now = dt.datetime.now().strftime("%Y-%m-%d %H:%M")
    lines: List[str] = []

    lines.append("# Cookies / CrosswordGO")
    lines.append("")
    lines.append(f"Auto-generated project documentation. Last update: `{now}`.")
    lines.append("")
    lines.append("## Overview")
    lines.append("")
    lines.append("- Mobile crossword game runtime in `Assets/WordChef`, plus an editor-side crossword generator in `Assets/QtCrossword`.")
    lines.append("- Runtime supports JSON-driven crossword layouts (`CrosswordConfigs`) and optional bonus words (`HiddenWords`).")
    lines.append("- The Qt generator can build variants, apply constraints, manage hidden words, and save level JSON files.")
    lines.append("- Additional editor utilities convert CWZ files and configure gold line visual assets.")
    lines.append("")
    lines.append("## Environment")
    lines.append("")
    lines.append(f"- Unity editor: `{unity_version}`")
    lines.append(f"- Key scenes: `{', '.join(scenes)}`")
    lines.append("")
    lines.append("### Package Snapshot")
    lines.append("")
    if packages:
        for name, version in packages:
            lines.append(f"- `{name}`: `{version}`")
    else:
        lines.append("- No package data found.")
    lines.append("")
    lines.append("## Repository Layout")
    lines.append("")
    lines.append("- `Assets/WordChef`: game runtime, scenes, prefabs, resources, word lists.")
    lines.append("- `Assets/QtCrossword`: crossword generation models + editor window.")
    lines.append("- `LevelsOrg`: source CWZ levels (input for converter).")
    lines.append("- `Utils/WorldChef`: utility scripts (including this README generator).")
    lines.append("- `Docs/Screenshots`: optional screenshots auto-embedded into this README.")
    lines.append("")
    lines.append("## Runtime Flow")
    lines.append("")
    lines.append("1. `MainController` loads `GameLevel` and falls back to JSON crossword configs if needed.")
    lines.append("2. `Pan` builds center letters from unique letters in current crossword JSON.")
    lines.append("3. `WordRegion` builds a shared-cell crossword grid from virtual coordinates.")
    lines.append("4. `LineDrawer` handles drag input, previews text, and evaluates submitted words.")
    lines.append("5. Valid answers reveal cells; hidden words grant reward points and persist collected state.")
    lines.append("")
    lines.append("## Level Data Format")
    lines.append("")
    lines.append("`CrosswordData` schema (`Assets/WordChef/_Scripts/Main/CrosswordConfig.cs`):")
    lines.append("")
    lines.append("```json")
    lines.append("{")
    lines.append('  "CrosswordConfigs": [')
    lines.append('    { "Answer": "star", "XPos": 2, "YPos": 0, "Direction": 1 }')
    lines.append("  ],")
    lines.append('  "HiddenWords": ["taser", "rate", "seat"]')
    lines.append("}")
    lines.append("```")
    lines.append("")
    if level_info.get("exists"):
        preview = ", ".join(level_info.get("hidden_preview", []))
        lines.append(f"- Sample level: `{level_info.get('path')}`")
        lines.append(f"- Crossword entries: `{level_info.get('crossword_count')}`")
        lines.append(f"- Hidden words: `{level_info.get('hidden_count')}`")
        lines.append(f"- Unique crossword letters: `{level_info.get('unique_letters')}`")
        lines.append(f"- Hidden preview: `{preview}`")
    else:
        lines.append(f"- Sample level not found: `{level_info.get('path')}`")
    lines.append("")
    lines.append("## Qt Crossword Generator")
    lines.append("")
    lines.append("Main window: `Tools/Qt Crossword Generator`.")
    lines.append("")
    lines.append("- Input modes: manual words, constrained generation, input-only override.")
    lines.append("- Variant operations: generate, navigate, rotate, save/load JSON.")
    lines.append("- Hidden words: auto-add from unique letters, manual add via input + `+`, remove via per-word `-`.")
    lines.append("- Hidden-word auto-search timeout and resume cursor prevent UI hangs.")
    lines.append("- Debug dropdown currently includes `Erase Save` (clears save data and caches).")
    lines.append("")
    lines.append("### Feature Flags Detected")
    lines.append("")
    for key, enabled in sorted(flags.items(), key=lambda x: x[0]):
        lines.append(f"- `{key}`: `{'ON' if enabled else 'OFF'}`")
    lines.append("")
    lines.append("## Editor Menus")
    lines.append("")
    if menu_items:
        for menu, files in menu_items:
            owner = ", ".join(f"`{f}`" for f in files)
            lines.append(f"- `{menu}` -> {owner}")
    else:
        lines.append("- No `Tools/` or `Superpow/` menu items found.")
    lines.append("")
    lines.append("## Word List Stats (English)")
    lines.append("")
    for length, entries, unique, rel in word_stats:
        lines.append(
            f"- `{length}_letters`: entries `{entries}`, unique `{unique}` (`{rel}`)"
        )
    lines.append("")
    lines.append("## Key Files")
    lines.append("")
    for rel, desc in KEY_FILES:
        full = project_root / rel
        if full.exists():
            lines.append(f"- `{rel}`: {desc}")
    lines.append("")
    lines.append("## Screenshots")
    lines.append("")
    if screenshots:
        for rel in screenshots:
            title = Path(rel).stem.replace("_", " ").title()
            lines.append(f"### {title}")
            lines.append("")
            lines.append(f"![{title}]({rel})")
            lines.append("")
    else:
        lines.append("No screenshots found yet.")
        lines.append("")
        lines.append("Add screenshots to `Docs/Screenshots` with names like:")
        for file_name, caption in EXPECTED_SCREENSHOTS:
            lines.append(f"- `{file_name}` ({caption})")
        lines.append("")
        lines.append("After adding images, regenerate README to auto-embed them.")
        lines.append("")
    lines.append("## Regenerate This README")
    lines.append("")
    lines.append("```bash")
    lines.append("python Utils/WorldChef/generate_project_readme.py")
    lines.append("```")
    lines.append("")
    lines.append("Optional flags:")
    lines.append("")
    lines.append("- `--project-root <path>`")
    lines.append("- `--output <path-to-readme>`")
    lines.append("- `--screenshots-dir <path-to-screenshot-folder>`")
    lines.append("- `--stdout`")
    lines.append("")

    return "\n".join(lines).rstrip() + "\n"


def parse_args() -> argparse.Namespace:
    default_root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Generate project README.md")
    parser.add_argument(
        "--project-root",
        default=str(default_root),
        help="Repository root path (default: inferred from script path).",
    )
    parser.add_argument(
        "--output",
        default="README.md",
        help="Output README path (absolute or relative to project root).",
    )
    parser.add_argument(
        "--screenshots-dir",
        default="Docs/Screenshots",
        help="Screenshot directory (absolute or relative to project root).",
    )
    parser.add_argument(
        "--stdout",
        action="store_true",
        help="Print generated markdown to stdout instead of writing file.",
    )
    return parser.parse_args()


def resolve_path(base: Path, value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else (base / path)


def main() -> int:
    args = parse_args()
    project_root = Path(args.project_root).resolve()
    output_path = resolve_path(project_root, args.output).resolve()
    screenshots_dir = resolve_path(project_root, args.screenshots_dir).resolve()
    screenshots_dir.mkdir(parents=True, exist_ok=True)

    unity_version = parse_unity_version(project_root)
    packages = parse_packages(project_root)
    scenes = collect_scenes(project_root)
    menus = collect_menu_items(project_root)
    word_stats = collect_word_list_stats(project_root)
    level_info = parse_sample_level(project_root)
    flags = feature_flags(project_root)
    screenshots = collect_screenshots(project_root, screenshots_dir)

    markdown = build_readme(
        project_root=project_root,
        unity_version=unity_version,
        packages=packages,
        scenes=scenes,
        menu_items=menus,
        word_stats=word_stats,
        level_info=level_info,
        flags=flags,
        screenshots=screenshots,
    )

    if args.stdout:
        print(markdown)
        return 0

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(markdown, encoding="utf-8")
    print(f"README generated: {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
