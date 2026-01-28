using System.Collections.Generic;
using UnityEngine;

public class CrosswordLoader : MonoBehaviour
{
    public static List<CrosswordConfig> LoadCrosswordConfig(string filePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(filePath);
        if (!asset || asset.text.Length == 0)
        {
            Debug.LogError("Crossword config file not found!");
            return new List<CrosswordConfig>();
        }

        CrosswordData crosswordData = JsonUtility.FromJson<CrosswordData>(asset.text);
        return crosswordData.CrosswordConfigs;
    }
}
