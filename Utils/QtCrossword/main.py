from __future__ import annotations

import sys
from dataclasses import dataclass
from typing import Dict, List, Tuple

from PySide6.QtCore import Qt
from PySide6.QtGui import QColor, QFont
from PySide6.QtWidgets import (
    QAbstractItemView,
    QApplication,
    QHBoxLayout,
    QLabel,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QTableWidget,
    QTableWidgetItem,
    QTextEdit,
    QVBoxLayout,
    QWidget,
)

Grid = Dict[Tuple[int, int], str]


@dataclass(frozen=True)
class Placement:
    word: str
    row: int
    col: int
    horizontal: bool


@dataclass
class CrosswordResult:
    grid: Grid
    placements: List[Placement]
    used_words: List[str]
    skipped_words: List[str]
    min_row: int
    max_row: int
    min_col: int
    max_col: int
    intersections: int


@dataclass
class CrosswordVariants:
    variants: List[CrosswordResult]
    truncated: bool


def parse_words(raw_text: str) -> List[str]:
    tokens: List[str] = []
    current: List[str] = []

    for char in raw_text:
        if char.isalnum():
            current.append(char)
            continue
        if current:
            tokens.append("".join(current))
            current = []

    if current:
        tokens.append("".join(current))

    words: List[str] = []
    seen = set()
    for token in tokens:
        word = token.strip().upper()
        if len(word) < 2:
            continue
        if word in seen:
            continue
        seen.add(word)
        words.append(word)

    return words


class CrosswordGenerator:
    def __init__(self, max_variants: int = 2000, max_nodes: int = 220000) -> None:
        self.max_variants = max_variants
        self.max_nodes = max_nodes

    def generate(self, words: List[str]) -> CrosswordResult | None:
        variants = self.generate_variants(words)
        if not variants.variants:
            return None
        return variants.variants[0]

    def generate_variants(self, words: List[str]) -> CrosswordVariants:
        if not words:
            return CrosswordVariants(variants=[], truncated=False)

        ordered_words = sorted(words, key=lambda value: (-len(value), value))
        best_variants_by_signature: Dict[str, CrosswordResult] = {}
        best_used_count = 0
        visited_nodes = 0
        truncated = False
        stop_search = False

        def record_result(
            grid: Grid,
            usage: Dict[Tuple[int, int], int],
            placements: List[Placement],
            used_words: List[str],
        ) -> None:
            nonlocal best_used_count, truncated, stop_search
            used_count = len(used_words)
            if used_count == 0:
                return
            if used_count < best_used_count:
                return

            signature = self._grid_signature(grid)
            if used_count > best_used_count:
                best_used_count = used_count
                best_variants_by_signature.clear()
                truncated = False

            if signature in best_variants_by_signature:
                return

            if (
                used_count == best_used_count
                and len(best_variants_by_signature) >= self.max_variants
            ):
                truncated = True
                if best_used_count == len(words):
                    stop_search = True
                return

            min_row, max_row, min_col, max_col = self._bounds(grid)
            intersections = sum(1 for value in usage.values() if value > 1)
            used_word_set = set(used_words)
            skipped_words = [word for word in words if word not in used_word_set]

            best_variants_by_signature[signature] = CrosswordResult(
                grid=dict(grid),
                placements=list(placements),
                used_words=list(used_words),
                skipped_words=skipped_words,
                min_row=min_row,
                max_row=max_row,
                min_col=min_col,
                max_col=max_col,
                intersections=intersections,
            )

        def search_from_order(
            order: List[str],
            word_index: int,
            grid: Grid,
            usage: Dict[Tuple[int, int], int],
            placements: List[Placement],
            used_words: List[str],
        ) -> None:
            nonlocal visited_nodes, truncated, stop_search
            if stop_search:
                return

            visited_nodes += 1
            if visited_nodes > self.max_nodes:
                truncated = True
                stop_search = True
                return

            remaining_words = len(order) - word_index
            if len(used_words) + remaining_words < best_used_count:
                return

            if word_index >= len(order):
                record_result(grid, usage, placements, used_words)
                return

            word = order[word_index]
            candidates = self._find_candidates(word=word, grid=grid)

            for row, col, horizontal in candidates:
                if stop_search:
                    return

                new_grid = dict(grid)
                new_usage = dict(usage)
                self._place_word(
                    new_grid,
                    new_usage,
                    word=word,
                    row=row,
                    col=col,
                    horizontal=horizontal,
                )
                search_from_order(
                    order=order,
                    word_index=word_index + 1,
                    grid=new_grid,
                    usage=new_usage,
                    placements=[*placements, Placement(word, row, col, horizontal)],
                    used_words=[*used_words, word],
                )

            # This branch keeps words optional and allows more compact alternatives.
            search_from_order(
                order=order,
                word_index=word_index + 1,
                grid=grid,
                usage=usage,
                placements=placements,
                used_words=used_words,
            )

        for seed_word in ordered_words:
            if stop_search:
                break

            grid: Grid = {}
            usage: Dict[Tuple[int, int], int] = {}
            self._place_word(grid, usage, seed_word, row=0, col=0, horizontal=True)

            rest = [word for word in ordered_words if word != seed_word]
            search_from_order(
                order=rest,
                word_index=0,
                grid=grid,
                usage=usage,
                placements=[Placement(seed_word, row=0, col=0, horizontal=True)],
                used_words=[seed_word],
            )

        variants = list(best_variants_by_signature.values())
        variants.sort(
            key=lambda result: (
                -self._layout_score(result),
                self._grid_signature(result.grid),
            )
        )
        return CrosswordVariants(variants=variants, truncated=truncated)

    def _find_candidates(self, word: str, grid: Grid) -> List[Tuple[int, int, bool]]:
        candidates: List[Tuple[int, int, int, bool]] = []
        seen_starts = set()

        for letter_index, letter in enumerate(word):
            for (anchor_row, anchor_col), existing_letter in grid.items():
                if letter != existing_letter:
                    continue

                for horizontal in (True, False):
                    start_row = anchor_row if horizontal else anchor_row - letter_index
                    start_col = anchor_col - letter_index if horizontal else anchor_col
                    start_key = (start_row, start_col, horizontal)

                    if start_key in seen_starts:
                        continue
                    seen_starts.add(start_key)

                    can_place, intersections, new_cells = self._can_place_word(
                        grid=grid,
                        word=word,
                        row=start_row,
                        col=start_col,
                        horizontal=horizontal,
                        require_intersection=True,
                    )

                    if not can_place:
                        continue

                    compactness_penalty = abs(start_row) + abs(start_col)
                    score = intersections * 100 + len(word) * 5 - new_cells * 2 - compactness_penalty
                    candidates.append((score, start_row, start_col, horizontal))

        candidates.sort(key=lambda item: (-item[0], item[1], item[2], item[3]))
        return [(row, col, horizontal) for _, row, col, horizontal in candidates]

    def _can_place_word(
        self,
        grid: Grid,
        word: str,
        row: int,
        col: int,
        horizontal: bool,
        require_intersection: bool,
    ) -> Tuple[bool, int, int]:
        intersections = 0
        new_cells = 0

        for index, letter in enumerate(word):
            cell_row = row if horizontal else row + index
            cell_col = col + index if horizontal else col
            cell = (cell_row, cell_col)

            existing = grid.get(cell)
            if existing is not None:
                if existing != letter:
                    return False, 0, 0
                intersections += 1
            else:
                new_cells += 1
                if horizontal:
                    if (cell_row - 1, cell_col) in grid or (cell_row + 1, cell_col) in grid:
                        return False, 0, 0
                else:
                    if (cell_row, cell_col - 1) in grid or (cell_row, cell_col + 1) in grid:
                        return False, 0, 0

        before = (row, col - 1) if horizontal else (row - 1, col)
        after = (row, col + len(word)) if horizontal else (row + len(word), col)
        if before in grid or after in grid:
            return False, 0, 0

        if require_intersection and intersections == 0:
            return False, 0, 0

        if new_cells == 0:
            return False, 0, 0

        return True, intersections, new_cells

    @staticmethod
    def _place_word(
        grid: Grid,
        usage: Dict[Tuple[int, int], int],
        word: str,
        row: int,
        col: int,
        horizontal: bool,
    ) -> None:
        for index, letter in enumerate(word):
            cell_row = row if horizontal else row + index
            cell_col = col + index if horizontal else col
            cell = (cell_row, cell_col)
            grid[cell] = letter
            usage[cell] = usage.get(cell, 0) + 1

    @staticmethod
    def _bounds(grid: Grid) -> Tuple[int, int, int, int]:
        rows = [coord[0] for coord in grid]
        cols = [coord[1] for coord in grid]
        return min(rows), max(rows), min(cols), max(cols)

    @staticmethod
    def _layout_score(result: CrosswordResult) -> int:
        area = (result.max_row - result.min_row + 1) * (result.max_col - result.min_col + 1)
        words_bonus = len(result.used_words) * 10000
        cross_bonus = result.intersections * 250
        compact_penalty = area
        return words_bonus + cross_bonus - compact_penalty

    @staticmethod
    def _grid_signature(grid: Grid) -> str:
        if not grid:
            return ""

        cells = [(row, col, letter) for (row, col), letter in grid.items()]
        variants: List[str] = []
        for transform in range(8):
            transformed: List[Tuple[int, int, str]] = []
            for row, col, letter in cells:
                transformed_row, transformed_col = CrosswordGenerator._transform_coordinate(
                    row, col, transform
                )
                transformed.append((transformed_row, transformed_col, letter))

            min_row = min(item[0] for item in transformed)
            min_col = min(item[1] for item in transformed)
            normalized = sorted(
                (row - min_row, col - min_col, letter) for row, col, letter in transformed
            )
            variants.append("|".join(f"{row},{col},{letter}" for row, col, letter in normalized))

        return min(variants)

    @staticmethod
    def _transform_coordinate(row: int, col: int, transform: int) -> Tuple[int, int]:
        if transform == 0:
            return row, col
        if transform == 1:
            return row, -col
        if transform == 2:
            return -row, col
        if transform == 3:
            return -row, -col
        if transform == 4:
            return col, row
        if transform == 5:
            return col, -row
        if transform == 6:
            return -col, row
        if transform == 7:
            return -col, -row
        raise ValueError("Unexpected transform index")


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.generator = CrosswordGenerator()
        self.current_variants: List[CrosswordResult] = []
        self.current_variant_index = 0
        self.last_input_word_count = 0
        self.variants_truncated = False
        self.rotation_steps = 0

        self.setWindowTitle("Qt Crossword Generator")
        self.resize(1000, 760)

        root = QWidget()
        self.setCentralWidget(root)
        layout = QVBoxLayout(root)

        title = QLabel("Enter words (one per line or separated by commas/spaces):")
        layout.addWidget(title)

        self.input_edit = QTextEdit()
        self.input_edit.setPlaceholderText("PYTHON\nWIDGET\nBUTTON\nLAYOUT\nRANDOM\nCROSSWORD")
        layout.addWidget(self.input_edit)

        self.generate_button = QPushButton("Generate crossword")
        self.generate_button.clicked.connect(self.on_generate_clicked)
        layout.addWidget(self.generate_button)

        navigation_layout = QHBoxLayout()
        self.prev_button = QPushButton("Previous variant")
        self.prev_button.clicked.connect(self.show_previous_variant)
        self.prev_button.setEnabled(False)
        navigation_layout.addWidget(self.prev_button)

        self.variant_label = QLabel("Variant: -/-")
        self.variant_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        navigation_layout.addWidget(self.variant_label)

        self.next_button = QPushButton("Next variant")
        self.next_button.clicked.connect(self.show_next_variant)
        self.next_button.setEnabled(False)
        navigation_layout.addWidget(self.next_button)
        layout.addLayout(navigation_layout)

        rotation_layout = QHBoxLayout()
        self.rotate_left_button = QPushButton("Rotate 90° left")
        self.rotate_left_button.clicked.connect(self.rotate_left)
        self.rotate_left_button.setEnabled(False)
        rotation_layout.addWidget(self.rotate_left_button)

        self.rotation_label = QLabel("Rotation: 0°")
        self.rotation_label.setAlignment(Qt.AlignmentFlag.AlignCenter)
        rotation_layout.addWidget(self.rotation_label)

        self.rotate_right_button = QPushButton("Rotate 90° right")
        self.rotate_right_button.clicked.connect(self.rotate_right)
        self.rotate_right_button.setEnabled(False)
        rotation_layout.addWidget(self.rotate_right_button)
        layout.addLayout(rotation_layout)

        self.status_label = QLabel("Ready.")
        self.status_label.setWordWrap(True)
        layout.addWidget(self.status_label)

        self.grid_table = QTableWidget()
        self.grid_table.setEditTriggers(QAbstractItemView.EditTrigger.NoEditTriggers)
        self.grid_table.setSelectionMode(QAbstractItemView.SelectionMode.NoSelection)
        self.grid_table.horizontalHeader().setVisible(False)
        self.grid_table.verticalHeader().setVisible(False)
        self.grid_table.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self.grid_table.setStyleSheet("QTableWidget { gridline-color: #707070; }")
        layout.addWidget(self.grid_table)

    def on_generate_clicked(self) -> None:
        words = parse_words(self.input_edit.toPlainText())
        if len(words) < 2:
            QMessageBox.warning(
                self,
                "Not enough data",
                "Please enter at least two words with 2+ characters.",
            )
            return

        variants_result = self.generator.generate_variants(words)
        if not variants_result.variants:
            QMessageBox.warning(self, "Generation failed", "Could not generate crossword.")
            return

        self.current_variants = variants_result.variants
        self.current_variant_index = 0
        self.last_input_word_count = len(words)
        self.variants_truncated = variants_result.truncated
        self.rotation_steps = 0
        self._show_variant(self.current_variant_index)

    def show_previous_variant(self) -> None:
        if not self.current_variants:
            return
        self._show_variant(self.current_variant_index - 1)

    def show_next_variant(self) -> None:
        if not self.current_variants:
            return
        self._show_variant(self.current_variant_index + 1)

    def rotate_left(self) -> None:
        if not self.current_variants:
            return
        self.rotation_steps = (self.rotation_steps - 1) % 4
        self._show_variant(self.current_variant_index)

    def rotate_right(self) -> None:
        if not self.current_variants:
            return
        self.rotation_steps = (self.rotation_steps + 1) % 4
        self._show_variant(self.current_variant_index)

    def _show_variant(self, index: int) -> None:
        if not self.current_variants:
            self.variant_label.setText("Variant: -/-")
            self.prev_button.setEnabled(False)
            self.next_button.setEnabled(False)
            self.rotate_left_button.setEnabled(False)
            self.rotate_right_button.setEnabled(False)
            self.rotation_label.setText("Rotation: 0°")
            return

        total = len(self.current_variants)
        self.current_variant_index = index % total
        result = self.current_variants[self.current_variant_index]
        self.render_grid(result)
        self._update_variant_label()
        self._update_rotation_label()
        self._update_status_label(result)

    def _update_variant_label(self) -> None:
        total = len(self.current_variants)
        if total == 0:
            self.variant_label.setText("Variant: -/-")
            self.prev_button.setEnabled(False)
            self.next_button.setEnabled(False)
            return

        has_multiple = total > 1
        self.prev_button.setEnabled(has_multiple)
        self.next_button.setEnabled(has_multiple)
        suffix = "+" if self.variants_truncated else ""
        self.variant_label.setText(
            f"Variant {self.current_variant_index + 1}/{total}{suffix} "
            f"(total: {total}{suffix})"
        )

    def _update_status_label(self, result: CrosswordResult) -> None:
        total_variants = len(self.current_variants)
        suffix = "+" if self.variants_truncated else ""
        limit_note = " Search limit reached." if self.variants_truncated else ""
        skipped = self._preview_words(result.skipped_words)
        skipped_text = f" Skipped: {skipped}." if skipped else ""
        rotation_angle = self.rotation_steps * 90

        self.status_label.setText(
            f"Unique variants found: {total_variants}{suffix}. "
            f"Placed {len(result.used_words)} of {self.last_input_word_count} words. "
            f"Intersections: {result.intersections}. "
            f"Rotation: {rotation_angle}°. "
            f"{skipped_text}{limit_note}"
        )

    def render_grid(self, result: CrosswordResult) -> None:
        display_grid = self._rotated_grid(result.grid)
        min_row, max_row, min_col, max_col = self._bounds(display_grid)
        rows = max_row - min_row + 1
        cols = max_col - min_col + 1

        self.grid_table.clear()
        self.grid_table.setRowCount(rows)
        self.grid_table.setColumnCount(cols)

        max_dimension = max(rows, cols, 1)
        cell_size = max(22, min(42, 820 // max_dimension))

        letter_font = QFont(self.grid_table.font())
        letter_font.setBold(True)
        letter_font.setPointSize(max(9, cell_size // 2))

        letter_bg = QColor("#ffffff")
        empty_bg = QColor("#252525")
        letter_fg = QColor("#111111")

        for row in range(rows):
            self.grid_table.setRowHeight(row, cell_size)
            for col in range(cols):
                if row == 0:
                    self.grid_table.setColumnWidth(col, cell_size)

                abs_row = min_row + row
                abs_col = min_col + col
                letter = display_grid.get((abs_row, abs_col), "")

                item = QTableWidgetItem(letter)
                item.setFlags(Qt.ItemFlag.ItemIsEnabled)
                item.setTextAlignment(Qt.AlignmentFlag.AlignCenter)

                if letter:
                    item.setBackground(letter_bg)
                    item.setForeground(letter_fg)
                    item.setFont(letter_font)
                else:
                    item.setBackground(empty_bg)
                    item.setForeground(empty_bg)

                self.grid_table.setItem(row, col, item)

    def _update_rotation_label(self) -> None:
        if not self.current_variants:
            self.rotate_left_button.setEnabled(False)
            self.rotate_right_button.setEnabled(False)
            self.rotation_label.setText("Rotation: 0°")
            return

        self.rotate_left_button.setEnabled(True)
        self.rotate_right_button.setEnabled(True)
        angle = self.rotation_steps * 90
        self.rotation_label.setText(f"Rotation: {angle}°")

    def _rotated_grid(self, grid: Grid) -> Grid:
        if self.rotation_steps % 4 == 0:
            return dict(grid)

        rotated: Grid = {}
        for (row, col), letter in grid.items():
            new_row, new_col = self._rotate_coordinate(row, col, self.rotation_steps)
            rotated[(new_row, new_col)] = letter
        return rotated

    @staticmethod
    def _bounds(grid: Grid) -> Tuple[int, int, int, int]:
        rows = [coord[0] for coord in grid]
        cols = [coord[1] for coord in grid]
        return min(rows), max(rows), min(cols), max(cols)

    @staticmethod
    def _rotate_coordinate(row: int, col: int, steps: int) -> Tuple[int, int]:
        normalized_steps = steps % 4
        if normalized_steps == 0:
            return row, col
        if normalized_steps == 1:
            return col, -row
        if normalized_steps == 2:
            return -row, -col
        return -col, row

    @staticmethod
    def _preview_words(words: List[str], limit: int = 12) -> str:
        if not words:
            return ""
        if len(words) <= limit:
            return ", ".join(words)
        return ", ".join(words[:limit]) + ", ..."


def main() -> None:
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
