using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace QtCrossword
{
    [DisallowMultipleComponent]
    public sealed class CrosswordGridRenderer : MonoBehaviour
    {
        private sealed class CellView
        {
            public GameObject Root;
            public Image Background;
            public Text Label;
        }

        [Header("References")]
        [SerializeField] private GridLayoutGroup gridLayout;
        [SerializeField] private Font letterFont;

        [Header("Sizing")]
        [SerializeField] private int maxViewSize = 820;
        [SerializeField] private int minCellSize = 22;
        [SerializeField] private int maxCellSize = 42;
        [SerializeField] private int minFontSize = 9;

        [Header("Colors")]
        [SerializeField] private Color letterBackground = Color.white;
        [SerializeField] private Color emptyBackground = new Color32(0x25, 0x25, 0x25, 0xFF);
        [SerializeField] private Color letterForeground = new Color32(0x11, 0x11, 0x11, 0xFF);
        [SerializeField] private Color emptyForeground = new Color32(0x25, 0x25, 0x25, 0xFF);

        private readonly List<CellView> cellPool = new List<CellView>();

        private void Awake()
        {
            if (gridLayout == null)
            {
                gridLayout = GetComponent<GridLayoutGroup>();
            }

            if (letterFont == null)
            {
                letterFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
        }

        public void RenderGrid(Dictionary<GridCoordinate, char> grid)
        {
            if (grid == null || grid.Count == 0)
            {
                ClearGrid();
                return;
            }

            int minRow;
            int maxRow;
            int minCol;
            int maxCol;
            CrosswordGenerator.Bounds(grid, out minRow, out maxRow, out minCol, out maxCol);

            int rows = maxRow - minRow + 1;
            int cols = maxCol - minCol + 1;
            int maxDimension = Mathf.Max(rows, cols, 1);
            int cellSize = Mathf.Clamp(maxViewSize / maxDimension, minCellSize, maxCellSize);
            int fontSize = Mathf.Max(minFontSize, cellSize / 2);

            if (gridLayout != null)
            {
                gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                gridLayout.constraintCount = cols;
                gridLayout.cellSize = new Vector2(cellSize, cellSize);
            }

            int totalCells = rows * cols;
            EnsureCellCount(totalCells);

            int poolIndex = 0;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    CellView view = cellPool[poolIndex];
                    poolIndex++;

                    GridCoordinate absolute = new GridCoordinate(minRow + row, minCol + col);
                    char letter;
                    bool hasLetter = grid.TryGetValue(absolute, out letter);

                    view.Root.SetActive(true);
                    view.Background.color = hasLetter ? letterBackground : emptyBackground;
                    view.Label.color = hasLetter ? letterForeground : emptyForeground;
                    view.Label.text = hasLetter ? letter.ToString() : string.Empty;
                    view.Label.fontSize = fontSize;
                }
            }

            for (int i = poolIndex; i < cellPool.Count; i++)
            {
                cellPool[i].Root.SetActive(false);
            }
        }

        public void ClearGrid()
        {
            for (int i = 0; i < cellPool.Count; i++)
            {
                cellPool[i].Root.SetActive(false);
            }
        }

        private void EnsureCellCount(int count)
        {
            while (cellPool.Count < count)
            {
                cellPool.Add(CreateCell());
            }
        }

        private CellView CreateCell()
        {
            Transform parent = gridLayout != null ? gridLayout.transform : transform;

            GameObject cellObject = new GameObject(
                "Cell",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image)
            );
            cellObject.transform.SetParent(parent, false);

            Image background = cellObject.GetComponent<Image>();
            background.raycastTarget = false;

            GameObject textObject = new GameObject(
                "Label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Text)
            );
            textObject.transform.SetParent(cellObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text label = textObject.GetComponent<Text>();
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.resizeTextForBestFit = false;
            label.fontStyle = FontStyle.Bold;
            label.font = letterFont;

            return new CellView
            {
                Root = cellObject,
                Background = background,
                Label = label
            };
        }
    }
}
