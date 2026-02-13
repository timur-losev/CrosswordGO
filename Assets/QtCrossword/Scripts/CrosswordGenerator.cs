using System;
using System.Collections.Generic;

namespace QtCrossword
{
    public static class CrosswordWordParser
    {
        public static List<string> ParseWords(string rawText)
        {
            List<string> tokens = new List<string>();
            List<char> current = new List<char>();

            if (rawText == null)
            {
                rawText = string.Empty;
            }

            foreach (char character in rawText)
            {
                if (char.IsLetterOrDigit(character))
                {
                    current.Add(character);
                    continue;
                }

                if (current.Count > 0)
                {
                    tokens.Add(new string(current.ToArray()));
                    current.Clear();
                }
            }

            if (current.Count > 0)
            {
                tokens.Add(new string(current.ToArray()));
            }

            List<string> words = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < tokens.Count; i++)
            {
                string word = tokens[i].Trim().ToUpperInvariant();
                if (word.Length < 2)
                {
                    continue;
                }

                if (seen.Contains(word))
                {
                    continue;
                }

                seen.Add(word);
                words.Add(word);
            }

            return words;
        }
    }

    public sealed class CrosswordGenerator
    {
        private struct Candidate
        {
            public int Score;
            public int Row;
            public int Col;
            public bool Horizontal;
        }

        private struct CandidateStart : IEquatable<CandidateStart>
        {
            public int Row;
            public int Col;
            public bool Horizontal;

            public CandidateStart(int row, int col, bool horizontal)
            {
                Row = row;
                Col = col;
                Horizontal = horizontal;
            }

            public bool Equals(CandidateStart other)
            {
                return Row == other.Row && Col == other.Col && Horizontal == other.Horizontal;
            }

            public override bool Equals(object obj)
            {
                return obj is CandidateStart other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Row;
                    hashCode = (hashCode * 397) ^ Col;
                    hashCode = (hashCode * 397) ^ (Horizontal ? 1 : 0);
                    return hashCode;
                }
            }
        }

        public int MaxVariants { get; private set; }
        public int MaxNodes { get; private set; }

        public CrosswordGenerator(int maxVariants = 2000, int maxNodes = 220000)
        {
            MaxVariants = maxVariants;
            MaxNodes = maxNodes;
        }

        public CrosswordResult Generate(List<string> words)
        {
            CrosswordVariants variants = GenerateVariants(words);
            if (variants.Variants.Count == 0)
            {
                return null;
            }

            return variants.Variants[0];
        }

        public CrosswordVariants GenerateVariants(List<string> words)
        {
            if (words == null || words.Count == 0)
            {
                return new CrosswordVariants(new List<CrosswordResult>(), false);
            }

            List<string> orderedWords = new List<string>(words);
            orderedWords.Sort(delegate (string left, string right)
            {
                int lengthCompare = right.Length.CompareTo(left.Length);
                if (lengthCompare != 0)
                {
                    return lengthCompare;
                }

                return string.CompareOrdinal(left, right);
            });

            Dictionary<string, CrosswordResult> bestVariantsBySignature =
                new Dictionary<string, CrosswordResult>(StringComparer.Ordinal);
            int bestUsedCount = 0;
            int visitedNodes = 0;
            bool truncated = false;
            bool stopSearch = false;

            void RecordResult(
                Dictionary<GridCoordinate, char> grid,
                Dictionary<GridCoordinate, int> usage,
                List<Placement> placements,
                List<string> usedWords
            )
            {
                int usedCount = usedWords.Count;
                if (usedCount == 0)
                {
                    return;
                }

                if (usedCount < bestUsedCount)
                {
                    return;
                }

                string signature = GridSignature(grid);
                if (usedCount > bestUsedCount)
                {
                    bestUsedCount = usedCount;
                    bestVariantsBySignature.Clear();
                    truncated = false;
                }

                if (bestVariantsBySignature.ContainsKey(signature))
                {
                    return;
                }

                if (usedCount == bestUsedCount && bestVariantsBySignature.Count >= MaxVariants)
                {
                    truncated = true;
                    if (bestUsedCount == words.Count)
                    {
                        stopSearch = true;
                    }

                    return;
                }

                int minRow;
                int maxRow;
                int minCol;
                int maxCol;
                Bounds(grid, out minRow, out maxRow, out minCol, out maxCol);

                int intersections = 0;
                foreach (KeyValuePair<GridCoordinate, int> pair in usage)
                {
                    if (pair.Value > 1)
                    {
                        intersections++;
                    }
                }

                HashSet<string> usedWordSet = new HashSet<string>(usedWords, StringComparer.Ordinal);
                List<string> skippedWords = new List<string>();
                for (int i = 0; i < words.Count; i++)
                {
                    if (!usedWordSet.Contains(words[i]))
                    {
                        skippedWords.Add(words[i]);
                    }
                }

                bestVariantsBySignature[signature] = new CrosswordResult(
                    new Dictionary<GridCoordinate, char>(grid),
                    new List<Placement>(placements),
                    new List<string>(usedWords),
                    skippedWords,
                    minRow,
                    maxRow,
                    minCol,
                    maxCol,
                    intersections
                );
            }

            void SearchFromOrder(
                List<string> order,
                int wordIndex,
                Dictionary<GridCoordinate, char> grid,
                Dictionary<GridCoordinate, int> usage,
                List<Placement> placements,
                List<string> usedWords
            )
            {
                if (stopSearch)
                {
                    return;
                }

                visitedNodes++;
                if (visitedNodes > MaxNodes)
                {
                    truncated = true;
                    stopSearch = true;
                    return;
                }

                int remainingWords = order.Count - wordIndex;
                if (usedWords.Count + remainingWords < bestUsedCount)
                {
                    return;
                }

                if (wordIndex >= order.Count)
                {
                    RecordResult(grid, usage, placements, usedWords);
                    return;
                }

                string word = order[wordIndex];
                List<Candidate> candidates = FindCandidates(word, grid);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (stopSearch)
                    {
                        return;
                    }

                    Candidate candidate = candidates[i];
                    Dictionary<GridCoordinate, char> newGrid = new Dictionary<GridCoordinate, char>(grid);
                    Dictionary<GridCoordinate, int> newUsage = new Dictionary<GridCoordinate, int>(usage);
                    PlaceWord(newGrid, newUsage, word, candidate.Row, candidate.Col, candidate.Horizontal);

                    List<Placement> newPlacements = new List<Placement>(placements)
                    {
                        new Placement(word, candidate.Row, candidate.Col, candidate.Horizontal)
                    };

                    List<string> newUsedWords = new List<string>(usedWords)
                    {
                        word
                    };

                    SearchFromOrder(
                        order,
                        wordIndex + 1,
                        newGrid,
                        newUsage,
                        newPlacements,
                        newUsedWords
                    );
                }

                // This branch keeps words optional and allows more compact alternatives.
                SearchFromOrder(order, wordIndex + 1, grid, usage, placements, usedWords);
            }

            for (int seedIndex = 0; seedIndex < orderedWords.Count; seedIndex++)
            {
                if (stopSearch)
                {
                    break;
                }

                string seedWord = orderedWords[seedIndex];
                Dictionary<GridCoordinate, char> grid = new Dictionary<GridCoordinate, char>();
                Dictionary<GridCoordinate, int> usage = new Dictionary<GridCoordinate, int>();
                PlaceWord(grid, usage, seedWord, 0, 0, true);

                List<string> rest = new List<string>();
                for (int i = 0; i < orderedWords.Count; i++)
                {
                    if (!string.Equals(orderedWords[i], seedWord, StringComparison.Ordinal))
                    {
                        rest.Add(orderedWords[i]);
                    }
                }

                SearchFromOrder(
                    rest,
                    0,
                    grid,
                    usage,
                    new List<Placement> { new Placement(seedWord, 0, 0, true) },
                    new List<string> { seedWord }
                );
            }

            List<CrosswordResult> variants = new List<CrosswordResult>(bestVariantsBySignature.Values);
            variants.Sort(delegate (CrosswordResult left, CrosswordResult right)
            {
                int scoreCompare = LayoutScore(right).CompareTo(LayoutScore(left));
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                string leftSignature = GridSignature(left.Grid);
                string rightSignature = GridSignature(right.Grid);
                return string.CompareOrdinal(leftSignature, rightSignature);
            });

            return new CrosswordVariants(variants, truncated);
        }

        private List<Candidate> FindCandidates(string word, Dictionary<GridCoordinate, char> grid)
        {
            List<Candidate> candidates = new List<Candidate>();
            HashSet<CandidateStart> seenStarts = new HashSet<CandidateStart>();

            for (int letterIndex = 0; letterIndex < word.Length; letterIndex++)
            {
                char letter = word[letterIndex];
                foreach (KeyValuePair<GridCoordinate, char> pair in grid)
                {
                    if (letter != pair.Value)
                    {
                        continue;
                    }

                    int anchorRow = pair.Key.Row;
                    int anchorCol = pair.Key.Col;
                    for (int i = 0; i < 2; i++)
                    {
                        bool horizontal = i == 0;
                        int startRow = horizontal ? anchorRow : anchorRow - letterIndex;
                        int startCol = horizontal ? anchorCol - letterIndex : anchorCol;
                        CandidateStart startKey = new CandidateStart(startRow, startCol, horizontal);
                        if (seenStarts.Contains(startKey))
                        {
                            continue;
                        }

                        seenStarts.Add(startKey);

                        int intersections;
                        int newCells;
                        bool canPlace = CanPlaceWord(
                            grid,
                            word,
                            startRow,
                            startCol,
                            horizontal,
                            true,
                            out intersections,
                            out newCells
                        );

                        if (!canPlace)
                        {
                            continue;
                        }

                        int compactnessPenalty = Math.Abs(startRow) + Math.Abs(startCol);
                        int score = intersections * 100 + word.Length * 5 - newCells * 2 - compactnessPenalty;
                        candidates.Add(
                            new Candidate
                            {
                                Score = score,
                                Row = startRow,
                                Col = startCol,
                                Horizontal = horizontal
                            }
                        );
                    }
                }
            }

            candidates.Sort(delegate (Candidate left, Candidate right)
            {
                int scoreCompare = right.Score.CompareTo(left.Score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                int rowCompare = left.Row.CompareTo(right.Row);
                if (rowCompare != 0)
                {
                    return rowCompare;
                }

                int colCompare = left.Col.CompareTo(right.Col);
                if (colCompare != 0)
                {
                    return colCompare;
                }

                int leftHorizontal = left.Horizontal ? 1 : 0;
                int rightHorizontal = right.Horizontal ? 1 : 0;
                return leftHorizontal.CompareTo(rightHorizontal);
            });

            return candidates;
        }

        private static bool CanPlaceWord(
            Dictionary<GridCoordinate, char> grid,
            string word,
            int row,
            int col,
            bool horizontal,
            bool requireIntersection,
            out int intersections,
            out int newCells
        )
        {
            intersections = 0;
            newCells = 0;

            for (int index = 0; index < word.Length; index++)
            {
                int cellRow = horizontal ? row : row + index;
                int cellCol = horizontal ? col + index : col;
                GridCoordinate cell = new GridCoordinate(cellRow, cellCol);
                char existing;
                if (grid.TryGetValue(cell, out existing))
                {
                    if (existing != word[index])
                    {
                        intersections = 0;
                        newCells = 0;
                        return false;
                    }

                    intersections++;
                }
                else
                {
                    newCells++;
                    if (horizontal)
                    {
                        if (grid.ContainsKey(new GridCoordinate(cellRow - 1, cellCol))
                            || grid.ContainsKey(new GridCoordinate(cellRow + 1, cellCol)))
                        {
                            intersections = 0;
                            newCells = 0;
                            return false;
                        }
                    }
                    else
                    {
                        if (grid.ContainsKey(new GridCoordinate(cellRow, cellCol - 1))
                            || grid.ContainsKey(new GridCoordinate(cellRow, cellCol + 1)))
                        {
                            intersections = 0;
                            newCells = 0;
                            return false;
                        }
                    }
                }
            }

            GridCoordinate before = horizontal
                ? new GridCoordinate(row, col - 1)
                : new GridCoordinate(row - 1, col);
            GridCoordinate after = horizontal
                ? new GridCoordinate(row, col + word.Length)
                : new GridCoordinate(row + word.Length, col);
            if (grid.ContainsKey(before) || grid.ContainsKey(after))
            {
                intersections = 0;
                newCells = 0;
                return false;
            }

            if (requireIntersection && intersections == 0)
            {
                intersections = 0;
                newCells = 0;
                return false;
            }

            if (newCells == 0)
            {
                intersections = 0;
                newCells = 0;
                return false;
            }

            return true;
        }

        public static void PlaceWord(
            Dictionary<GridCoordinate, char> grid,
            Dictionary<GridCoordinate, int> usage,
            string word,
            int row,
            int col,
            bool horizontal
        )
        {
            for (int index = 0; index < word.Length; index++)
            {
                int cellRow = horizontal ? row : row + index;
                int cellCol = horizontal ? col + index : col;
                GridCoordinate cell = new GridCoordinate(cellRow, cellCol);
                grid[cell] = word[index];
                int count;
                usage.TryGetValue(cell, out count);
                usage[cell] = count + 1;
            }
        }

        public static void Bounds(
            Dictionary<GridCoordinate, char> grid,
            out int minRow,
            out int maxRow,
            out int minCol,
            out int maxCol
        )
        {
            if (grid == null || grid.Count == 0)
            {
                minRow = 0;
                maxRow = 0;
                minCol = 0;
                maxCol = 0;
                return;
            }

            bool first = true;
            minRow = 0;
            maxRow = 0;
            minCol = 0;
            maxCol = 0;

            foreach (KeyValuePair<GridCoordinate, char> pair in grid)
            {
                int row = pair.Key.Row;
                int col = pair.Key.Col;
                if (first)
                {
                    minRow = row;
                    maxRow = row;
                    minCol = col;
                    maxCol = col;
                    first = false;
                    continue;
                }

                if (row < minRow)
                {
                    minRow = row;
                }

                if (row > maxRow)
                {
                    maxRow = row;
                }

                if (col < minCol)
                {
                    minCol = col;
                }

                if (col > maxCol)
                {
                    maxCol = col;
                }
            }
        }

        public static int LayoutScore(CrosswordResult result)
        {
            int area = (result.MaxRow - result.MinRow + 1) * (result.MaxCol - result.MinCol + 1);
            int wordsBonus = result.UsedWords.Count * 10000;
            int crossBonus = result.Intersections * 250;
            int compactPenalty = area;
            return wordsBonus + crossBonus - compactPenalty;
        }

        public static string GridSignature(Dictionary<GridCoordinate, char> grid)
        {
            if (grid == null || grid.Count == 0)
            {
                return string.Empty;
            }

            List<Tuple<int, int, char>> cells = new List<Tuple<int, int, char>>(grid.Count);
            foreach (KeyValuePair<GridCoordinate, char> pair in grid)
            {
                cells.Add(Tuple.Create(pair.Key.Row, pair.Key.Col, pair.Value));
            }

            List<string> variants = new List<string>(8);
            for (int transform = 0; transform < 8; transform++)
            {
                List<Tuple<int, int, char>> transformed = new List<Tuple<int, int, char>>(cells.Count);
                for (int i = 0; i < cells.Count; i++)
                {
                    int transformedRow;
                    int transformedCol;
                    TransformCoordinate(cells[i].Item1, cells[i].Item2, transform, out transformedRow, out transformedCol);
                    transformed.Add(Tuple.Create(transformedRow, transformedCol, cells[i].Item3));
                }

                int minRow = int.MaxValue;
                int minCol = int.MaxValue;
                for (int i = 0; i < transformed.Count; i++)
                {
                    if (transformed[i].Item1 < minRow)
                    {
                        minRow = transformed[i].Item1;
                    }

                    if (transformed[i].Item2 < minCol)
                    {
                        minCol = transformed[i].Item2;
                    }
                }

                List<Tuple<int, int, char>> normalized = new List<Tuple<int, int, char>>(transformed.Count);
                for (int i = 0; i < transformed.Count; i++)
                {
                    normalized.Add(
                        Tuple.Create(
                            transformed[i].Item1 - minRow,
                            transformed[i].Item2 - minCol,
                            transformed[i].Item3
                        )
                    );
                }

                normalized.Sort(delegate (Tuple<int, int, char> left, Tuple<int, int, char> right)
                {
                    int rowCompare = left.Item1.CompareTo(right.Item1);
                    if (rowCompare != 0)
                    {
                        return rowCompare;
                    }

                    int colCompare = left.Item2.CompareTo(right.Item2);
                    if (colCompare != 0)
                    {
                        return colCompare;
                    }

                    return left.Item3.CompareTo(right.Item3);
                });

                string[] parts = new string[normalized.Count];
                for (int i = 0; i < normalized.Count; i++)
                {
                    parts[i] = normalized[i].Item1 + "," + normalized[i].Item2 + "," + normalized[i].Item3;
                }

                variants.Add(string.Join("|", parts));
            }

            string best = variants[0];
            for (int i = 1; i < variants.Count; i++)
            {
                if (string.CompareOrdinal(variants[i], best) < 0)
                {
                    best = variants[i];
                }
            }

            return best;
        }

        public static void TransformCoordinate(int row, int col, int transform, out int transformedRow, out int transformedCol)
        {
            switch (transform)
            {
                case 0:
                    transformedRow = row;
                    transformedCol = col;
                    return;
                case 1:
                    transformedRow = row;
                    transformedCol = -col;
                    return;
                case 2:
                    transformedRow = -row;
                    transformedCol = col;
                    return;
                case 3:
                    transformedRow = -row;
                    transformedCol = -col;
                    return;
                case 4:
                    transformedRow = col;
                    transformedCol = row;
                    return;
                case 5:
                    transformedRow = col;
                    transformedCol = -row;
                    return;
                case 6:
                    transformedRow = -col;
                    transformedCol = row;
                    return;
                case 7:
                    transformedRow = -col;
                    transformedCol = -row;
                    return;
                default:
                    throw new ArgumentOutOfRangeException("transform", "Unexpected transform index.");
            }
        }
    }
}
