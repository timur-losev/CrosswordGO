using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// Utility to convert CWZ level sources to JSON crossword configs.
/// Looks for numbered *.cwz files in /LevelsOrg and writes LevelCW_{n}.json
/// into Assets/WordChef/Resources/World_0/SubWorld_0.
public static class CwzConverter
{
#if UNITY_EDITOR
    private const string LevelsOrgFolder = "LevelsOrg";
    private const string OutputFolder = "Assets/WordChef/Resources/World_0/SubWorld_0";

    [MenuItem("Tools/Convert CWZ Levels")]
    public static void ConvertAll()
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string sourceDir = Path.Combine(projectRoot, LevelsOrgFolder);
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogError($"Source directory not found: {sourceDir}");
                return;
            }

            Directory.CreateDirectory(Path.Combine(projectRoot, OutputFolder));

            var files = Directory.GetFiles(sourceDir, "*.cwz")
                .Select(f => new { Path = f, Name = Path.GetFileNameWithoutExtension(f) })
                .OrderBy(f => ParseLevelNumber(f.Name))
                .ToList();

            foreach (var file in files)
            {
                int levelIndex = ParseLevelNumber(file.Name);
                var data = ParseCwz(file.Path);
                if (data.CrosswordConfigs.Count == 0)
                {
                    Debug.LogWarning($"No entries found in {file.Path}, skipping.");
                    continue;
                }

                string json = JsonUtility.ToJson(data, true);
                string outPath = Path.Combine(projectRoot, OutputFolder, $"LevelCW_{levelIndex}.json");
                File.WriteAllText(outPath, json);
                Debug.Log($"Converted {file.Path} -> {outPath} ({data.CrosswordConfigs.Count} entries)");
            }

            AssetDatabase.Refresh();
        }
        catch (Exception ex)
        {
            Debug.LogError($"CWZ conversion failed: {ex}");
        }
    }

    private static int ParseLevelNumber(string name)
    {
        // Expect filenames like "0", "1", etc. Fall back to 0 if parsing fails.
        return int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }

    private static CrosswordData ParseCwz(string path)
    {
        var data = new CrosswordData { CrosswordConfigs = new List<CrosswordConfig>() };

        // CWZ файлы не имеют единственного корня, поэтому оборачиваем содержимое.
        // Также некоторые строки содержат незакрытые / повреждённые теги.
        // Отфильтруем только строки, начинающиеся с "<ClueAnswerPair" или "</ClueAnswerPair" и вложенные теги.
        string raw = File.ReadAllText(path);
        var filtered = new List<string> { "<Root>" };

        using (StringReader sr = new StringReader(raw))
        {
            string line;
            bool insidePair = false;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();

                if (line.StartsWith("<ClueAnswerPair", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add("<ClueAnswerPair>");
                    insidePair = true;
                    continue;
                }
                if (line.StartsWith("</ClueAnswerPair", StringComparison.OrdinalIgnoreCase))
                {
                    filtered.Add("</ClueAnswerPair>");
                    insidePair = false;
                    continue;
                }

                if (insidePair)
                {
                    // Пропускаем пустые или подозрительные строки
                    if (line.StartsWith("<") && line.Contains(">") && !line.Contains("<Set>"))
                    {
                        filtered.Add(line);
                    }
                }
            }
        }

        filtered.Add("</Root>");
        string wrapped = string.Join("\n", filtered);

        XDocument doc = XDocument.Parse(wrapped, LoadOptions.None);
        var pairs = doc.Descendants("ClueAnswerPair");
        foreach (var pair in pairs)
        {
            string answer = pair.Element("Answer")?.Value ?? string.Empty;
            string xStr = pair.Element("XPos")?.Value ?? "0";
            string yStr = pair.Element("YPos")?.Value ?? "0";
            string dirStr = pair.Element("Direction")?.Value ?? "0";

            if (string.IsNullOrWhiteSpace(answer))
            {
                continue;
            }

            int x = SafeInt(xStr);
            int y = SafeInt(yStr);
            int dir = SafeInt(dirStr);

            data.CrosswordConfigs.Add(new CrosswordConfig
            {
                Answer = answer,
                XPos = x,
                YPos = y,
                Direction = dir
            });
        }

        return data;
    }

    private static int SafeInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
    }
#endif
}

