using PoemPoetry.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoemPoetry.Editor
{
    /// <summary>
    /// One-click capture of every directly-reachable screen. Spins up a throwaway in-memory scene
    /// holding only a <see cref="ScreenshotRunner"/>, enters Play mode to run the capture, then exits
    /// and restores whatever scene you had open. Output lands in the project's <c>screenshots/</c>.
    /// </summary>
    [InitializeOnLoad]
    public static class ScreenshotCaptureMenu
    {
        private const string RestoreKey = "PoemPoetry.Screenshot.RestoreScene";

        static ScreenshotCaptureMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("PoemPoetry/截图/截取所有界面")]
        public static void CaptureAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("PoemPoetry", "请先退出 Play 模式，再运行截图。", "好");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // Remember the current scene so we can reopen it after the run.
            SessionState.SetString(RestoreKey, SceneManager.GetActiveScene().path ?? "");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("ScreenshotRunner");
            go.AddComponent<ScreenshotRunner>();
            // No need to save the scene; Play mode runs the in-memory copy.

            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            var path = SessionState.GetString(RestoreKey, null);
            if (string.IsNullOrEmpty(path)) return;
            SessionState.EraseString(RestoreKey);
            if (System.IO.File.Exists(path))
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }
    }
}
