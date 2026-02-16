#!/usr/bin/env python3
"""
Enrich *_letters.txt files with popular singular nouns from internet data.

Primary source: Datamuse API (https://api.datamuse.com/words)
"""

from __future__ import annotations

import argparse
import json
import logging
import re
import string
import time
from pathlib import Path
from typing import Iterable
from urllib.error import HTTPError, URLError
from urllib.parse import urlencode
from urllib.request import Request, urlopen

try:
    import inflect
except ImportError:  # pragma: no cover - optional dependency
    inflect = None


SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent.parent
DEFAULT_TARGET = PROJECT_ROOT / "Assets/WordChef/10000 English Words/3_letters.txt"
CACHE_DIR = SCRIPT_DIR / ".cache"
NOUN_CACHE_PATH = CACHE_DIR / "noun_cache.json"
LOGS_DIR = SCRIPT_DIR / "logs"
DEFAULT_LOG_PATH = LOGS_DIR / "enrich_word_lists.log"

DATAMUSE_URL = "https://api.datamuse.com/words"
LENGTH_FILE_RE = re.compile(r"^(?P<length>\d+)_letters\.txt$", re.IGNORECASE)
SUPPORTED_LENGTHS = (3, 4, 5, 6, 7)
# High-frequency function words that should not appear in noun-only lists.
BLOCKED_WORDS = {
    "a",
    "an",
    "and",
    "are",
    "as",
    "at",
    "be",
    "been",
    "being",
    "but",
    "by",
    "can",
    "did",
    "do",
    "does",
    "for",
    "from",
    "had",
    "has",
    "have",
    "he",
    "her",
    "hers",
    "him",
    "his",
    "how",
    "i",
    "if",
    "in",
    "into",
    "is",
    "it",
    "its",
    "me",
    "my",
    "nor",
    "not",
    "of",
    "on",
    "one",
    "or",
    "our",
    "ours",
    "out",
    "she",
    "so",
    "than",
    "that",
    "the",
    "their",
    "theirs",
    "them",
    "there",
    "these",
    "they",
    "this",
    "those",
    "to",
    "too",
    "two",
    "up",
    "us",
    "was",
    "we",
    "were",
    "what",
    "when",
    "where",
    "which",
    "who",
    "why",
    "will",
    "with",
    "you",
    "your",
    "yours",
    "ser",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Update *_letters.txt with missing popular singular nouns that have "
            "all unique letters."
        )
    )
    parser.add_argument(
        "target",
        nargs="?",
        default=str(DEFAULT_TARGET),
        help="Path to one *_letters.txt file. Supported lengths: 3,4,5,6,7.",
    )
    parser.add_argument(
        "--all-files",
        action="store_true",
        help="Process 3_letters.txt ... 7_letters.txt in target directory.",
    )
    parser.add_argument(
        "--limit",
        type=int,
        default=120,
        help="Maximum number of words to add per file.",
    )
    parser.add_argument(
        "--max-per-query",
        type=int,
        default=1000,
        help="Maximum Datamuse results per request.",
    )
    parser.add_argument(
        "--min-score",
        type=float,
        default=300.0,
        help="Minimum Datamuse score for candidate words.",
    )
    parser.add_argument(
        "--min-freq",
        type=float,
        default=1.5,
        help="Minimum Datamuse frequency tag (f:*) for candidate words.",
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=20.0,
        help="HTTP timeout in seconds.",
    )
    parser.add_argument(
        "--retries",
        type=int,
        default=3,
        help="Retry count per Datamuse request.",
    )
    parser.add_argument(
        "--delay",
        type=float,
        default=0.08,
        help="Delay between requests in seconds.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Show candidates but do not modify files.",
    )
    parser.add_argument(
        "--append-only",
        action="store_true",
        help="Only append new words and keep existing lines unchanged.",
    )
    parser.add_argument(
        "--strict-noun",
        action="store_true",
        help=(
            "Use strict noun filtering: allow only words whose exact POS tags "
            "are noun-only (exclude noun+verb, noun+adjective, etc.)."
        ),
    )
    parser.add_argument(
        "--log-level",
        default="INFO",
        choices=["DEBUG", "INFO", "WARNING", "ERROR"],
        help="Console log verbosity level.",
    )
    parser.add_argument(
        "--log-file",
        default=str(DEFAULT_LOG_PATH),
        help="Path to a log file. Relative paths are resolved from current directory.",
    )
    parser.add_argument(
        "--no-log-file",
        action="store_true",
        help="Disable file logging and report only to console.",
    )
    return parser.parse_args()


def parse_length_from_filename(path: Path) -> int | None:
    match = LENGTH_FILE_RE.match(path.name)
    if not match:
        return None
    return int(match.group("length"))


def supported_lengths_text() -> str:
    return ", ".join(str(length) for length in SUPPORTED_LENGTHS)


def validate_supported_length(path: Path, length: int | None) -> int:
    if length is None:
        raise ValueError(
            f"Cannot detect word length from file name: {path.name}. "
            f"Expected format like 3_letters.txt."
        )
    if length not in SUPPORTED_LENGTHS:
        raise ValueError(
            f"Unsupported word length {length} in {path.name}. "
            f"Supported lengths: {supported_lengths_text()}."
        )
    return length


def resolve_targets(target: Path, all_files: bool) -> tuple[list[Path], list[int]]:
    if all_files:
        files: list[Path] = []
        missing_lengths: list[int] = []
        for length in SUPPORTED_LENGTHS:
            candidate = target.parent / f"{length}_letters.txt"
            if candidate.is_file():
                files.append(candidate)
            else:
                missing_lengths.append(length)
        return files, missing_lengths
    return [target], []


def resolve_target_path(raw_target: str) -> Path:
    target = Path(raw_target)
    if target.is_absolute():
        return target.resolve()

    from_cwd = (Path.cwd() / target).resolve()
    if from_cwd.exists():
        return from_cwd

    return (PROJECT_ROOT / target).resolve()


def resolve_runtime_path(raw_path: str) -> Path:
    path = Path(raw_path)
    if path.is_absolute():
        return path.resolve()
    return (Path.cwd() / path).resolve()


def setup_logger(args: argparse.Namespace) -> tuple[logging.Logger, Path | None]:
    logger = logging.getLogger("wordchef.enrich")
    logger.setLevel(logging.DEBUG)
    logger.handlers.clear()

    formatter = logging.Formatter(
        fmt="%(asctime)s | %(levelname)s | %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    )

    console_handler = logging.StreamHandler()
    console_handler.setLevel(getattr(logging, args.log_level.upper()))
    console_handler.setFormatter(formatter)
    logger.addHandler(console_handler)

    log_file_path: Path | None = None
    if not args.no_log_file:
        log_file_path = resolve_runtime_path(args.log_file)
        log_file_path.parent.mkdir(parents=True, exist_ok=True)
        file_handler = logging.FileHandler(log_file_path, encoding="utf-8")
        file_handler.setLevel(logging.DEBUG)
        file_handler.setFormatter(formatter)
        logger.addHandler(file_handler)

    logger.propagate = False
    return logger, log_file_path


def read_words(path: Path) -> list[str]:
    words: list[str] = []
    seen: set[str] = set()
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        word = raw_line.strip().lower()
        if not word or word in seen:
            continue
        seen.add(word)
        words.append(word)
    return words


def write_words(path: Path, words: Iterable[str]) -> None:
    output = "\n".join(words).strip() + "\n"
    path.write_text(output, encoding="utf-8")


def load_noun_cache(path: Path) -> dict[str, bool]:
    if not path.exists():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError:
        return {}
    if not isinstance(data, dict):
        return {}
    cache: dict[str, bool] = {}
    for key, value in data.items():
        if isinstance(key, str) and isinstance(value, bool):
            cache[key] = value
    return cache


def save_noun_cache(path: Path, cache: dict[str, bool]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = json.dumps(cache, ensure_ascii=True, indent=2, sort_keys=True)
    path.write_text(payload + "\n", encoding="utf-8")


def fetch_datamuse(
    pattern: str,
    max_items: int,
    timeout: float,
    retries: int,
    delay: float,
    md: str = "pf",
    logger: logging.Logger | None = None,
) -> list[dict]:
    params = urlencode({"sp": pattern, "md": md, "max": str(max_items)})
    url = f"{DATAMUSE_URL}?{params}"
    headers = {"User-Agent": "word-list-enricher/1.0"}
    last_error: Exception | None = None

    for attempt in range(1, retries + 1):
        try:
            if logger is not None and logger.isEnabledFor(logging.DEBUG):
                logger.debug(
                    "Datamuse request: pattern=%s md=%s max=%s attempt=%s/%s",
                    pattern,
                    md,
                    max_items,
                    attempt,
                    retries,
                )
            request = Request(url=url, headers=headers)
            with urlopen(request, timeout=timeout) as response:
                payload = response.read().decode("utf-8")
            data = json.loads(payload)
            if delay > 0:
                time.sleep(delay)
            if logger is not None and logger.isEnabledFor(logging.DEBUG):
                logger.debug(
                    "Datamuse response: pattern=%s md=%s rows=%s",
                    pattern,
                    md,
                    len(data) if isinstance(data, list) else 0,
                )
            return data if isinstance(data, list) else []
        except (HTTPError, URLError, TimeoutError, json.JSONDecodeError) as exc:
            last_error = exc
            if logger is not None:
                logger.warning(
                    "Datamuse attempt failed: pattern=%s md=%s attempt=%s/%s error=%s",
                    pattern,
                    md,
                    attempt,
                    retries,
                    exc,
                )
            if attempt < retries:
                time.sleep(max(0.2, delay * attempt))

    raise RuntimeError(f"Datamuse request failed: {url}\nReason: {last_error}")


def build_patterns(length: int) -> list[str]:
    wildcard = "?" * length
    per_letter = [f"{first}{'?' * (length - 1)}" for first in string.ascii_lowercase]
    return [wildcard, *per_letter]


def has_unique_letters(word: str) -> bool:
    return len(word) == len(set(word))


def is_clean_word(word: str, length: int) -> bool:
    return (
        len(word) == length
        and word.isascii()
        and word.isalpha()
        and word.islower()
    )


def has_noun_tag(tags: list[str], strict_noun: bool = False) -> bool:
    if "n" not in tags or "prop" in tags:
        return False
    pos_order = [tag for tag in tags if tag in {"n", "v", "adj", "adv", "u"}]
    if not pos_order:
        return False
    if strict_noun:
        return all(tag == "n" for tag in pos_order)
    return pos_order[0] == "n"


def is_blocked_word(word: str) -> bool:
    return word in BLOCKED_WORDS


def noun_cache_key(word: str, strict_noun: bool) -> str:
    return f"{word}|strict={1 if strict_noun else 0}"


def is_noun_exact(
    word: str,
    noun_cache: dict[str, bool],
    timeout: float,
    retries: int,
    delay: float,
    logger: logging.Logger,
    strict_noun: bool = False,
) -> bool:
    cache_key = noun_cache_key(word, strict_noun)

    if is_blocked_word(word):
        noun_cache[cache_key] = False
        return False

    cached = noun_cache.get(cache_key)
    if cached is None and not strict_noun:
        # Backward compatibility with old cache format: {"word": bool}.
        legacy_cached = noun_cache.get(word)
        if isinstance(legacy_cached, bool):
            noun_cache[cache_key] = legacy_cached
            return legacy_cached

    if cached is not None:
        return cached

    rows = fetch_datamuse(
        pattern=word,
        max_items=20,
        timeout=timeout,
        retries=retries,
        delay=delay,
        md="p",
        logger=logger,
    )

    is_noun = False
    for item in rows:
        row_word = str(item.get("word", "")).lower().strip()
        if row_word != word:
            continue
        tags = item.get("tags", [])
        if isinstance(tags, list) and has_noun_tag(tags, strict_noun=strict_noun):
            is_noun = True
            break

    noun_cache[cache_key] = is_noun
    return is_noun


def rank_from_item(item: dict) -> float:
    score = parse_score(item)
    best_freq = extract_best_freq(item.get("tags", []))
    return score + best_freq * 1000.0


def parse_score(item: dict) -> float:
    raw_score = item.get("score", 0.0)
    try:
        return float(raw_score)
    except (TypeError, ValueError):
        return 0.0


def extract_best_freq(tags: object) -> float:
    if not isinstance(tags, list):
        return 0.0

    best_freq = 0.0
    for tag in tags:
        if not isinstance(tag, str) or not tag.startswith("f:"):
            continue
        try:
            best_freq = max(best_freq, float(tag[2:]))
        except ValueError:
            continue
    return best_freq


def is_singular(word: str, inflect_engine) -> bool:
    if inflect_engine is None:
        # Fallback heuristic when inflect is unavailable.
        if word.endswith("s") and not word.endswith(("ss", "us", "is")):
            return False
        return True

    singular = inflect_engine.singular_noun(word)
    if singular is False:
        return True
    return singular == word


def collect_candidates(
    length: int,
    max_per_query: int,
    min_score: float,
    min_freq: float,
    timeout: float,
    retries: int,
    delay: float,
    logger: logging.Logger,
    strict_noun: bool = False,
) -> tuple[list[tuple[str, float]], dict[str, int]]:
    ranked: dict[str, float] = {}
    scanned_total = 0
    skipped_low_score = 0
    skipped_low_freq = 0

    for pattern in build_patterns(length):
        rows = fetch_datamuse(
            pattern=pattern,
            max_items=max_per_query,
            timeout=timeout,
            retries=retries,
            delay=delay,
            logger=logger,
        )
        for item in rows:
            scanned_total += 1
            word = str(item.get("word", "")).lower().strip()
            tags = item.get("tags", [])
            if not isinstance(tags, list):
                continue
            if not has_noun_tag(tags, strict_noun=strict_noun):
                continue
            if not is_clean_word(word, length):
                continue
            if is_blocked_word(word):
                continue
            score = parse_score(item)
            if score < min_score:
                skipped_low_score += 1
                continue
            best_freq = extract_best_freq(tags)
            if best_freq < min_freq:
                skipped_low_freq += 1
                continue

            rank = rank_from_item(item)
            previous = ranked.get(word)
            if previous is None or rank > previous:
                ranked[word] = rank

    stats = {
        "scanned_total": scanned_total,
        "kept_unique": len(ranked),
        "skipped_low_score": skipped_low_score,
        "skipped_low_freq": skipped_low_freq,
    }
    return sorted(ranked.items(), key=lambda pair: pair[1], reverse=True), stats


def process_file(
    path: Path,
    args: argparse.Namespace,
    inflect_engine,
    noun_cache: dict[str, bool],
    logger: logging.Logger,
) -> tuple[int, int]:
    length = validate_supported_length(path, parse_length_from_filename(path))

    existing_words = read_words(path)
    cleanup_invalid = 0
    cleanup_non_unique = 0
    cleanup_non_singular = 0
    cleanup_non_noun = 0

    if args.append_only:
        base_words = list(existing_words)
        removed = 0
    else:
        base_words = []
        for word in existing_words:
            if not is_clean_word(word, length):
                cleanup_invalid += 1
                continue
            if not has_unique_letters(word):
                cleanup_non_unique += 1
                continue
            if not is_singular(word, inflect_engine):
                cleanup_non_singular += 1
                continue
            if not is_noun_exact(
                word=word,
                noun_cache=noun_cache,
                timeout=args.timeout,
                retries=args.retries,
                delay=args.delay,
                logger=logger,
                strict_noun=args.strict_noun,
            ):
                cleanup_non_noun += 1
                continue
            base_words.append(word)
        removed = len(existing_words) - len(base_words)

    existing_set = set(base_words)
    ranked_candidates, candidate_stats = collect_candidates(
        length=length,
        max_per_query=args.max_per_query,
        min_score=args.min_score,
        min_freq=args.min_freq,
        timeout=args.timeout,
        retries=args.retries,
        delay=args.delay,
        logger=logger,
        strict_noun=args.strict_noun,
    )

    additions: list[str] = []
    skip_existing = 0
    skip_blocked = 0
    skip_non_unique = 0
    skip_non_singular = 0
    skip_non_noun = 0

    for word, _rank in ranked_candidates:
        if word in existing_set:
            skip_existing += 1
            continue
        if is_blocked_word(word):
            skip_blocked += 1
            continue
        if not has_unique_letters(word):
            skip_non_unique += 1
            continue
        if not is_singular(word, inflect_engine):
            skip_non_singular += 1
            continue
        if not is_noun_exact(
            word=word,
            noun_cache=noun_cache,
            timeout=args.timeout,
            retries=args.retries,
            delay=args.delay,
            logger=logger,
            strict_noun=args.strict_noun,
        ):
            skip_non_noun += 1
            continue
        additions.append(word)
        if len(additions) >= args.limit:
            break

    wrote_file = False
    if not args.dry_run and (additions or removed):
        write_words(path, [*base_words, *additions])
        wrote_file = True

    preview = ", ".join(additions[:12]) if additions else "-"
    mode = "dry-run" if args.dry_run else "updated"
    logger.info(
        "[%s] %s | existing=%s kept=%s removed=%s candidates=%s added=%s wrote=%s",
        mode,
        path.name,
        len(existing_words),
        len(base_words),
        removed,
        candidate_stats["kept_unique"],
        len(additions),
        wrote_file,
    )
    logger.info(
        "%s preview additions: %s",
        path.name,
        preview,
    )
    logger.debug(
        "%s cleanup stats | invalid=%s non_unique=%s non_singular=%s non_noun=%s",
        path.name,
        cleanup_invalid,
        cleanup_non_unique,
        cleanup_non_singular,
        cleanup_non_noun,
    )
    logger.debug(
        "%s candidate skip stats | scanned=%s kept_unique=%s low_score=%s "
        "low_freq=%s existing=%s blocked=%s non_unique=%s non_singular=%s "
        "non_noun=%s",
        path.name,
        candidate_stats["scanned_total"],
        candidate_stats["kept_unique"],
        candidate_stats["skipped_low_score"],
        candidate_stats["skipped_low_freq"],
        skip_existing,
        skip_blocked,
        skip_non_unique,
        skip_non_singular,
        skip_non_noun,
    )
    return len(additions), removed


def main() -> None:
    args = parse_args()
    logger, log_file_path = setup_logger(args)

    logger.info("------------------------------------------------------------")
    logger.info("Word list enrich run started")
    logger.info(
        "Options: dry_run=%s append_only=%s strict_noun=%s all_files=%s "
        "limit=%s max_per_query=%s min_score=%s min_freq=%s",
        args.dry_run,
        args.append_only,
        args.strict_noun,
        args.all_files,
        args.limit,
        args.max_per_query,
        args.min_score,
        args.min_freq,
    )
    if log_file_path is not None:
        logger.info("Log file: %s", log_file_path)

    try:
        target = resolve_target_path(args.target)
        if not target.exists():
            raise FileNotFoundError(f"Target file not found: {target}")

        target_length = parse_length_from_filename(target)
        if not args.all_files:
            validate_supported_length(target, target_length)

        targets, missing_lengths = resolve_targets(target=target, all_files=args.all_files)
        if not targets:
            raise FileNotFoundError(
                f"No supported *_letters.txt files found in: {target.parent}. "
                f"Expected lengths: {supported_lengths_text()}."
            )

        if missing_lengths:
            logger.warning(
                "Missing supported files for lengths: %s",
                ", ".join(str(length) for length in missing_lengths),
            )

        logger.info("Resolved target files: %s", ", ".join(str(path) for path in targets))

        inflect_engine = inflect.engine() if inflect is not None else None
        if inflect_engine is None:
            logger.warning(
                "Package 'inflect' is not installed. "
                "Singular filtering uses a fallback heuristic."
            )

        noun_cache = load_noun_cache(NOUN_CACHE_PATH)
        cache_before = len(noun_cache)
        logger.info("Loaded noun cache entries: %s", cache_before)

        total_added = 0
        total_removed = 0
        for file_path in targets:
            logger.info("Processing file: %s", file_path)
            added, removed = process_file(
                file_path,
                args,
                inflect_engine,
                noun_cache,
                logger,
            )
            total_added += added
            total_removed += removed

        if not args.dry_run and len(noun_cache) != cache_before:
            save_noun_cache(NOUN_CACHE_PATH, noun_cache)
            logger.info(
                "Saved noun cache: %s -> %s entries",
                cache_before,
                len(noun_cache),
            )
        else:
            logger.info("Noun cache unchanged: %s entries", len(noun_cache))

        logger.info(
            "Done. Total added words: %s | total removed words: %s | noun cache size: %s",
            total_added,
            total_removed,
            len(noun_cache),
        )
    except Exception:
        logger.exception("Run failed")
        raise


if __name__ == "__main__":
    main()
