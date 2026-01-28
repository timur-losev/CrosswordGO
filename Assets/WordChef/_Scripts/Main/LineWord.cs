using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineWord : MonoBehaviour
{

    public string answer;
    public float cellSize;
    public List<Cell> cells = new List<Cell>();
    public int numLetters;
    public float lineLength;

    [HideInInspector]
    public bool isShown, isVertical;

    public void Build(bool isVertical)
    {
        this.isVertical = isVertical;
        numLetters = answer.Length;
        float cellGap = cellSize * Const.CELL_GAP_COEF;
        float step = cellSize + cellGap;
        RectTransform lineRect = GetComponent<RectTransform>();
        lineRect.pivot = new Vector2(0f, 1f);
        lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);

        for (int i = 0; i < numLetters; i++)
        {
            Cell cell = Instantiate(MonoUtils.instance.cell);
            cell.letter = answer[i].ToString();
            cell.letterText.transform.localScale = Vector3.one * (cellSize / 80f);
            cell.letterText.fontSize = ConfigController.Config.fontSizeInCellMainScene;

            RectTransform cellTransform = cell.GetComponent<RectTransform>();
            cellTransform.SetParent(transform, false);
            cellTransform.sizeDelta = new Vector2(cellSize, cellSize);
            cellTransform.localScale = Vector3.one;

            float x = cellSize * 0.5f + (isVertical ? 0f : i * step);
            float y = -cellSize * 0.5f - (isVertical ? i * step : 0f);

            cellTransform.anchoredPosition = new Vector2(x, y);
            cells.Add(cell);
        }
    }

    public void Initialize(bool isVertical, float cellSize, List<Cell> existingCells)
    {
        this.isVertical = isVertical;
        this.cellSize = cellSize;
        cells = existingCells;
        numLetters = answer.Length;

        var lineRect = GetComponent<RectTransform>();
        lineRect.pivot = new Vector2(0f, 1f);
        lineRect.anchorMin = lineRect.anchorMax = new Vector2(0.5f, 0.5f);

        SetLineLength();
    }

    public void SetLineLength()
    {
        int numLetters = answer.Length;
        var rt = GetComponent<RectTransform>();
        lineLength = numLetters * cellSize + (numLetters - 1) * cellSize * Const.CELL_GAP_COEF;
        if (isVertical)
        {
            rt.sizeDelta = new Vector2(cellSize, lineLength);
        }
        else
        {
            rt.sizeDelta = new Vector2(lineLength, cellSize);
        }
    }

    public void SetProgress(string progress)
    {
        isShown = true;
        int i = 0;
        foreach (var cell in cells)
        {
            if (progress[i] == '1')
            {
                cell.isShown = true;
                cell.letterText.text = cell.letter;
            }
            else
            {
                isShown = false;
            }
            i++;
        }
    }

    public void ShowAnswer()
    {
        isShown = true;
        foreach (var cell in cells)
        {
            cell.isShown = true;
        }

        StartCoroutine(IEShowAnswer());
    }

    public IEnumerator IEShowAnswer()
    {
        foreach (var cell in cells)
        {
            cell.isShown = true;
            cell.Animate();
            yield return new WaitForSeconds(0.1f);
        }
    }

    public void ShowHint()
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            if (!cell.isShown)
            {
                cell.ShowHint();
                if (i == cells.Count - 1)
                {
                    isShown = true;
                }
                return;
            }
        }
    }
}
