using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class IOSBuild
{
    private const string DefaultOutputPath = "Builds/iOS";

    [MenuItem("Build/Build iOS Xcode Project")]
    public static void BuildIOSMenu()
    {
        BuildIOS(DefaultOutputPath);
    }

    // Unity CLI entrypoint:
    // -executeMethod IOSBuild.BuildIOSFromCommandLine -buildOutput Builds/iOS
    public static void BuildIOSFromCommandLine()
    {
        string outputPath = DefaultOutputPath;
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-buildOutput")
            {
                outputPath = args[i + 1];
                break;
            }
        }

        BuildIOS(outputPath);
    }

    private static void BuildIOS(string outputPath)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            throw new InvalidOperationException("No enabled scenes in Build Settings.");
        }

        Directory.CreateDirectory(outputPath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        };

        Debug.Log("Starting iOS build to: " + outputPath);
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new Exception("iOS build failed. Result: " + summary.result);
        }

        Debug.Log("iOS build succeeded: " + outputPath);
    }
}
