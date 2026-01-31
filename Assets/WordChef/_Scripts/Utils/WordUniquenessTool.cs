using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// Editor tool to check if a word already exists in crossword levels.
/// Scans LevelCW_*.json files under Assets/WordChef/Resources.
public class WordUniquenessTool : MonoBehaviour
{
#if UNITY_EDITOR
    private const string ResourcesRoot = "Assets/WordChef/Resources";
    private const string LevelPrefix = "LevelCW_";

    private static string inputWord = string.Empty;
    private static Vector2 scroll;
    private static List<string> results = new List<string>();
    private static int scannedFiles;
    private static string lastMessage = string.Empty;

    [MenuItem("Tools/Word Uniqueness Checker")]
    public static void ShowWindow()
    {
        EditorWindow.GetWindow<WordUniquenessWindow>("Word Uniqueness");
    }

    private class WordUniquenessWindow : EditorWindow
    {
        private void OnGUI()
        {
            GUILayout.Label("Check word uniqueness in LevelCW JSON", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            inputWord = EditorGUILayout.TextField("Word", inputWord);
            if (GUILayout.Button("Check", GUILayout.Width(80)))
            {
                RunCheck();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(lastMessage))
            {
                EditorGUILayout.HelpBox(lastMessage, MessageType.Info);
            }

            if (results.Count > 0)
            {
                GUILayout.Label($"Found in {results.Count} level(s).", EditorStyles.label);
                scroll = EditorGUILayout.BeginScrollView(scroll);
                foreach (var r in results)
                {
                    EditorGUILayout.LabelField(r);
                }
                EditorGUILayout.EndScrollView();
            }
            else if (!string.IsNullOrEmpty(inputWord))
            {
                GUILayout.Label("No matches.", EditorStyles.label);
            }

            GUILayout.Space(6);
            GUILayout.Label($"Scanned files: {scannedFiles}", EditorStyles.miniLabel);
        }
    }

    private static void RunCheck()
    {
        results.Clear();
        scannedFiles = 0;
        lastMessage = string.Empty;

        string word = (inputWord ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(word))
        {
            lastMessage = "Enter a word to search.";
            return;
        }

        string[] guids = AssetDatabase.FindAssets("LevelCW_", new[] { ResourcesRoot });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            if (!Path.GetFileName(path).StartsWith(LevelPrefix, StringComparison.OrdinalIgnoreCase)) continue;

            scannedFiles++;
            string json = File.ReadAllText(path);
            CrosswordData data = JsonUtility.FromJson<CrosswordData>(json);
            if (data == null || data.CrosswordConfigs == null) continue;

            bool found = data.CrosswordConfigs.Any(cfg =>
                !string.IsNullOrEmpty(cfg.Answer) &&
                string.Equals(cfg.Answer.Trim(), word, StringComparison.OrdinalIgnoreCase));

            if (found)
            {
                results.Add(path.Replace(ResourcesRoot + "/", string.Empty));
            }
        }

        if (scannedFiles == 0)
        {
            lastMessage = "No LevelCW JSON files found under Resources.";
        }
        else if (results.Count == 0)
        {
            lastMessage = "Word not found in existing levels.";
        }
        else
        {
            lastMessage = $"Found in {results.Count} level(s).";
        }
    }
#endif
}
