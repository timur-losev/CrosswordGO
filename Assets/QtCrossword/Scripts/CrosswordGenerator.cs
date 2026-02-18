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

    public sealed class ConstraintWordSelectionRequest
    {
        public int Count3;
        public int Count4;
        public int Count5;
        public int Count6;
        public int Count7;
        public int TargetUniqueLetters = 12;
        public int TargetUniqueLettersSpread;
        public int MaxSearchNodes = 120000;
        public int MaxBranching = 40;
        public Dictionary<int, List<string>> WordPoolsByLength = new Dictionary<int, List<string>>();
        public HashSet<string> ExcludedWords = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> PreviouslyUsedWords = new HashSet<string>(StringComparer.Ordinal);

        public int GetRequestedCount(int length)
        {
            switch (length)
            {
                case 3:
                    return Math.Max(0, Count3);
                case 4:
                    return Math.Max(0, Count4);
                case 5:
                    return Math.Max(0, Count5);
                case 6:
                    return Math.Max(0, Count6);
                case 7:
                    return Math.Max(0, Count7);
                default:
                    return 0;
            }
        }

        public int TotalRequestedCount
        {
            get
            {
                return
                    Math.Max(0, Count3) +
                    Math.Max(0, Count4) +
                    Math.Max(0, Count5) +
                    Math.Max(0, Count6) +
                    Math.Max(0, Count7);
            }
        }
    }

    public sealed class ConstraintWordSelectionResult
    {
        public bool HasRequest { get; private set; }
        public bool Success { get; private set; }
        public bool SearchTruncated { get; private set; }
        public List<string> SelectedWords { get; private set; }
        public int TargetUniqueLetters { get; private set; }
        public int TargetUniqueLettersSpread { get; private set; }
        public int AchievedUniqueLetters { get; private set; }
        public int Delta { get; private set; }
        public int CenterDelta { get; private set; }
        public int VisitedNodes { get; private set; }
        public string Diagnostics { get; private set; }
        public Dictionary<int, int> RequestedByLength { get; private set; }
        public Dictionary<int, int> AvailableByLength { get; private set; }

        public ConstraintWordSelectionResult(
            bool hasRequest,
            bool success,
            bool searchTruncated,
            List<string> selectedWords,
            int targetUniqueLetters,
            int achievedUniqueLetters,
            int delta,
            int visitedNodes,
            string diagnostics,
            Dictionary<int, int> requestedByLength,
            Dictionary<int, int> availableByLength,
            int targetUniqueLettersSpread = 0,
            int centerDelta = 0
        )
        {
            HasRequest = hasRequest;
            Success = success;
            SearchTruncated = searchTruncated;
            SelectedWords = selectedWords ?? new List<string>();
            TargetUniqueLetters = targetUniqueLetters;
            TargetUniqueLettersSpread = targetUniqueLettersSpread;
            AchievedUniqueLetters = achievedUniqueLetters;
            Delta = delta;
            CenterDelta = centerDelta;
            VisitedNodes = visitedNodes;
            Diagnostics = diagnostics ?? string.Empty;
            RequestedByLength = requestedByLength ?? new Dictionary<int, int>();
            AvailableByLength = availableByLength ?? new Dictionary<int, int>();
        }
    }

    public static class ConstraintWordPicker
    {
        private sealed class WordEntry
        {
            public string Word;
            public int Length;
            public uint LetterMask;
            public int UniqueLetterCount;
            public bool WasUsedPreviously;
            public int FrequencyRank;

            public WordEntry(
                string word,
                int length,
                uint letterMask,
                int uniqueLetterCount,
                bool wasUsedPreviously,
                int frequencyRank
            )
            {
                Word = word;
                Length = length;
                LetterMask = letterMask;
                UniqueLetterCount = uniqueLetterCount;
                WasUsedPreviously = wasUsedPreviously;
                FrequencyRank = frequencyRank;
            }
        }

        private struct RankedCandidate
        {
            public WordEntry Entry;
            public int Score;
            public int RangeDelta;
            public int CenterDelta;
            public int NewLetters;
            public int FrequencyRank;
            public bool WasUsedPreviously;
        }

        public static ConstraintWordSelectionResult SelectWords(ConstraintWordSelectionRequest request)
        {
            Dictionary<int, int> requestedByLength = BuildRequestedByLength(request);
            Dictionary<int, int> availableByLength = new Dictionary<int, int>();

            if (request == null)
            {
                return new ConstraintWordSelectionResult(
                    false,
                    false,
                    false,
                    new List<string>(),
                    0,
                    0,
                    0,
                    0,
                    "Constraint request is null.",
                    requestedByLength,
                    availableByLength
                );
            }

            int totalRequested = request.TotalRequestedCount;
            if (totalRequested == 0)
            {
                return new ConstraintWordSelectionResult(
                    false,
                    false,
                    false,
                    new List<string>(),
                    request.TargetUniqueLetters,
                    0,
                    0,
                    0,
                    "No constrained word counts were requested.",
                    requestedByLength,
                    availableByLength
                );
            }

            if (totalRequested < 2)
            {
                return new ConstraintWordSelectionResult(
                    true,
                    false,
                    false,
                    new List<string>(),
                    request.TargetUniqueLetters,
                    0,
                    0,
                    0,
                    "At least two words are required to build a crossword.",
                    requestedByLength,
                    availableByLength
                );
            }

            int targetUniqueLetters = Clamp(request.TargetUniqueLetters, 0, 26);
            int targetUniqueLettersSpread = Clamp(request.TargetUniqueLettersSpread, 0, 26);
            int minTargetUniqueLetters = Clamp(targetUniqueLetters - targetUniqueLettersSpread, 0, 26);
            int maxTargetUniqueLetters = Clamp(targetUniqueLetters + targetUniqueLettersSpread, 0, 26);
            int maxNodes = Math.Max(2000, request.MaxSearchNodes);
            int maxBranching = Math.Max(8, request.MaxBranching);

            HashSet<string> excludedWords = new HashSet<string>(StringComparer.Ordinal);
            if (request.ExcludedWords != null)
            {
                foreach (string item in request.ExcludedWords)
                {
                    if (string.IsNullOrWhiteSpace(item))
                    {
                        continue;
                    }

                    excludedWords.Add(item.Trim().ToUpperInvariant());
                }
            }

            HashSet<string> previouslyUsedWords = new HashSet<string>(StringComparer.Ordinal);
            if (request.PreviouslyUsedWords != null)
            {
                foreach (string item in request.PreviouslyUsedWords)
                {
                    if (string.IsNullOrWhiteSpace(item))
                    {
                        continue;
                    }

                    previouslyUsedWords.Add(item.Trim().ToUpperInvariant());
                }
            }

            Dictionary<int, List<WordEntry>> poolsByLength = new Dictionary<int, List<WordEntry>>();
            Dictionary<string, WordEntry> entryByWord = new Dictionary<string, WordEntry>(StringComparer.Ordinal);
            for (int length = 3; length <= 7; length++)
            {
                int requestedCount = request.GetRequestedCount(length);
                List<string> source = null;
                if (request.WordPoolsByLength != null)
                {
                    request.WordPoolsByLength.TryGetValue(length, out source);
                }

                List<WordEntry> filtered = FilterWordPool(source, length, excludedWords, previouslyUsedWords);
                poolsByLength[length] = filtered;
                availableByLength[length] = filtered.Count;
                for (int i = 0; i < filtered.Count; i++)
                {
                    entryByWord[filtered[i].Word] = filtered[i];
                }

                if (requestedCount > filtered.Count)
                {
                    return new ConstraintWordSelectionResult(
                        true,
                        false,
                        false,
                        new List<string>(),
                        targetUniqueLetters,
                        0,
                        0,
                        0,
                        "Not enough available words of length " + length +
                        ". Requested: " + requestedCount + ", available: " + filtered.Count + ".",
                        requestedByLength,
                        availableByLength
                    );
                }
            }

            List<int> slotLengths = new List<int>(totalRequested);
            for (int length = 3; length <= 7; length++)
            {
                int requestedCount = request.GetRequestedCount(length);
                for (int i = 0; i < requestedCount; i++)
                {
                    slotLengths.Add(length);
                }
            }

            slotLengths.Sort(delegate (int left, int right)
            {
                int leftPool = availableByLength[left];
                int rightPool = availableByLength[right];
                int poolCompare = leftPool.CompareTo(rightPool);
                if (poolCompare != 0)
                {
                    return poolCompare;
                }

                return right.CompareTo(left);
            });

            bool stopSearch = false;
            bool searchTruncated = false;
            int visitedNodes = 0;
            int bestRangeDelta = int.MaxValue;
            int bestCenterDelta = int.MaxValue;
            int bestUsedPreviouslyCount = int.MaxValue;
            int bestFrequencyRankSum = int.MaxValue;
            int bestUniqueLetters = -1;
            string bestSignature = null;
            List<string> bestSelected = null;

            List<string> selectedWords = new List<string>(totalRequested);
            HashSet<string> usedWords = new HashSet<string>(StringComparer.Ordinal);

            void CommitBest(uint letterMask, List<string> selected)
            {
                int achievedUnique = PopCount(letterMask);
                int rangeDelta = DistanceToRange(achievedUnique, minTargetUniqueLetters, maxTargetUniqueLetters);
                int centerDelta = Math.Abs(targetUniqueLetters - achievedUnique);
                int usedPreviouslyCount = 0;
                int frequencyRankSum = 0;
                for (int i = 0; i < selected.Count; i++)
                {
                    WordEntry metadata;
                    if (entryByWord.TryGetValue(selected[i], out metadata))
                    {
                        if (metadata.WasUsedPreviously)
                        {
                            usedPreviouslyCount++;
                        }
                        frequencyRankSum += metadata.FrequencyRank;
                    }
                }
                string signature = string.Join("|", selected.ToArray());

                bool isBetter = false;
                if (rangeDelta < bestRangeDelta)
                {
                    isBetter = true;
                }
                else if (rangeDelta == bestRangeDelta)
                {
                    if (centerDelta < bestCenterDelta)
                    {
                        isBetter = true;
                    }
                    else if (centerDelta == bestCenterDelta)
                    {
                        if (usedPreviouslyCount < bestUsedPreviouslyCount)
                        {
                            isBetter = true;
                        }
                        else if (usedPreviouslyCount == bestUsedPreviouslyCount)
                        {
                            if (frequencyRankSum < bestFrequencyRankSum)
                            {
                                isBetter = true;
                            }
                            else if (frequencyRankSum == bestFrequencyRankSum)
                            {
                                if (achievedUnique > bestUniqueLetters)
                                {
                                    isBetter = true;
                                }
                                else if (achievedUnique == bestUniqueLetters)
                                {
                                    if (bestSignature == null || string.CompareOrdinal(signature, bestSignature) < 0)
                                    {
                                        isBetter = true;
                                    }
                                }
                            }
                        }
                    }
                }

                if (!isBetter)
                {
                    return;
                }

                bestRangeDelta = rangeDelta;
                bestCenterDelta = centerDelta;
                bestUsedPreviouslyCount = usedPreviouslyCount;
                bestFrequencyRankSum = frequencyRankSum;
                bestUniqueLetters = achievedUnique;
                bestSignature = signature;
                bestSelected = new List<string>(selected);
            }

            void Search(int depth, uint currentMask)
            {
                if (stopSearch)
                {
                    return;
                }

                visitedNodes++;
                if (visitedNodes > maxNodes)
                {
                    searchTruncated = true;
                    return;
                }

                if (depth >= slotLengths.Count)
                {
                    CommitBest(currentMask, selectedWords);
                    return;
                }

                int currentUnique = PopCount(currentMask);
                int remainingSlots = slotLengths.Count - depth;
                int maxPossibleUnique = Math.Min(26, currentUnique + remainingSlots * 7);
                int optimisticRangeDelta = DistanceBetweenRanges(
                    currentUnique,
                    maxPossibleUnique,
                    minTargetUniqueLetters,
                    maxTargetUniqueLetters
                );
                if (optimisticRangeDelta > bestRangeDelta)
                {
                    return;
                }
                if (optimisticRangeDelta == bestRangeDelta)
                {
                    int optimisticCenterDelta = DistanceToRange(targetUniqueLetters, currentUnique, maxPossibleUnique);
                    if (optimisticCenterDelta > bestCenterDelta)
                    {
                        return;
                    }
                }

                int length = slotLengths[depth];
                List<WordEntry> pool = poolsByLength[length];
                List<WordEntry> shortlist = BuildShortlist(
                    pool,
                    usedWords,
                    currentMask,
                    targetUniqueLetters,
                    minTargetUniqueLetters,
                    maxTargetUniqueLetters,
                    maxBranching
                );
                for (int i = 0; i < shortlist.Count; i++)
                {
                    WordEntry entry = shortlist[i];
                    selectedWords.Add(entry.Word);
                    usedWords.Add(entry.Word);
                    Search(depth + 1, currentMask | entry.LetterMask);
                    usedWords.Remove(entry.Word);
                    selectedWords.RemoveAt(selectedWords.Count - 1);

                    if (stopSearch)
                    {
                        return;
                    }
                }
            }

            Search(0, 0u);

            if (bestSelected == null || bestSelected.Count == 0)
            {
                return new ConstraintWordSelectionResult(
                    true,
                    false,
                    searchTruncated,
                    new List<string>(),
                    targetUniqueLetters,
                    0,
                    0,
                    visitedNodes,
                    "Unable to pick a valid word set under current constraints.",
                    requestedByLength,
                    availableByLength,
                    targetUniqueLettersSpread
                );
            }

            bestSelected.Sort(delegate (string left, string right)
            {
                int lengthCompare = left.Length.CompareTo(right.Length);
                if (lengthCompare != 0)
                {
                    return lengthCompare;
                }

                return string.CompareOrdinal(left, right);
            });

            string targetRangeText;
            if (targetUniqueLettersSpread > 0)
            {
                targetRangeText = targetUniqueLetters + " ±" + targetUniqueLettersSpread +
                    " (" + minTargetUniqueLetters + "-" + maxTargetUniqueLetters + ")";
            }
            else
            {
                targetRangeText = targetUniqueLetters.ToString();
            }

            string diagnostics = "Picked " + bestSelected.Count + " words. " +
                "Unique letters: " + bestUniqueLetters + "/" + targetRangeText +
                " (range delta " + bestRangeDelta + ", center delta " + bestCenterDelta + ").";
            if (searchTruncated)
            {
                diagnostics += " Search limit reached.";
            }

            return new ConstraintWordSelectionResult(
                true,
                true,
                searchTruncated,
                bestSelected,
                targetUniqueLetters,
                bestUniqueLetters,
                bestRangeDelta,
                visitedNodes,
                diagnostics,
                requestedByLength,
                availableByLength,
                targetUniqueLettersSpread,
                bestCenterDelta
            );
        }

        private static Dictionary<int, int> BuildRequestedByLength(ConstraintWordSelectionRequest request)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            for (int length = 3; length <= 7; length++)
            {
                result[length] = request == null ? 0 : request.GetRequestedCount(length);
            }

            return result;
        }

        private static List<WordEntry> FilterWordPool(
            List<string> source,
            int expectedLength,
            HashSet<string> excludedWords,
            HashSet<string> previouslyUsedWords
        )
        {
            List<WordEntry> result = new List<WordEntry>();
            if (source == null || source.Count == 0)
            {
                return result;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                string raw = source[i];
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                string word = raw.Trim().ToUpperInvariant();
                if (word.Length != expectedLength)
                {
                    continue;
                }

                if (excludedWords != null && excludedWords.Contains(word))
                {
                    continue;
                }

                if (seen.Contains(word))
                {
                    continue;
                }

                uint letterMask;
                if (!TryBuildLetterMask(word, out letterMask))
                {
                    continue;
                }

                seen.Add(word);
                bool wasUsed = previouslyUsedWords != null && previouslyUsedWords.Contains(word);
                result.Add(new WordEntry(word, expectedLength, letterMask, PopCount(letterMask), wasUsed, i));
            }

            return result;
        }

        private static List<WordEntry> BuildShortlist(
            List<WordEntry> pool,
            HashSet<string> usedWords,
            uint currentMask,
            int targetUniqueLetters,
            int minTargetUniqueLetters,
            int maxTargetUniqueLetters,
            int maxBranching
        )
        {
            List<RankedCandidate> ranked = new List<RankedCandidate>();
            for (int i = 0; i < pool.Count; i++)
            {
                WordEntry entry = pool[i];
                if (usedWords.Contains(entry.Word))
                {
                    continue;
                }

                uint mergedMask = currentMask | entry.LetterMask;
                int achievedUnique = PopCount(mergedMask);
                int rangeDelta = DistanceToRange(achievedUnique, minTargetUniqueLetters, maxTargetUniqueLetters);
                int centerDelta = Math.Abs(targetUniqueLetters - achievedUnique);
                int newLetters = PopCount(entry.LetterMask & ~currentMask);
                int score = rangeDelta * 1000 + centerDelta * 100;

                ranked.Add(
                    new RankedCandidate
                    {
                        Entry = entry,
                        Score = score,
                        RangeDelta = rangeDelta,
                        CenterDelta = centerDelta,
                        NewLetters = newLetters,
                        FrequencyRank = entry.FrequencyRank,
                        WasUsedPreviously = entry.WasUsedPreviously
                    }
                );
            }

            ranked.Sort(delegate (RankedCandidate left, RankedCandidate right)
            {
                int scoreCompare = left.Score.CompareTo(right.Score);
                if (scoreCompare != 0)
                {
                    return scoreCompare;
                }

                int rangeCompare = left.RangeDelta.CompareTo(right.RangeDelta);
                if (rangeCompare != 0)
                {
                    return rangeCompare;
                }

                int centerCompare = left.CenterDelta.CompareTo(right.CenterDelta);
                if (centerCompare != 0)
                {
                    return centerCompare;
                }

                int usedCompare = (left.WasUsedPreviously ? 1 : 0).CompareTo(right.WasUsedPreviously ? 1 : 0);
                if (usedCompare != 0)
                {
                    return usedCompare;
                }

                int frequencyCompare = left.FrequencyRank.CompareTo(right.FrequencyRank);
                if (frequencyCompare != 0)
                {
                    return frequencyCompare;
                }

                int gainCompare = right.NewLetters.CompareTo(left.NewLetters);
                if (gainCompare != 0)
                {
                    return gainCompare;
                }

                return string.CompareOrdinal(left.Entry.Word, right.Entry.Word);
            });

            int take = ranked.Count;
            if (take > maxBranching)
            {
                take = maxBranching;
            }

            List<WordEntry> shortlist = new List<WordEntry>(take);
            for (int i = 0; i < take; i++)
            {
                shortlist.Add(ranked[i].Entry);
            }

            return shortlist;
        }

        private static bool TryBuildLetterMask(string word, out uint letterMask)
        {
            letterMask = 0u;
            if (string.IsNullOrEmpty(word))
            {
                return false;
            }

            for (int i = 0; i < word.Length; i++)
            {
                char letter = char.ToUpperInvariant(word[i]);
                if (letter < 'A' || letter > 'Z')
                {
                    return false;
                }

                uint bit = 1u << (letter - 'A');
                if ((letterMask & bit) != 0u)
                {
                    return false;
                }

                letterMask |= bit;
            }

            return true;
        }

        private static int PopCount(uint value)
        {
            int count = 0;
            while (value != 0u)
            {
                value &= value - 1u;
                count++;
            }

            return count;
        }

        private static int DistanceToRange(int value, int min, int max)
        {
            if (value < min)
            {
                return min - value;
            }

            if (value > max)
            {
                return value - max;
            }

            return 0;
        }

        private static int DistanceBetweenRanges(int minA, int maxA, int minB, int maxB)
        {
            if (maxA < minB)
            {
                return minB - maxA;
            }

            if (maxB < minA)
            {
                return minA - maxB;
            }

            return 0;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
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
