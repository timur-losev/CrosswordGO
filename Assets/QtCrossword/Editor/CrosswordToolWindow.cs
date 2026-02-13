using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QtCrossword.EditorTools
{
    public sealed class CrosswordToolWindow : EditorWindow
    {
        private const string DefaultStorageFolder = "Assets/WordChef/Resources/World_0/SubWorld_0";

        private string inputText =
            "PYTHON\nWIDGET\nBUTTON\nLAYOUT\nRANDOM\nCROSSWORD";

        private int maxVariants = 2000;
        private int maxNodes = 220000;

        private List<CrosswordResult> currentVariants = new List<CrosswordResult>();
        private int currentVariantIndex;
        private int lastInputWordCount;
        private bool variantsTruncated;
        private int rotationSteps;
        private Vector2 gridScroll;
        private string statusText = "Ready.";
        private string storageFolder = DefaultStorageFolder;
        private List<string> jsonFileNames = new List<string>();
        private int selectedJsonFileIndex = -1;
        private string saveFileName = "LevelCW_0.json";
        private int saveOffsetX;
        private int saveOffsetY;

        private GUIStyle statusStyle;
        private GUIStyle cellStyle;

        [MenuItem("Tools/Qt Crossword Generator")]
        public static void OpenWindow()
        {
            CrosswordToolWindow window = GetWindow<CrosswordToolWindow>("Qt Crossword Generator");
            window.minSize = new Vector2(960f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshJsonFiles();
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.LabelField("Enter words (one per line or separated by commas/spaces):", EditorStyles.boldLabel);
            inputText = EditorGUILayout.TextArea(inputText, GUILayout.MinHeight(120f));

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            maxVariants = Mathf.Max(1, EditorGUILayout.IntField("Max Variants", maxVariants));
            maxNodes = Mathf.Max(1, EditorGUILayout.IntField("Max Nodes", maxNodes));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("Generate crossword", GUILayout.Height(28f)))
            {
                OnGenerateClicked();
            }

            DrawNavigationRow();
            DrawRotationRow();
            DrawStorageSection();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(statusText, statusStyle);
            EditorGUILayout.Space(8f);

            DrawGridArea();
        }

        private void DrawNavigationRow()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(currentVariants.Count <= 1);
            if (GUILayout.Button("Previous variant", GUILayout.Height(24f)))
            {
                ShowPreviousVariant();
            }

            if (GUILayout.Button("Next variant", GUILayout.Height(24f)))
            {
                ShowNextVariant();
            }

            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            GUILayout.Label(GetVariantLabelText(), EditorStyles.label, GUILayout.Width(280f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRotationRow()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(currentVariants.Count == 0);
            if (GUILayout.Button("Rotate 90° left", GUILayout.Height(24f)))
            {
                RotateLeft();
            }

            if (GUILayout.Button("Rotate 90° right", GUILayout.Height(24f)))
            {
                RotateRight();
            }

            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            GUILayout.Label("Rotation: " + (rotationSteps * 90) + "°", EditorStyles.label, GUILayout.Width(280f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStorageSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("JSON storage", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            storageFolder = EditorGUILayout.TextField("Folder", storageFolder);
            if (GUILayout.Button("Reset", GUILayout.Width(70f)))
            {
                storageFolder = DefaultStorageFolder;
                RefreshJsonFiles();
            }

            if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
            {
                RefreshJsonFiles();
            }

            if (GUILayout.Button("Reveal", GUILayout.Width(80f)))
            {
                RevealStorageFolder();
            }

            EditorGUILayout.EndHorizontal();

            if (jsonFileNames.Count == 0)
            {
                EditorGUILayout.HelpBox("No JSON files found in the storage folder.", MessageType.Warning);
            }
            else
            {
                int safeIndex = Mathf.Clamp(selectedJsonFileIndex, 0, jsonFileNames.Count - 1);
                int newIndex = EditorGUILayout.Popup("Existing file", safeIndex, jsonFileNames.ToArray());
                if (newIndex != selectedJsonFileIndex)
                {
                    selectedJsonFileIndex = newIndex;
                    saveFileName = jsonFileNames[selectedJsonFileIndex];
                }
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(jsonFileNames.Count == 0);
            if (GUILayout.Button("Load selected JSON", GUILayout.Height(24f)))
            {
                LoadSelectedJson();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            saveFileName = EditorGUILayout.TextField("Save file name", saveFileName);
            EditorGUILayout.BeginHorizontal();
            saveOffsetX = EditorGUILayout.IntField("Save offset X", saveOffsetX);
            saveOffsetY = EditorGUILayout.IntField("Save offset Y", saveOffsetY);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(currentVariants.Count == 0);
            if (GUILayout.Button("Save current variant to JSON", GUILayout.Height(24f)))
            {
                SaveCurrentVariant();
            }

            EditorGUI.EndDisabledGroup();
        }

        private void DrawGridArea()
        {
            if (currentVariants.Count == 0)
            {
                EditorGUILayout.HelpBox("No crossword generated or loaded yet.", MessageType.Info);
                return;
            }

            CrosswordResult result = currentVariants[currentVariantIndex];
            Dictionary<GridCoordinate, char> displayGrid = RotatedGrid(result.Grid);

            int minRow;
            int maxRow;
            int minCol;
            int maxCol;
            CrosswordGenerator.Bounds(displayGrid, out minRow, out maxRow, out minCol, out maxCol);
            int rows = maxRow - minRow + 1;
            int cols = maxCol - minCol + 1;

            int maxDimension = Mathf.Max(rows, cols, 1);
            int cellSize = Mathf.Clamp(820 / maxDimension, 22, 42);
            int fontSize = Mathf.Max(9, cellSize / 2);
            cellStyle.fontSize = fontSize;

            float width = cols * cellSize;
            float height = rows * cellSize;

            gridScroll = EditorGUILayout.BeginScrollView(gridScroll, GUILayout.ExpandHeight(true));
            Rect canvasRect = GUILayoutUtility.GetRect(width, height, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));

            EditorGUI.DrawRect(canvasRect, new Color32(0x25, 0x25, 0x25, 0xFF));

            Color letterBackground = Color.white;
            Color letterForeground = new Color32(0x11, 0x11, 0x11, 0xFF);
            Color emptyBackground = new Color32(0x25, 0x25, 0x25, 0xFF);

            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < cols; col++)
                {
                    GridCoordinate absolute = new GridCoordinate(minRow + row, minCol + col);
                    char letter;
                    bool hasLetter = displayGrid.TryGetValue(absolute, out letter);

                    Rect cellRect = new Rect(
                        canvasRect.x + col * cellSize,
                        canvasRect.y + row * cellSize,
                        cellSize,
                        cellSize
                    );

                    EditorGUI.DrawRect(cellRect, hasLetter ? letterBackground : emptyBackground);
                    Handles.color = new Color32(0x70, 0x70, 0x70, 0xFF);
                    Handles.DrawAAPolyLine(
                        1f,
                        new Vector3(cellRect.xMin, cellRect.yMin),
                        new Vector3(cellRect.xMax, cellRect.yMin),
                        new Vector3(cellRect.xMax, cellRect.yMax),
                        new Vector3(cellRect.xMin, cellRect.yMax),
                        new Vector3(cellRect.xMin, cellRect.yMin)
                    );

                    if (hasLetter)
                    {
                        Color oldColor = GUI.color;
                        GUI.color = letterForeground;
                        GUI.Label(cellRect, letter.ToString(), cellStyle);
                        GUI.color = oldColor;
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshJsonFiles()
        {
            string absoluteFolder = GetAbsoluteStorageFolder();
            string previous = GetSelectedJsonFileName();

            jsonFileNames.Clear();
            selectedJsonFileIndex = -1;

            if (string.IsNullOrWhiteSpace(absoluteFolder) || !Directory.Exists(absoluteFolder))
            {
                return;
            }

            string[] files = Directory.GetFiles(absoluteFolder, "*.json", SearchOption.TopDirectoryOnly);
            Array.Sort(files, CompareJsonFilePaths);
            for (int i = 0; i < files.Length; i++)
            {
                jsonFileNames.Add(Path.GetFileName(files[i]));
            }

            if (jsonFileNames.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrEmpty(previous))
            {
                selectedJsonFileIndex = jsonFileNames.FindIndex(name =>
                    string.Equals(name, previous, StringComparison.OrdinalIgnoreCase)
                );
            }

            if (selectedJsonFileIndex < 0)
            {
                selectedJsonFileIndex = 0;
            }

            if (string.IsNullOrWhiteSpace(saveFileName))
            {
                saveFileName = jsonFileNames[selectedJsonFileIndex];
            }
        }

        private void LoadSelectedJson()
        {
            string absolutePath = GetSelectedJsonAbsolutePath();
            if (string.IsNullOrWhiteSpace(absolutePath) || !File.Exists(absolutePath))
            {
                EditorUtility.DisplayDialog("Load failed", "Selected JSON file does not exist.", "OK");
                return;
            }

            try
            {
                string json = File.ReadAllText(absolutePath);
                CrosswordData data = JsonUtility.FromJson<CrosswordData>(json);
                if (data == null || data.CrosswordConfigs == null || data.CrosswordConfigs.Count == 0)
                {
                    EditorUtility.DisplayDialog("Load failed", "JSON does not contain crossword entries.", "OK");
                    return;
                }

                CrosswordResult result = BuildResultFromConfigs(data.CrosswordConfigs);
                if (result == null || result.Grid == null || result.Grid.Count == 0)
                {
                    EditorUtility.DisplayDialog("Load failed", "Could not parse crossword from JSON.", "OK");
                    return;
                }

                currentVariants = new List<CrosswordResult> { result };
                currentVariantIndex = 0;
                variantsTruncated = false;
                lastInputWordCount = result.UsedWords.Count;
                rotationSteps = 0;
                inputText = string.Join("\n", result.UsedWords.ToArray());
                ShowVariant(0);
                ShowNotification(new GUIContent("Crossword loaded from " + Path.GetFileName(absolutePath)));
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to load crossword JSON: " + ex);
                EditorUtility.DisplayDialog("Load failed", "Exception while reading JSON. See Console.", "OK");
            }
        }

        private void SaveCurrentVariant()
        {
            if (currentVariants.Count == 0)
            {
                EditorUtility.DisplayDialog("Save failed", "There is no variant to save.", "OK");
                return;
            }

            if (rotationSteps != 0)
            {
                bool continueSave = EditorUtility.DisplayDialog(
                    "Rotation notice",
                    "Save uses the original unrotated crossword coordinates.\n\nContinue?",
                    "Save",
                    "Cancel"
                );
                if (!continueSave)
                {
                    return;
                }
            }

            string normalizedFileName = NormalizeJsonFileName(saveFileName);
            if (string.IsNullOrWhiteSpace(normalizedFileName))
            {
                EditorUtility.DisplayDialog("Save failed", "Please provide a valid file name.", "OK");
                return;
            }

            string absoluteFolder = GetAbsoluteStorageFolder();
            if (string.IsNullOrWhiteSpace(absoluteFolder))
            {
                EditorUtility.DisplayDialog("Save failed", "Storage folder path is empty.", "OK");
                return;
            }

            try
            {
                Directory.CreateDirectory(absoluteFolder);
                string absolutePath = Path.Combine(absoluteFolder, normalizedFileName);

                CrosswordResult current = currentVariants[currentVariantIndex];
                CrosswordData data = BuildDataFromResult(current);
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(absolutePath, json);

                saveFileName = normalizedFileName;
                AssetDatabase.Refresh();
                RefreshJsonFiles();
                SelectJsonByName(normalizedFileName);
                ShowNotification(new GUIContent("Saved " + normalizedFileName));
            }
            catch (Exception ex)
            {
                Debug.LogError("Failed to save crossword JSON: " + ex);
                EditorUtility.DisplayDialog("Save failed", "Exception while writing JSON. See Console.", "OK");
            }
        }

        private CrosswordResult BuildResultFromConfigs(List<CrosswordConfig> configs)
        {
            Dictionary<GridCoordinate, char> grid = new Dictionary<GridCoordinate, char>();
            Dictionary<GridCoordinate, int> usage = new Dictionary<GridCoordinate, int>();
            List<Placement> placements = new List<Placement>();
            List<string> usedWords = new List<string>();

            for (int i = 0; i < configs.Count; i++)
            {
                CrosswordConfig config = configs[i];
                if (config == null || string.IsNullOrWhiteSpace(config.Answer))
                {
                    continue;
                }

                string word = config.Answer.Trim().ToUpperInvariant();
                bool horizontal = config.Direction != 1;
                int row = config.YPos;
                int col = config.XPos;
                AddWordToGridForLoadedConfig(grid, usage, word, row, col, horizontal);
                placements.Add(new Placement(word, row, col, horizontal));
                usedWords.Add(word);
            }

            if (grid.Count == 0)
            {
                return null;
            }

            int minRow;
            int maxRow;
            int minCol;
            int maxCol;
            CrosswordGenerator.Bounds(grid, out minRow, out maxRow, out minCol, out maxCol);

            int intersections = 0;
            foreach (KeyValuePair<GridCoordinate, int> pair in usage)
            {
                if (pair.Value > 1)
                {
                    intersections++;
                }
            }

            return new CrosswordResult(
                grid,
                placements,
                usedWords,
                new List<string>(),
                minRow,
                maxRow,
                minCol,
                maxCol,
                intersections
            );
        }

        private CrosswordData BuildDataFromResult(CrosswordResult result)
        {
            int minRow = result.MinRow;
            int minCol = result.MinCol;
            int rowShift = -minRow + saveOffsetY;
            int colShift = -minCol + saveOffsetX;

            CrosswordData data = new CrosswordData
            {
                CrosswordConfigs = new List<CrosswordConfig>()
            };

            for (int i = 0; i < result.Placements.Count; i++)
            {
                Placement placement = result.Placements[i];
                data.CrosswordConfigs.Add(
                    new CrosswordConfig
                    {
                        Answer = placement.Word.ToLowerInvariant(),
                        XPos = placement.Col + colShift,
                        YPos = placement.Row + rowShift,
                        Direction = placement.Horizontal ? 0 : 1
                    }
                );
            }

            return data;
        }

        private static void AddWordToGridForLoadedConfig(
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
                GridCoordinate coordinate = new GridCoordinate(cellRow, cellCol);
                char existingLetter;
                if (!grid.TryGetValue(coordinate, out existingLetter))
                {
                    grid[coordinate] = word[index];
                }
                else if (existingLetter != word[index])
                {
                    Debug.LogWarning(
                        "Crossword letter mismatch at " + coordinate.Col + "," + coordinate.Row +
                        ": " + existingLetter + " vs " + word[index]
                    );
                }

                int count;
                usage.TryGetValue(coordinate, out count);
                usage[coordinate] = count + 1;
            }
        }

        private string GetAbsoluteStorageFolder()
        {
            if (string.IsNullOrWhiteSpace(storageFolder))
            {
                return string.Empty;
            }

            if (Path.IsPathRooted(storageFolder))
            {
                return storageFolder;
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.GetFullPath(Path.Combine(projectRoot, storageFolder));
        }

        private void RevealStorageFolder()
        {
            string absoluteFolder = GetAbsoluteStorageFolder();
            if (string.IsNullOrWhiteSpace(absoluteFolder))
            {
                return;
            }

            Directory.CreateDirectory(absoluteFolder);
            EditorUtility.RevealInFinder(absoluteFolder);
        }

        private string GetSelectedJsonFileName()
        {
            if (selectedJsonFileIndex < 0 || selectedJsonFileIndex >= jsonFileNames.Count)
            {
                return string.Empty;
            }

            return jsonFileNames[selectedJsonFileIndex];
        }

        private string GetSelectedJsonAbsolutePath()
        {
            string fileName = GetSelectedJsonFileName();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return string.Empty;
            }

            string absoluteFolder = GetAbsoluteStorageFolder();
            if (string.IsNullOrWhiteSpace(absoluteFolder))
            {
                return string.Empty;
            }

            return Path.Combine(absoluteFolder, fileName);
        }

        private void SelectJsonByName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return;
            }

            int index = jsonFileNames.FindIndex(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                selectedJsonFileIndex = index;
            }
        }

        private static string NormalizeJsonFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string fileNameOnly = Path.GetFileName(value.Trim());
            if (string.IsNullOrWhiteSpace(fileNameOnly))
            {
                return string.Empty;
            }

            if (!fileNameOnly.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                fileNameOnly += ".json";
            }

            return fileNameOnly;
        }

        private static int CompareJsonFilePaths(string leftPath, string rightPath)
        {
            string leftName = Path.GetFileNameWithoutExtension(leftPath);
            string rightName = Path.GetFileNameWithoutExtension(rightPath);
            int leftNumber = ExtractTrailingNumber(leftName);
            int rightNumber = ExtractTrailingNumber(rightName);
            int numberCompare = leftNumber.CompareTo(rightNumber);
            if (numberCompare != 0)
            {
                return numberCompare;
            }

            return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return int.MaxValue;
            }

            int end = value.Length - 1;
            while (end >= 0 && char.IsDigit(value[end]))
            {
                end--;
            }

            if (end == value.Length - 1)
            {
                return int.MaxValue;
            }

            string numericPart = value.Substring(end + 1);
            int parsed;
            if (int.TryParse(numericPart, out parsed))
            {
                return parsed;
            }

            return int.MaxValue;
        }

        private void OnGenerateClicked()
        {
            List<string> words = CrosswordWordParser.ParseWords(inputText);
            if (words.Count < 2)
            {
                EditorUtility.DisplayDialog(
                    "Not enough data",
                    "Please enter at least two words with 2+ characters.",
                    "OK"
                );
                return;
            }

            CrosswordGenerator generator = new CrosswordGenerator(maxVariants, maxNodes);
            CrosswordVariants variantsResult = generator.GenerateVariants(words);
            if (variantsResult.Variants.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "Generation failed",
                    "Could not generate crossword.",
                    "OK"
                );
                return;
            }

            currentVariants = variantsResult.Variants;
            currentVariantIndex = 0;
            lastInputWordCount = words.Count;
            variantsTruncated = variantsResult.Truncated;
            rotationSteps = 0;
            ShowVariant(currentVariantIndex);
        }

        private void ShowPreviousVariant()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            ShowVariant(currentVariantIndex - 1);
        }

        private void ShowNextVariant()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            ShowVariant(currentVariantIndex + 1);
        }

        private void RotateLeft()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            rotationSteps = Mod(rotationSteps - 1, 4);
            ShowVariant(currentVariantIndex);
        }

        private void RotateRight()
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
                statusText = "Ready.";
                Repaint();
                return;
            }

            int total = currentVariants.Count;
            currentVariantIndex = Mod(index, total);
            CrosswordResult result = currentVariants[currentVariantIndex];
            UpdateStatusLabel(result);
            Repaint();
        }

        private void UpdateStatusLabel(CrosswordResult result)
        {
            int totalVariants = currentVariants.Count;
            string suffix = variantsTruncated ? "+" : string.Empty;
            string limitNote = variantsTruncated ? " Search limit reached." : string.Empty;
            string skipped = PreviewWords(result.SkippedWords);
            string skippedText = string.IsNullOrEmpty(skipped) ? string.Empty : " Skipped: " + skipped + ".";
            int rotationAngle = rotationSteps * 90;

            statusText =
                "Unique variants found: " + totalVariants + suffix + ". " +
                "Placed " + result.UsedWords.Count + " of " + lastInputWordCount + " words. " +
                "Intersections: " + result.Intersections + ". " +
                "Rotation: " + rotationAngle + "°. " +
                skippedText + limitNote;
        }

        private string GetVariantLabelText()
        {
            int total = currentVariants.Count;
            if (total == 0)
            {
                return "Variant: -/-";
            }

            string suffix = variantsTruncated ? "+" : string.Empty;
            return "Variant " + (currentVariantIndex + 1) + "/" + total + suffix + " (total: " + total + suffix + ")";
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

        private void EnsureStyles()
        {
            if (statusStyle == null)
            {
                statusStyle = new GUIStyle(EditorStyles.wordWrappedLabel);
                statusStyle.richText = false;
            }

            if (cellStyle == null)
            {
                cellStyle = new GUIStyle(EditorStyles.boldLabel);
                cellStyle.alignment = TextAnchor.MiddleCenter;
            }
        }
    }
}
