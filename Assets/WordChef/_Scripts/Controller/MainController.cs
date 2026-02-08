using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Superpow;
using System.Linq;

public class MainController : BaseController {
    public Text levelNameText;

    private int world, subWorld, level;
    private bool isGameComplete;
    private GameLevel gameLevel;

    public static MainController instance;

    protected override void Awake()
    {
        base.Awake();
        instance = this;
    }

    protected override void Start()
    {
        base.Start();
        world = GameState.currentWorld;
        subWorld = GameState.currentSubWorld;
        level = GameState.currentLevel;

        gameLevel = Utils.Load(world, subWorld, level);
        if (gameLevel == null)
        {
            gameLevel = CreateGameLevelFromCrossword(world, subWorld, level);
        }

        if (gameLevel == null)
        {
            Debug.LogError($"No GameLevel asset or crossword config for {world}/{subWorld}/{level}");
            return;
        }

        if (Pan.instance != null)
        {
            Pan.instance.Load(gameLevel);
        }
        else
        {
            Debug.LogWarning("MainController: Pan instance is missing in scene.");
        }

        if (WordRegion.instance != null)
        {
            WordRegion.instance.Load(gameLevel);
        }
        else
        {
            Debug.LogWarning("MainController: WordRegion instance is missing in scene.");
        }

        if (world == 0 && subWorld == 0 && level == 0)
        {
            Timer.Schedule(this, 0.5f, () =>
            {
                DialogController.instance.ShowDialog(DialogType.HowtoPlay);
            });
        }

        if (levelNameText != null)
        {
            levelNameText.text = GameState.currentSubWorldName + " - " + (level + 1);
        }
    }

    public void OnComplete()
    {
        if (isGameComplete) return;
        isGameComplete = true;

        Timer.Schedule(this, 1f, () =>
        {
            DialogController.instance.ShowDialog(DialogType.Win);
            Sound.instance.Play(Sound.Others.Win);
        });
    }

    private string BuildLevelName()
    {
        return world + "-" + subWorld + "-" + level;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && !DialogController.instance.IsDialogShowing())
        {
            DialogController.instance.ShowDialog(DialogType.Pause);
        }
    }

    private GameLevel CreateGameLevelFromCrossword(int world, int subWorld, int level)
    {
        string path = WordRegion.GetCrosswordConfigPath(world, subWorld, level);
        var configs = CrosswordLoader.LoadCrosswordConfig(path);
        if (configs == null || configs.Count == 0) return null;

        var gl = ScriptableObject.CreateInstance<GameLevel>();
        gl.answers = string.Join("|", configs.Select(c => c.Answer));
        gl.validWords = string.Empty;

        // Берём уникальные буквы, чтобы пан имел базовое слово при необходимости
        var uniqueChars = new HashSet<char>();
        foreach (var cfg in configs)
        {
            foreach (char ch in cfg.Answer.ToUpper())
            {
                uniqueChars.Add(ch);
            }
        }
        gl.word = new string(uniqueChars.ToArray());
        return gl;
    }
}
