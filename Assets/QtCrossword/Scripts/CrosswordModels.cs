using System;
using System.Collections.Generic;

namespace QtCrossword
{
    [Serializable]
    public struct GridCoordinate : IEquatable<GridCoordinate>
    {
        public int Row;
        public int Col;

        public GridCoordinate(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public bool Equals(GridCoordinate other)
        {
            return Row == other.Row && Col == other.Col;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Row * 397) ^ Col;
            }
        }
    }

    [Serializable]
    public struct Placement
    {
        public string Word;
        public int Row;
        public int Col;
        public bool Horizontal;

        public Placement(string word, int row, int col, bool horizontal)
        {
            Word = word;
            Row = row;
            Col = col;
            Horizontal = horizontal;
        }
    }

    [Serializable]
    public sealed class CrosswordResult
    {
        public Dictionary<GridCoordinate, char> Grid { get; private set; }
        public List<Placement> Placements { get; private set; }
        public List<string> UsedWords { get; private set; }
        public List<string> SkippedWords { get; private set; }
        public int MinRow { get; private set; }
        public int MaxRow { get; private set; }
        public int MinCol { get; private set; }
        public int MaxCol { get; private set; }
        public int Intersections { get; private set; }

        public CrosswordResult(
            Dictionary<GridCoordinate, char> grid,
            List<Placement> placements,
            List<string> usedWords,
            List<string> skippedWords,
            int minRow,
            int maxRow,
            int minCol,
            int maxCol,
            int intersections
        )
        {
            Grid = grid;
            Placements = placements;
            UsedWords = usedWords;
            SkippedWords = skippedWords;
            MinRow = minRow;
            MaxRow = maxRow;
            MinCol = minCol;
            MaxCol = maxCol;
            Intersections = intersections;
        }
    }

    [Serializable]
    public sealed class CrosswordVariants
    {
        public List<CrosswordResult> Variants { get; private set; }
        public bool Truncated { get; private set; }

        public CrosswordVariants(List<CrosswordResult> variants, bool truncated)
        {
            Variants = variants ?? new List<CrosswordResult>();
            Truncated = truncated;
        }
    }
}
