using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Text;

public class WordRegion : MonoBehaviour
{
    public TextPreview textPreview;
    public Compliment compliment;
    private List<LineWord> lines = new List<LineWord>();
    private List<string> validWords = new List<string>();
    private Dictionary<Vector2Int, Cell> cellMap = new Dictionary<Vector2Int, Cell>();
    private List<string> allWordsUpper = new List<string>();

    private GameLevel gameLevel;
    private float cellSize;
    private float step;
    private float totalWidth;
    private float totalHeight;
    private int minX;
    private int minY;
    private int gridWidth;
    private int gridHeight;
    private bool hasLongLine;

    private RectTransform rt;
    public static WordRegion instance;

    private void Awake()
    {
        instance = this;
        rt = GetComponent<RectTransform>();
    }

    public static string GetCrosswordConfigPath(int world, int subWorld, int level)
    {
        return $"World_{world}/SubWorld_{subWorld}/LevelCW_{level}";
    }

    public void Load(GameLevel gameLevel)
    {
        this.gameLevel = gameLevel;
        lines.Clear();
        cellMap.Clear();

        // Очистка предыдущих элементов на канвасе, если была перезагрузка
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }

        var wordList = CUtils.BuildListFromString<string>(this.gameLevel.answers);
        validWords = CUtils.BuildListFromString<string>(this.gameLevel.validWords);
        BuildAllWords(wordList);

        // Загрузка кроссворда из конфигурации
        string configFilePath = GetCrosswordConfigPath(GameState.currentWorld, GameState.currentSubWorld, GameState.currentLevel);
        var crosswordConfigs = CrosswordLoader.LoadCrosswordConfig(configFilePath);

        if (crosswordConfigs == null || crosswordConfigs.Count == 0)
        {
            // Fallback на первый уровень, если ресурс для текущего не найден
            string fallbackPath = GetCrosswordConfigPath(0, 0, 0);
            if (fallbackPath != configFilePath)
            {
                crosswordConfigs = CrosswordLoader.LoadCrosswordConfig(fallbackPath);
                if (crosswordConfigs != null && crosswordConfigs.Count > 0)
                {
                    Debug.LogWarning($"Crossword config not found for {configFilePath}, using fallback {fallbackPath}");
                }
            }

            if (crosswordConfigs == null || crosswordConfigs.Count == 0)
            {
                Debug.LogError($"Failed to load crossword configuration for {configFilePath}");
                return;
            }
        }

        // Определение размера сетки на основе максимальных значений XPos и YPos
        minX = crosswordConfigs.Min(c => c.XPos);
        minY = crosswordConfigs.Min(c => c.YPos);
        int maxX = crosswordConfigs.Max(c => c.XPos);
        int maxY = crosswordConfigs.Max(c => c.YPos);
        gridWidth = maxX - minX + 1;
        gridHeight = maxY - minY + 1;

        // Задание размера ячейки на основе размеров сетки и размеров RectTransform
        cellSize = CalculateCellSize(gridWidth, gridHeight);
        step = cellSize * (1f + Const.CELL_GAP_COEF);
        totalWidth = cellSize + (gridWidth - 1) * step;
        totalHeight = cellSize + (gridHeight - 1) * step;

        string[] levelProgress = GetLevelProgress();
        bool useProgress = levelProgress.Length != 0 && CheckLevelProgress(levelProgress, wordList);

        int lineIndex = 0;
        foreach (var config in crosswordConfigs)
        {
            LineWord line = Instantiate(MonoUtils.instance.lineWord);
            line.answer = config.Answer.ToUpper();
            bool isVertical = config.Direction == 1;


            List<Cell> lineCells = BuildLineCells(config, isVertical);
            line.Initialize(isVertical, cellSize, lineCells);

            if (useProgress)
            {
                line.SetProgress(levelProgress[lineIndex]);
            }

            var lineRect = line.GetComponent<RectTransform>();
            lineRect.SetParent(transform, false);
            lineRect.localScale = Vector3.one;
            lineRect.anchoredPosition = GetWordAnchorPosition(config, isVertical);

            lines.Add(line);
            lineIndex++;
        }
    }

    private List<Cell> BuildLineCells(CrosswordConfig config, bool isVertical)
    {
        List<Cell> lineCells = new List<Cell>();
        int length = config.Answer.Length;
        Vector2Int start = new Vector2Int(config.XPos - minX, config.YPos - minY);

        for (int i = 0; i < length; i++)
        {
            int x = isVertical ? start.x : start.x + i;
            int y = isVertical ? start.y + i : start.y;
            Vector2Int pos = new Vector2Int(x, y);

            Cell cell;
            if (!cellMap.TryGetValue(pos, out cell))
            {
                cell = Instantiate(MonoUtils.instance.cell);
                cell.letter = config.Answer[i].ToString().ToUpper();
                cell.letterText.transform.localScale = Vector3.one * (cellSize / 80f);
                cell.letterText.fontSize = ConfigController.Config.fontSizeInCellMainScene;

                var cellRect = cell.GetComponent<RectTransform>();
                cellRect.SetParent(transform, false);
                cellRect.sizeDelta = new Vector2(cellSize, cellSize);
                cellRect.localScale = Vector3.one;
                cellRect.anchorMin = cellRect.anchorMax = new Vector2(0.5f, 0.5f);
                cellRect.pivot = new Vector2(0.5f, 0.5f);
                cellRect.anchoredPosition = GetCellPosition(pos);

                cellMap.Add(pos, cell);
            }
            else
            {
                // Если пересечение, убедимся что буквы совпадают
                string currentLetter = cell.letter.ToUpper();
                string newLetter = config.Answer[i].ToString().ToUpper();
                if (currentLetter != newLetter)
                {
                    Debug.LogWarningFormat("Crossword letter mismatch at {0},{1}: {2} vs {3}", pos.x, pos.y, currentLetter, newLetter);
                }
            }

            lineCells.Add(cell);
        }

        return lineCells;
    }

    private Vector2 GetWordAnchorPosition(CrosswordConfig config, bool isVertical)
    {
        Vector2Int pos = new Vector2Int(config.XPos - minX, config.YPos - minY);
        // Привязываем якорь слова к первой его букве
        return GetCellPosition(pos);
    }

    private float CalculateCellSize(int gridWidth, int gridHeight)
    {
        float cellWidth = rt.rect.width / (gridWidth + Const.CELL_GAP_COEF * (gridWidth - 1));
        float cellHeight = rt.rect.height / (gridHeight + Const.CELL_GAP_COEF * (gridHeight - 1));
        return Mathf.Min(cellWidth, cellHeight);
    }

    private Vector2 GetCellPosition(Vector2Int cellPosition)
    {
        float originX = -totalWidth / 2f;
        float originY = totalHeight / 2f;

        float x = originX + cellPosition.x * step;
        float y = originY - cellPosition.y * step;

        return new Vector2(x, y);
    }

    private void BuildAllWords(List<string> wordList)
    {
        var set = new HashSet<string>();
        if (wordList != null)
        {
            foreach (var word in wordList)
            {
                if (!string.IsNullOrEmpty(word))
                {
                    set.Add(word.ToUpperInvariant());
                }
            }
        }

        if (validWords != null)
        {
            foreach (var word in validWords)
            {
                if (!string.IsNullOrEmpty(word))
                {
                    set.Add(word.ToUpperInvariant());
                }
            }
        }

        allWordsUpper = set.ToList();
    }

    public void GetAllowedNextLetters(string prefix, HashSet<char> result)
    {
        if (result == null) return;
        result.Clear();

        if (string.IsNullOrEmpty(prefix) || allWordsUpper == null || allWordsUpper.Count == 0)
        {
            return;
        }

        string upperPrefix = prefix.ToUpperInvariant();
        int prefixLen = upperPrefix.Length;

        foreach (var word in allWordsUpper)
        {
            if (word.Length <= prefixLen) continue;
            if (word.StartsWith(upperPrefix))
            {
                result.Add(word[prefixLen]);
            }
        }
    }

    public void SetHighlightLetter(char letter)
    {
        if (letter == '\0')
        {
            ClearHighlights();
            return;
        }

        char target = char.ToUpperInvariant(letter);
        foreach (var cell in cellMap.Values)
        {
            if (cell == null || string.IsNullOrEmpty(cell.letter)) continue;
            char c = char.ToUpperInvariant(cell.letter[0]);
            cell.SetHighlighted(c == target);
        }
    }

    public void ClearHighlights()
    {
        foreach (var cell in cellMap.Values)
        {
            if (cell == null) continue;
            cell.SetHighlighted(false);
        }
    }

    public void CheckAnswer(string checkWord)
    {
        LineWord line = lines.Find(x => x.answer == checkWord);

        if (line != null)
        {
            if (!line.isShown)
            {
                textPreview.SetAnswerColor();
                line.ShowAnswer();
                CheckGameComplete();

                if (lines.Last() == line)
                {
                    compliment.ShowRandom();
                }

                Sound.instance.Play(Sound.Others.Match);
            }
            else
            {
                textPreview.SetExistColor();
            }
        }
        else if (validWords.Contains(checkWord.ToLower()))
        {
            ExtraWord.instance.ProcessWorld(checkWord);
        }
        else
        {
            textPreview.SetWrongColor();
        }

        textPreview.FadeOut();
    }

    private void CheckGameComplete()
    {
        SaveLevelProgress();
        var isNotShown = lines.Find(x => !x.isShown);
        if (isNotShown == null)
        {
            ClearLevelProgress();
            MainController.instance.OnComplete();

            if (lines.Count >= 6)
            {
                compliment.ShowRandom();
            }
        }
    }

    public void HintClick()
    {
        int ballance = CurrencyController.GetBalance();
        if (ballance >= Const.HINT_COST)
        {
            var line = lines.Find(x => !x.isShown);

            if (line != null)
            {
                line.ShowHint();
                CurrencyController.DebitBalance(Const.HINT_COST);
                CheckGameComplete();

                Prefs.AddToNumHint(GameState.currentWorld, GameState.currentSubWorld, GameState.currentLevel);
            }
        }
        else
        {
            DialogController.instance.ShowDialog(DialogType.Shop);
        }
        Sound.instance.PlayButton();
    }

    public void SaveLevelProgress()
    {
        if (!Prefs.IsLastLevel()) return;

        List<string> results = new List<string>();
        foreach (var line in lines)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var cell in line.cells)
            {
                sb.Append(cell.isShown ? "1" : "0");
            }
            results.Add(sb.ToString());
        }

        Prefs.levelProgress = results.ToArray();
    }

    public string[] GetLevelProgress()
    {
        if (!Prefs.IsLastLevel()) return new string[0];
        return Prefs.levelProgress;
    }

    public void ClearLevelProgress()
    {
        if (!Prefs.IsLastLevel()) return;
        CPlayerPrefs.DeleteKey("level_progress");
    }

    public bool CheckLevelProgress(string[] levelProgress, List<string> wordList)
    {
        if (levelProgress.Length != wordList.Count) return false;

        for (int i = 0; i < wordList.Count; i++)
        {
            if (levelProgress[i].Length != wordList[i].Length) return false;
        }
        return true;
    }

    private void OnApplicationPause(bool pause)
    {
        if (!pause)
        {
            Timer.Schedule(this, 0.5f, () =>
            {
                UpdateBoard();
            });
        }
    }

    private void UpdateBoard()
    {
        string[] progress = GetLevelProgress();
        if (progress.Length == 0) return;

        int i = 0;
        foreach (var line in lines)
        {
            line.SetProgress(progress[i]);
            i++;
        }
    }
}
