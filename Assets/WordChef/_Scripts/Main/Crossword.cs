using System.Collections.Generic;
using UnityEngine;

public class Crossword
{
    public class WordInfo
    {
        public string Word { get; set; }
        public bool IsVertical { get; set; }
        public Vector2Int StartPosition { get; set; }
    }

    public List<WordInfo> Words { get; private set; } = new List<WordInfo>();
    public int Width { get; private set; }
    public int Height { get; private set; }

    private char[,] grid;

    public Crossword(int width, int height)
    {
        Width = width;
        Height = height;
        grid = new char[width, height];
    }

    public bool AddWord(string word, bool isVertical, Vector2Int startPosition)
    {
        if (CanPlaceWord(word, isVertical, startPosition))
        {
            Words.Add(new WordInfo { Word = word, IsVertical = isVertical, StartPosition = startPosition });
            PlaceWord(word, isVertical, startPosition);
            return true;
        }
        return false;
    }

    private bool CanPlaceWord(string word, bool isVertical, Vector2Int startPosition)
    {
        int length = word.Length;

        if (isVertical)
        {
            if (startPosition.y + length > Height)
                return false;

            for (int i = 0; i < length; i++)
            {
                char existingChar = grid[startPosition.x, startPosition.y + i];
                if (existingChar != '\0' && existingChar != word[i])
                    return false;
            }
        }
        else
        {
            if (startPosition.x + length > Width)
                return false;

            for (int i = 0; i < length; i++)
            {
                char existingChar = grid[startPosition.x + i, startPosition.y];
                if (existingChar != '\0' && existingChar != word[i])
                    return false;
            }
        }

        return true;
    }

    private void PlaceWord(string word, bool isVertical, Vector2Int startPosition)
    {
        int length = word.Length;

        if (isVertical)
        {
            for (int i = 0; i < length; i++)
            {
                grid[startPosition.x, startPosition.y + i] = word[i];
            }
        }
        else
        {
            for (int i = 0; i < length; i++)
            {
                grid[startPosition.x + i, startPosition.y] = word[i];
            }
        }
    }

    public char GetCharAt(Vector2Int position)
    {
        return grid[position.x, position.y];
    }
}
