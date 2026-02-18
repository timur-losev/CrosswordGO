using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace QtCrossword.EditorTools
{
    public sealed class CrosswordToolWindow : EditorWindow
    {
        private const string DefaultStorageFolder = "Assets/WordChef/Resources/World_0/SubWorld_0";
        private const string EnglishWordsFolder = "Assets/WordChef/10000 English Words";
        private const double HiddenWordSearchTimeoutSeconds = 0.2d;

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
        private string uniqueLettersText = "Unique letters: -";
        private string hiddenWordsText = "Hidden words: -";
        private string hiddenWordInput = string.Empty;
        private string storageFolder = DefaultStorageFolder;
        private List<string> jsonFileNames = new List<string>();
        private int selectedJsonFileIndex = -1;
        private string saveFileName = "LevelCW_0.json";
        private int saveOffsetX;
        private int saveOffsetY;
        private int requestedWords3;
        private int requestedWords4;
        private int requestedWords5;
        private int requestedWords6;
        private int requestedWords7;
        private int targetConstraintUniqueLetters = 12;
        private int targetConstraintUniqueLettersSpread;
        private bool discardUsedWords;
        private bool inputOnlyMode;
        private string constraintStatusText = "Constraint mode: disabled (using input text).";
        private ConstraintWordSelectionResult lastConstraintResult;
        private bool wordPoolsLoaded;
        private bool hiddenWordCandidatesBuilt;
        private readonly List<HiddenWordCandidate> hiddenWordCandidates = new List<HiddenWordCandidate>();
        private readonly Dictionary<string, List<string>> hiddenWordsByVariantKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> hiddenWordSearchCursorByVariantKey = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<int, List<string>> wordPoolsByLength = new Dictionary<int, List<string>>();
        private readonly Dictionary<int, string> wordListFileNames = new Dictionary<int, string>
        {
            { 3, "3_letters.txt" },
            { 4, "4_letters.txt" },
            { 5, "5_letters.txt" },
            { 6, "6_letters.txt" },
            { 7, "7_letters.txt" }
        };

        private GUIStyle statusStyle;
        private GUIStyle cellStyle;

        private struct HiddenWordCandidate
        {
            public string Word;
            public uint LetterMask;
            public int Length;
            public int FrequencyRank;
        }

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
            SelectLevelZeroByDefault();
            if (jsonFileNames.Count > 0)
            {
                LoadSelectedJson();
            }
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawDebugToolbar();
            DrawStorageSection();

            inputOnlyMode = EditorGUILayout.ToggleLeft("Read words only from input text", inputOnlyMode);
            EditorGUILayout.LabelField("Enter words (one per line or separated by commas/spaces):", EditorStyles.boldLabel);
            inputText = EditorGUILayout.TextArea(inputText, GUILayout.MinHeight(120f));

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            maxVariants = Mathf.Max(1, EditorGUILayout.IntField("Max Variants", maxVariants));
            maxNodes = Mathf.Max(1, EditorGUILayout.IntField("Max Nodes", maxNodes));
            EditorGUILayout.EndHorizontal();
            DrawConstraintSection();

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Generate crossword", GUILayout.Height(28f)))
            {
                OnGenerateClicked();
            }
            EditorGUI.BeginDisabledGroup(currentVariants.Count == 0);
            if (GUILayout.Button("Add hidden word from unique letters", GUILayout.Height(28f)))
            {
                OnAddHiddenWordClicked();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            DrawNavigationRow();
            DrawRotationRow();

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(statusText, statusStyle);
            EditorGUILayout.LabelField(uniqueLettersText, statusStyle);
            DrawHiddenWordsStrip();
            EditorGUILayout.LabelField(constraintStatusText, statusStyle);
            EditorGUILayout.Space(8f);

            DrawGridArea();
        }

        private void DrawDebugToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect buttonRect = GUILayoutUtility.GetRect(new GUIContent("Debug"), EditorStyles.toolbarDropDown, GUILayout.Width(90f));
            if (GUI.Button(buttonRect, "Debug", EditorStyles.toolbarDropDown))
            {
                ShowDebugMenu(buttonRect);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void ShowDebugMenu(Rect buttonRect)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Erase Save"), false, OnEraseSaveDebugClicked);
            menu.DropDown(buttonRect);
        }

        private void OnEraseSaveDebugClicked()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Erase Save",
                "Delete all local saves and runtime caches?\n\nThis action cannot be undone.",
                "Erase",
                "Cancel"
            );
            if (!confirmed)
            {
                return;
            }

            try
            {
                SaveDebugActions.EraseSaveOrThrow();

                ResetToolCachesAfterErase();
                statusText = "Debug: saves and caches erased.";
                ShowNotification(new GUIContent("Erase Save completed"));
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogError("Erase Save failed: " + ex);
                EditorUtility.DisplayDialog("Erase Save failed", "See Console for details.", "OK");
            }
        }

        private void ResetToolCachesAfterErase()
        {
            hiddenWordsByVariantKey.Clear();
            hiddenWordSearchCursorByVariantKey.Clear();
            hiddenWordCandidates.Clear();
            hiddenWordCandidatesBuilt = false;
            wordPoolsLoaded = false;
            currentVariants.Clear();
            currentVariantIndex = 0;
            variantsTruncated = false;
            rotationSteps = 0;
            hiddenWordsText = "Hidden words: -";
            uniqueLettersText = "Unique letters: -";
            constraintStatusText = "Constraint mode: disabled (using input text).";
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

        private void DrawConstraintSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Word Constraints (optional)", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "If any count is > 0, the tool builds words from 3-7 letter lists and ignores manual input text.",
                MessageType.None
            );

            EditorGUILayout.BeginHorizontal();
            requestedWords3 = Mathf.Max(0, EditorGUILayout.IntField("3 letters", requestedWords3));
            requestedWords4 = Mathf.Max(0, EditorGUILayout.IntField("4 letters", requestedWords4));
            requestedWords5 = Mathf.Max(0, EditorGUILayout.IntField("5 letters", requestedWords5));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            requestedWords6 = Mathf.Max(0, EditorGUILayout.IntField("6 letters", requestedWords6));
            requestedWords7 = Mathf.Max(0, EditorGUILayout.IntField("7 letters", requestedWords7));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            targetConstraintUniqueLetters = Mathf.Clamp(
                EditorGUILayout.IntField("Target unique letters", targetConstraintUniqueLetters),
                0,
                26
            );
            targetConstraintUniqueLettersSpread = Mathf.Clamp(
                EditorGUILayout.IntField("± spread", targetConstraintUniqueLettersSpread),
                0,
                26
            );
            EditorGUILayout.EndHorizontal();

            discardUsedWords = EditorGUILayout.ToggleLeft("Discard used words (scan current storage folder JSON)", discardUsedWords);
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
                    LoadSelectedJson();
                }
            }

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

        private bool HasConstraintRequest()
        {
            return TotalConstraintWordsRequested() > 0;
        }

        private int TotalConstraintWordsRequested()
        {
            return requestedWords3 + requestedWords4 + requestedWords5 + requestedWords6 + requestedWords7;
        }

        private void EnsureWordPoolsLoaded()
        {
            if (wordPoolsLoaded)
            {
                return;
            }

            wordPoolsByLength.Clear();
            hiddenWordCandidates.Clear();
            hiddenWordCandidatesBuilt = false;
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            foreach (KeyValuePair<int, string> pair in wordListFileNames)
            {
                int length = pair.Key;
                string relativeFilePath = Path.Combine(EnglishWordsFolder, pair.Value);
                string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, relativeFilePath));
                List<string> words = new List<string>();

                if (File.Exists(absolutePath))
                {
                    string fileContent = File.ReadAllText(absolutePath);
                    List<string> parsedWords = CrosswordWordParser.ParseWords(fileContent);
                    for (int i = 0; i < parsedWords.Count; i++)
                    {
                        string word = parsedWords[i];
                        if (string.IsNullOrWhiteSpace(word) || word.Length != length)
                        {
                            continue;
                        }

                        words.Add(word.Trim().ToUpperInvariant());
                    }
                }
                else
                {
                    Debug.LogWarning("Word list not found: " + absolutePath);
                }

                wordPoolsByLength[length] = words;
            }

            wordPoolsLoaded = true;
        }

        private void BuildHiddenWordCandidatesIfNeeded()
        {
            if (hiddenWordCandidatesBuilt)
            {
                return;
            }

            hiddenWordCandidates.Clear();
            foreach (KeyValuePair<int, List<string>> pool in wordPoolsByLength)
            {
                List<string> words = pool.Value;
                if (words == null)
                {
                    continue;
                }

                for (int i = 0; i < words.Count; i++)
                {
                    string word = words[i];
                    uint mask;
                    if (!TryBuildLetterMask(word, out mask))
                    {
                        continue;
                    }

                    hiddenWordCandidates.Add(new HiddenWordCandidate
                    {
                        Word = word,
                        LetterMask = mask,
                        Length = word.Length,
                        FrequencyRank = i
                    });
                }
            }

            hiddenWordCandidates.Sort((left, right) =>
            {
                int byFrequency = left.FrequencyRank.CompareTo(right.FrequencyRank);
                if (byFrequency != 0)
                {
                    return byFrequency;
                }

                int byLength = right.Length.CompareTo(left.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                return string.Compare(left.Word, right.Word, StringComparison.Ordinal);
            });

            hiddenWordCandidatesBuilt = true;
        }

        private void OnAddHiddenWordClicked()
        {
            if (currentVariants.Count == 0)
            {
                return;
            }

            EnsureWordPoolsLoaded();
            BuildHiddenWordCandidatesIfNeeded();

            CrosswordResult result = currentVariants[currentVariantIndex];
            uint availableLettersMask = BuildUniqueLetterMaskFromGrid(result.Grid);
            if (availableLettersMask == 0)
            {
                EditorUtility.DisplayDialog("Hidden words", "No letters found in current crossword.", "OK");
                return;
            }

            List<string> hiddenForVariant = GetHiddenWordsForVariant(result);
            HashSet<string> blockedWords = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < result.UsedWords.Count; i++)
            {
                blockedWords.Add(result.UsedWords[i].Trim().ToUpperInvariant());
            }
            for (int i = 0; i < hiddenForVariant.Count; i++)
            {
                blockedWords.Add(hiddenForVariant[i].Trim().ToUpperInvariant());
            }

            int candidateCount = hiddenWordCandidates.Count;
            if (candidateCount == 0)
            {
                EditorUtility.DisplayDialog("Hidden words", "No candidate words loaded.", "OK");
                return;
            }

            int currentIndex = GetHiddenWordSearchStartIndex(result, candidateCount);
            int checkedCount = 0;
            double deadline = EditorApplication.timeSinceStartup + HiddenWordSearchTimeoutSeconds;

            while (checkedCount < candidateCount)
            {
                if (EditorApplication.timeSinceStartup >= deadline)
                {
                    SetHiddenWordSearchStartIndex(result, currentIndex, candidateCount);
                    ShowNotification(new GUIContent("Hidden word search timed out"));
                    return;
                }

                HiddenWordCandidate candidate = hiddenWordCandidates[currentIndex];
                if (blockedWords.Contains(candidate.Word))
                {
                    currentIndex = (currentIndex + 1) % candidateCount;
                    checkedCount++;
                    continue;
                }

                if ((candidate.LetterMask & ~availableLettersMask) != 0u)
                {
                    currentIndex = (currentIndex + 1) % candidateCount;
                    checkedCount++;
                    continue;
                }

                hiddenForVariant.Add(candidate.Word);
                hiddenForVariant.Sort(StringComparer.Ordinal);
                UpdateHiddenWordsLabel(result);
                SetHiddenWordSearchStartIndex(result, (currentIndex + 1) % candidateCount, candidateCount);
                ShowNotification(new GUIContent("Hidden word added: " + candidate.Word.ToLowerInvariant()));
                Repaint();
                return;

                // unreachable by design
            }

            SetHiddenWordSearchStartIndex(result, 0, candidateCount);

            EditorUtility.DisplayDialog(
                "Hidden words",
                "No hidden word was found using current unique letters.",
                "OK"
            );
        }

        private List<string> GetHiddenWordsForVariant(CrosswordResult result)
        {
            string key = GetVariantKey(result);
            if (string.IsNullOrEmpty(key))
            {
                return new List<string>();
            }

            List<string> words;
            if (!hiddenWordsByVariantKey.TryGetValue(key, out words))
            {
                words = new List<string>();
                hiddenWordsByVariantKey[key] = words;
            }

            return words;
        }

        private void SetHiddenWordsForVariant(CrosswordResult result, List<string> words)
        {
            string key = GetVariantKey(result);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            hiddenWordsByVariantKey[key] = NormalizeHiddenWords(words);
        }

        private static string GetVariantKey(CrosswordResult result)
        {
            if (result == null || result.Grid == null)
            {
                return string.Empty;
            }

            return CrosswordGenerator.GridSignature(result.Grid);
        }

        private int GetHiddenWordSearchStartIndex(CrosswordResult result, int candidateCount)
        {
            if (candidateCount <= 0)
            {
                return 0;
            }

            string key = GetVariantKey(result);
            if (string.IsNullOrEmpty(key))
            {
                return 0;
            }

            int savedIndex;
            if (!hiddenWordSearchCursorByVariantKey.TryGetValue(key, out savedIndex))
            {
                return 0;
            }

            if (savedIndex < 0 || savedIndex >= candidateCount)
            {
                return 0;
            }

            return savedIndex;
        }

        private void SetHiddenWordSearchStartIndex(CrosswordResult result, int nextIndex, int candidateCount)
        {
            if (candidateCount <= 0)
            {
                return;
            }

            string key = GetVariantKey(result);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            int normalizedIndex = nextIndex;
            if (normalizedIndex < 0)
            {
                normalizedIndex = 0;
            }
            if (normalizedIndex >= candidateCount)
            {
                normalizedIndex %= candidateCount;
            }

            hiddenWordSearchCursorByVariantKey[key] = normalizedIndex;
        }

        private static List<string> NormalizeHiddenWords(IEnumerable<string> words)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> normalized = new List<string>();
            if (words == null)
            {
                return normalized;
            }

            foreach (string word in words)
            {
                if (string.IsNullOrWhiteSpace(word))
                {
                    continue;
                }

                string normalizedWord = word.Trim().ToUpperInvariant();
                if (normalizedWord.Length < 2)
                {
                    continue;
                }

                if (seen.Add(normalizedWord))
                {
                    normalized.Add(normalizedWord);
                }
            }

            normalized.Sort(StringComparer.Ordinal);
            return normalized;
        }

        private void UpdateHiddenWordsLabel(CrosswordResult result)
        {
            if (result == null || result.Grid == null || result.Grid.Count == 0)
            {
                hiddenWordsText = "Hidden words: -";
                return;
            }

            List<string> hiddenWords = GetHiddenWordsForVariant(result);
            if (hiddenWords == null || hiddenWords.Count == 0)
            {
                hiddenWordsText = "Hidden words: -";
                return;
            }

            List<string> displayWords = new List<string>(hiddenWords.Count);
            for (int i = 0; i < hiddenWords.Count; i++)
            {
                displayWords.Add(hiddenWords[i].ToLowerInvariant());
            }

            hiddenWordsText = "Hidden words: " + string.Join(", ", displayWords.ToArray());
        }

        private void DrawHiddenWordsStrip()
        {
            if (currentVariants.Count == 0)
            {
                EditorGUILayout.LabelField(hiddenWordsText, statusStyle);
                return;
            }

            CrosswordResult result = currentVariants[currentVariantIndex];
            List<string> hiddenWords = GetHiddenWordsForVariant(result);

            int removeIndex = -1;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Hidden words:", GUILayout.Width(90f));
            hiddenWordInput = EditorGUILayout.TextField(hiddenWordInput, GUILayout.Width(140f));
            if (GUILayout.Button("+", EditorStyles.miniButton, GUILayout.Width(18f), GUILayout.Height(16f)))
            {
                TryAddManualHiddenWord(result);
            }

            if (hiddenWords == null || hiddenWords.Count == 0)
            {
                GUILayout.Label("-", statusStyle, GUILayout.ExpandWidth(false));
            }
            else
            {
                for (int i = 0; i < hiddenWords.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal(GUI.skin.box, GUILayout.ExpandWidth(false));
                    GUILayout.Label(hiddenWords[i].ToLowerInvariant(), GUILayout.ExpandWidth(false));
                    if (GUILayout.Button("-", EditorStyles.miniButton, GUILayout.Width(18f), GUILayout.Height(16f)))
                    {
                        removeIndex = i;
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (removeIndex >= 0 && removeIndex < hiddenWords.Count)
            {
                hiddenWords.RemoveAt(removeIndex);
                UpdateHiddenWordsLabel(result);
                Repaint();
            }
        }

        private void TryAddManualHiddenWord(CrosswordResult result)
        {
            if (result == null || result.Grid == null || result.Grid.Count == 0)
            {
                return;
            }

            string normalized = string.IsNullOrWhiteSpace(hiddenWordInput)
                ? string.Empty
                : hiddenWordInput.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(normalized))
            {
                EditorUtility.DisplayDialog("Hidden words", "Enter a word first.", "OK");
                return;
            }

            if (normalized.Length < 2)
            {
                EditorUtility.DisplayDialog("Hidden words", "Hidden word must contain at least 2 letters.", "OK");
                return;
            }

            uint wordMask;
            if (!TryBuildLetterMask(normalized, out wordMask))
            {
                EditorUtility.DisplayDialog(
                    "Hidden words",
                    "Use A-Z letters only and avoid repeating letters.",
                    "OK"
                );
                return;
            }

            uint availableLettersMask = BuildUniqueLetterMaskFromGrid(result.Grid);
            if ((wordMask & ~availableLettersMask) != 0u)
            {
                EditorUtility.DisplayDialog(
                    "Hidden words",
                    "The word cannot be composed from the current crossword unique letters.",
                    "OK"
                );
                return;
            }

            List<string> hiddenWords = GetHiddenWordsForVariant(result);
            if (hiddenWords.Contains(normalized))
            {
                return;
            }

            for (int i = 0; i < result.UsedWords.Count; i++)
            {
                string usedWord = result.UsedWords[i];
                if (string.Equals(usedWord, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    EditorUtility.DisplayDialog(
                        "Hidden words",
                        "This word already exists in the crossword.",
                        "OK"
                    );
                    return;
                }
            }

            hiddenWords.Add(normalized);
            hiddenWords.Sort(StringComparer.Ordinal);
            hiddenWordInput = string.Empty;
            UpdateHiddenWordsLabel(result);
            Repaint();
        }

        private static bool TryBuildLetterMask(string word, out uint mask)
        {
            mask = 0u;
            if (string.IsNullOrWhiteSpace(word))
            {
                return false;
            }

            for (int i = 0; i < word.Length; i++)
            {
                char c = char.ToUpperInvariant(word[i]);
                if (c < 'A' || c > 'Z')
                {
                    return false;
                }

                uint bit = 1u << (c - 'A');
                if ((mask & bit) != 0u)
                {
                    return false;
                }

                mask |= bit;
            }

            return true;
        }

        private static uint BuildUniqueLetterMaskFromGrid(Dictionary<GridCoordinate, char> grid)
        {
            uint mask = 0u;
            if (grid == null)
            {
                return mask;
            }

            foreach (KeyValuePair<GridCoordinate, char> pair in grid)
            {
                char c = char.ToUpperInvariant(pair.Value);
                if (c < 'A' || c > 'Z')
                {
                    continue;
                }

                mask |= 1u << (c - 'A');
            }

            return mask;
        }

        private HashSet<string> CollectUsedWordsFromStorageFolder()
        {
            HashSet<string> usedWords = new HashSet<string>(StringComparer.Ordinal);
            string absoluteFolder = GetAbsoluteStorageFolder();
            if (string.IsNullOrWhiteSpace(absoluteFolder) || !Directory.Exists(absoluteFolder))
            {
                return usedWords;
            }

            string[] files = Directory.GetFiles(absoluteFolder, "*.json", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    CrosswordData data = JsonUtility.FromJson<CrosswordData>(json);
                    if (data == null || data.CrosswordConfigs == null)
                    {
                        continue;
                    }

                    for (int j = 0; j < data.CrosswordConfigs.Count; j++)
                    {
                        CrosswordConfig config = data.CrosswordConfigs[j];
                        if (config == null || string.IsNullOrWhiteSpace(config.Answer))
                        {
                            continue;
                        }

                        usedWords.Add(config.Answer.Trim().ToUpperInvariant());
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("Skipping unreadable JSON file: " + files[i] + " (" + ex.Message + ")");
                }
            }

            return usedWords;
        }

        private ConstraintWordSelectionResult BuildConstraintSelection()
        {
            EnsureWordPoolsLoaded();
            HashSet<string> usedWordsFromStorage = CollectUsedWordsFromStorageFolder();

            ConstraintWordSelectionRequest request = new ConstraintWordSelectionRequest
            {
                Count3 = requestedWords3,
                Count4 = requestedWords4,
                Count5 = requestedWords5,
                Count6 = requestedWords6,
                Count7 = requestedWords7,
                TargetUniqueLetters = targetConstraintUniqueLetters,
                TargetUniqueLettersSpread = targetConstraintUniqueLettersSpread,
                MaxSearchNodes = Math.Max(5000, maxNodes),
                MaxBranching = 40,
                WordPoolsByLength = wordPoolsByLength,
                PreviouslyUsedWords = usedWordsFromStorage,
                ExcludedWords = discardUsedWords ? usedWordsFromStorage : new HashSet<string>(StringComparer.Ordinal)
            };

            return ConstraintWordPicker.SelectWords(request);
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

        private void SelectLevelZeroByDefault()
        {
            if (jsonFileNames.Count == 0)
            {
                return;
            }

            int levelZeroIndex = jsonFileNames.FindIndex(name =>
                string.Equals(name, "LevelCW_0.json", StringComparison.OrdinalIgnoreCase)
            );

            if (levelZeroIndex < 0)
            {
                levelZeroIndex = jsonFileNames.FindIndex(name =>
                {
                    string nameWithoutExtension = Path.GetFileNameWithoutExtension(name);
                    return ExtractTrailingNumber(nameWithoutExtension) == 0;
                });
            }

            if (levelZeroIndex < 0)
            {
                levelZeroIndex = 0;
            }

            selectedJsonFileIndex = levelZeroIndex;
            saveFileName = jsonFileNames[selectedJsonFileIndex];
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
                hiddenWordsByVariantKey.Clear();
                hiddenWordSearchCursorByVariantKey.Clear();
                SetHiddenWordsForVariant(result, data.HiddenWords);
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
                CrosswordConfigs = new List<CrosswordConfig>(),
                HiddenWords = new List<string>()
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

            List<string> hiddenForVariant = GetHiddenWordsForVariant(result);
            for (int i = 0; i < hiddenForVariant.Count; i++)
            {
                data.HiddenWords.Add(hiddenForVariant[i].ToLowerInvariant());
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
            List<string> words;
            lastConstraintResult = null;
            constraintStatusText = "Constraint mode: disabled (using input text).";

            if (inputOnlyMode)
            {
                words = CrosswordWordParser.ParseWords(inputText);
                constraintStatusText = "Input-only mode: constraint section ignored.";
            }
            else if (HasConstraintRequest())
            {
                ConstraintWordSelectionResult constraintResult = BuildConstraintSelection();
                lastConstraintResult = constraintResult;
                if (!constraintResult.Success)
                {
                    constraintStatusText = "Constraint mode failed: " + constraintResult.Diagnostics;
                    EditorUtility.DisplayDialog(
                        "Constraint selection failed",
                        constraintResult.Diagnostics,
                        "OK"
                    );
                    return;
                }

                words = new List<string>(constraintResult.SelectedWords);
                inputText = string.Join("\n", words.ToArray());
                string targetText = constraintResult.TargetUniqueLetters.ToString();
                if (constraintResult.TargetUniqueLettersSpread > 0)
                {
                    targetText = targetText + " ±" + constraintResult.TargetUniqueLettersSpread;
                }
                constraintStatusText =
                    "Constraint mode: target " + targetText +
                    ", achieved " + constraintResult.AchievedUniqueLetters +
                    ", range delta " + constraintResult.Delta +
                    ", center delta " + constraintResult.CenterDelta +
                    ", nodes " + constraintResult.VisitedNodes +
                    (discardUsedWords ? ", discard used words ON." : ", discard used words OFF.");
                if (constraintResult.SearchTruncated)
                {
                    constraintStatusText += " Search truncated.";
                }
            }
            else
            {
                words = CrosswordWordParser.ParseWords(inputText);
            }

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
            hiddenWordsByVariantKey.Clear();
            hiddenWordSearchCursorByVariantKey.Clear();
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
                uniqueLettersText = "Unique letters: -";
                hiddenWordsText = "Hidden words: -";
                Repaint();
                return;
            }

            int total = currentVariants.Count;
            currentVariantIndex = Mod(index, total);
            CrosswordResult result = currentVariants[currentVariantIndex];
            UpdateStatusLabel(result);
            UpdateUniqueLettersLabel(result);
            UpdateHiddenWordsLabel(result);
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

        private void UpdateUniqueLettersLabel(CrosswordResult result)
        {
            if (result == null || result.Grid == null || result.Grid.Count == 0)
            {
                uniqueLettersText = "Unique letters: -";
                return;
            }

            Dictionary<GridCoordinate, char> displayGrid = RotatedGrid(result.Grid);
            if (displayGrid == null || displayGrid.Count == 0)
            {
                uniqueLettersText = "Unique letters: -";
                return;
            }

            HashSet<char> seen = new HashSet<char>();
            foreach (KeyValuePair<GridCoordinate, char> pair in displayGrid)
            {
                if (pair.Value == '\0') continue;
                seen.Add(char.ToUpperInvariant(pair.Value));
            }

            if (seen.Count == 0)
            {
                uniqueLettersText = "Unique letters: -";
                return;
            }

            List<char> letters = new List<char>(seen);
            letters.Sort();

            StringBuilder sb = new StringBuilder("Unique letters: ");
            for (int i = 0; i < letters.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(' ');
                }
                sb.Append(letters[i]);
            }

            uniqueLettersText = sb.ToString();
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
