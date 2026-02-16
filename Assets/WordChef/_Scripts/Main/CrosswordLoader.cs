using System.Collections.Generic;
using UnityEngine;

public class CrosswordLoader : MonoBehaviour
{
    public static CrosswordData LoadCrosswordData(string filePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(filePath);
        if (!asset || asset.text.Length == 0)
        {
            Debug.LogError("Crossword config file not found!");
            return new CrosswordData
            {
                CrosswordConfigs = new List<CrosswordConfig>(),
                HiddenWords = new List<string>()
            };
        }

        CrosswordData crosswordData = JsonUtility.FromJson<CrosswordData>(asset.text);
        if (crosswordData == null)
        {
            crosswordData = new CrosswordData();
        }
        if (crosswordData.CrosswordConfigs == null)
        {
            crosswordData.CrosswordConfigs = new List<CrosswordConfig>();
        }
        if (crosswordData.HiddenWords == null)
        {
            crosswordData.HiddenWords = new List<string>();
        }
        return crosswordData;
    }

    public static List<CrosswordConfig> LoadCrosswordConfig(string filePath)
    {
        CrosswordData crosswordData = LoadCrosswordData(filePath);
        return crosswordData.CrosswordConfigs;
    }
}
