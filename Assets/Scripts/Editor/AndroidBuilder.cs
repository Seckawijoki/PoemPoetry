using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PoemPoetry.Editor
{
    /// <summary>
    /// One-click Android APK builder. Use the menu (Build → Android APK) from the Editor, or run
    /// headless from the command line:
    ///   Unity.exe -quit -batchmode -projectPath &lt;proj&gt; -buildTarget Android \
    ///             -executeMethod PoemPoetry.Editor.AndroidBuilder.BuildApk
    /// Optional command-line flags (batchmode only):
    ///   -outputPath &lt;file.apk&gt;   override the output path
    ///   -development              build a development (debuggable) APK
    /// Output defaults to &lt;project&gt;/Builds/Android/PoemPoetry-&lt;version&gt;.apk.
    /// </summary>
    public static class AndroidBuilder
    {
        private const string DefaultOutputDir = "Builds/Android";

        [MenuItem("Build/Android APK", priority = 0)]
        public static void BuildApkMenu()
        {
            string path = BuildInternal(development: false, overridePath: null);
            if (!string.IsNullOrEmpty(path))
            {
                EditorUtility.RevealInFinder(path);
            }
        }

        [MenuItem("Build/Android APK (Development)", priority = 1)]
        public static void BuildApkDevMenu()
        {
            string path = BuildInternal(development: true, overridePath: null);
            if (!string.IsNullOrEmpty(path))
            {
                EditorUtility.RevealInFinder(path);
            }
        }

        /// <summary>Entry point for -executeMethod (command-line / CI). Sets exit code on failure.</summary>
        public static void BuildApk()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool development = args.Contains("-development");
            string overridePath = ReadArg(args, "-outputPath");

            string result = BuildInternal(development, overridePath);
            if (string.IsNullOrEmpty(result))
            {
                EditorApplication.Exit(1);
            }
        }

        private static string BuildInternal(bool development, string overridePath)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[AndroidBuilder] No enabled scenes in Build Settings. Aborting.");
                return null;
            }

            // Guarantee an .aab is never produced here — we want an installable APK.
            EditorUserBuildSettings.buildAppBundle = false;

            string outputPath = overridePath;
            if (string.IsNullOrEmpty(outputPath))
            {
                string dir = Path.Combine(Directory.GetCurrentDirectory(), DefaultOutputDir);
                Directory.CreateDirectory(dir);
                string fileName = $"{SanitizeFileName(PlayerSettings.productName)}-{PlayerSettings.bundleVersion}.apk";
                outputPath = Path.Combine(dir, fileName);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development
                    ? (BuildOptions.Development | BuildOptions.AllowDebugging)
                    : BuildOptions.None,
            };

            Debug.Log($"[AndroidBuilder] Building {(development ? "development " : "")}APK -> {outputPath}\n" +
                      $"  scenes: {string.Join(", ", scenes)}\n" +
                      $"  package: {PlayerSettings.applicationIdentifier}  backend: {PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[AndroidBuilder] SUCCESS: {outputPath}  " +
                          $"({summary.totalSize / (1024f * 1024f):F1} MB, {summary.totalTime.TotalSeconds:F0}s)");
                return outputPath;
            }

            Debug.LogError($"[AndroidBuilder] FAILED: {summary.result} " +
                           $"({summary.totalErrors} errors). See log above.");
            return null;
        }

        private static string ReadArg(string[] args, string flag)
        {
            int i = Array.IndexOf(args, flag);
            if (i >= 0 && i + 1 < args.Length)
            {
                return args[i + 1];
            }
            return null;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
