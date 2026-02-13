using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace QtCrossword
{
    [DisallowMultipleComponent]
    public sealed class CrosswordUIController : MonoBehaviour
    {
        [Header("Input and Buttons")]
        [SerializeField] private InputField inputField;
        [SerializeField] private Button generateButton;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button rotateLeftButton;
        [SerializeField] private Button rotateRightButton;

        [Header("Labels")]
        [SerializeField] private Text variantLabel;
        [SerializeField] private Text rotationLabel;
        [SerializeField] private Text statusLabel;

        [Header("Grid")]
        [SerializeField] private CrosswordGridRenderer gridRenderer;

        [Header("Generator Limits")]
        [SerializeField] private int maxVariants = 2000;
        [SerializeField] private int maxNodes = 220000;

        private CrosswordGenerator generator;
        private List<CrosswordResult> currentVariants = new List<CrosswordResult>();
        private int currentVariantIndex;
        private int lastInputWordCount;
        private bool variantsTruncated;
        private int rotationSteps;

        private void Awake()
        {
            generator = new CrosswordGenerator(maxVariants, maxNodes);
            ApplyEmptyState();
            SetStatus("Ready.");
        }

        private void OnEnable()
        {
            if (generateButton != null)
            {
                generateButton.onClick.AddListener(OnGenerateClicked);
            }

            if (previousButton != null)
            {
                previousButton.onClick.AddListener(ShowPreviousVariant);
            }

            if (nextButton != null)
            {
                nextButton.onClick.AddListener(ShowNextVariant);
            }

            if (rotateLeftButton != null)
            {
                rotateLeftButton.onClick.AddListener(RotateLeft);
            }

            if (rotateRightButton != null)
            {
                rotateRightButton.onClick.AddListener(RotateRight);
            }
        }

        private void OnDisable()
        {
            if (generateButton != null)
            {
                generateButton.onClick.RemoveListener(OnGenerateClicked);
            }

            if (previousButton != null)
            {
                previousButton.onClick.RemoveListener(ShowPreviousVariant);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(ShowNextVariant);
            }

            if (rotateLeftButton != null)
            {
                rotateLeftButton.onClick.RemoveListener(RotateLeft);
            }

            if (rotateRightButton != null)
            {
                rotateRightButton.onClick.RemoveListener(RotateRight);
            }
        }

        public void OnGenerateClicked()
        {
            string rawInput = inputField != null ? inputField.text : string.Empty;
            List<string> words = CrosswordWordParser.ParseWords(rawInput);
            if (words.Count < 2)
            {
                SetStatus("Please enter at least two words with 2+ characters.");
                return;
            }

            CrosswordVariants variantsResult = generator.GenerateVariants(words);
            if (variantsResult.Variants.Count == 0)
            {
                SetStatus("Could not generate crossword.");
                return;
            }

            currentVariants = variantsResult.Variants;
            currentVariantIndex = 0;
            lastInputWordCount = words.Count;
            variantsTruncated = variantsResult.Truncated;
            rotationSteps = 0;
            ShowVariant(currentVariantIndex);
        }

        public void ShowPreviousVariant()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            ShowVariant(currentVariantIndex - 1);
        }

        public void ShowNextVariant()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            ShowVariant(currentVariantIndex + 1);
        }

        public void RotateLeft()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            rotationSteps = Mod(rotationSteps - 1, 4);
            ShowVariant(currentVariantIndex);
        }

        public void RotateRight()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            rotationSteps = Mod(rotationSteps + 1, 4);
            ShowVariant(currentVariantIndex);
        }

        private void ShowVariant(int index)
        {
            if (currentVariants.Count == 0)
            {
                ApplyEmptyState();
                return;
            }

            int total = currentVariants.Count;
            currentVariantIndex = Mod(index, total);
            CrosswordResult result = currentVariants[currentVariantIndex];
            Dictionary<GridCoordinate, char> displayGrid = RotatedGrid(result.Grid);

            if (gridRenderer != null)
            {
                gridRenderer.RenderGrid(displayGrid);
            }

            UpdateVariantLabel();
            UpdateRotationLabel();
            UpdateStatusLabel(result);
        }

        private void ApplyEmptyState()
        {
            SetText(variantLabel, "Variant: -/-");
            SetText(rotationLabel, "Rotation: 0°");
            SetButtonEnabled(previousButton, false);
            SetButtonEnabled(nextButton, false);
            SetButtonEnabled(rotateLeftButton, false);
            SetButtonEnabled(rotateRightButton, false);
            if (gridRenderer != null)
            {
                gridRenderer.ClearGrid();
            }
        }

        private void UpdateVariantLabel()
        {
            int total = currentVariants.Count;
            if (total == 0)
            {
                SetText(variantLabel, "Variant: -/-");
                SetButtonEnabled(previousButton, false);
                SetButtonEnabled(nextButton, false);
                return;
            }

            bool hasMultiple = total > 1;
            SetButtonEnabled(previousButton, hasMultiple);
            SetButtonEnabled(nextButton, hasMultiple);

            string suffix = variantsTruncated ? "+" : string.Empty;
            SetText(
                variantLabel,
                "Variant " + (currentVariantIndex + 1) + "/" + total + suffix +
                " (total: " + total + suffix + ")"
            );
        }

        private void UpdateStatusLabel(CrosswordResult result)
        {
            int totalVariants = currentVariants.Count;
            string suffix = variantsTruncated ? "+" : string.Empty;
            string limitNote = variantsTruncated ? " Search limit reached." : string.Empty;
            string skipped = PreviewWords(result.SkippedWords);
            string skippedText = string.IsNullOrEmpty(skipped) ? string.Empty : " Skipped: " + skipped + ".";
            int rotationAngle = rotationSteps * 90;

            SetStatus(
                "Unique variants found: " + totalVariants + suffix + ". " +
                "Placed " + result.UsedWords.Count + " of " + lastInputWordCount + " words. " +
                "Intersections: " + result.Intersections + ". " +
                "Rotation: " + rotationAngle + "°. " +
                skippedText + limitNote
            );
        }

        private void UpdateRotationLabel()
        {
            if (currentVariants.Count == 0)
            {
                SetButtonEnabled(rotateLeftButton, false);
                SetButtonEnabled(rotateRightButton, false);
                SetText(rotationLabel, "Rotation: 0°");
                return;
            }

            SetButtonEnabled(rotateLeftButton, true);
            SetButtonEnabled(rotateRightButton, true);
            int angle = rotationSteps * 90;
            SetText(rotationLabel, "Rotation: " + angle + "°");
        }

        private Dictionary<GridCoordinate, char> RotatedGrid(Dictionary<GridCoordinate, char> grid)
        {
            if (grid == null)
            {
                return new Dictionary<GridCoordinate, char>();
            }

            int normalizedSteps = Mod(rotationSteps, 4);
            if (normalizedSteps == 0)
            {
                return new Dictionary<GridCoordinate, char>(grid);
            }

            Dictionary<GridCoordinate, char> rotated = new Dictionary<GridCoordinate, char>(grid.Count);
            foreach (KeyValuePair<GridCoordinate, char> pair in grid)
            {
                int newRow;
                int newCol;
                RotateCoordinate(pair.Key.Row, pair.Key.Col, normalizedSteps, out newRow, out newCol);
                rotated[new GridCoordinate(newRow, newCol)] = pair.Value;
            }

            return rotated;
        }

        private static void RotateCoordinate(int row, int col, int steps, out int newRow, out int newCol)
        {
            int normalizedSteps = Mod(steps, 4);
            if (normalizedSteps == 0)
            {
                newRow = row;
                newCol = col;
                return;
            }

            if (normalizedSteps == 1)
            {
                newRow = col;
                newCol = -row;
                return;
            }

            if (normalizedSteps == 2)
            {
                newRow = -row;
                newCol = -col;
                return;
            }

            newRow = -col;
            newCol = row;
        }

        private static string PreviewWords(List<string> words, int limit = 12)
        {
            if (words == null || words.Count == 0)
            {
                return string.Empty;
            }

            if (words.Count <= limit)
            {
                return string.Join(", ", words.ToArray());
            }

            List<string> preview = words.GetRange(0, limit);
            return string.Join(", ", preview.ToArray()) + ", ...";
        }

        private static int Mod(int value, int divisor)
        {
            if (divisor == 0)
            {
                return 0;
            }

            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private void SetStatus(string value)
        {
            SetText(statusLabel, value);
        }

        private static void SetText(Text label, string value)
        {
            if (label != null)
            {
                label.text = value;
            }
        }

        private static void SetButtonEnabled(Button button, bool enabled)
        {
            if (button != null)
            {
                button.interactable = enabled;
            }
        }
    }
}
