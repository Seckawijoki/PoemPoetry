using System.Collections.Generic;
using System.IO;
using PoemPoetry.App;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoemPoetry.Editor
{
    /// <summary>One-click creation of a runnable scene (no manual prefab wiring needed).</summary>
    public static class SceneBuilder
    {
        [MenuItem("PoemPoetry/创建启动场景", priority = 40)]
        public static void CreateBootstrapScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cam = new GameObject("Main Camera");
            var c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.97f, 0.95f, 0.90f);
            c.orthographic = true;
            cam.tag = "MainCamera";

            var go = new GameObject("AppBootstrapper");
            go.AddComponent<AppBootstrapper>();

            const string dir = "Assets/Scenes";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            const string path = dir + "/Main.unity";
            EditorSceneManager.SaveScene(scene, path);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };

            EditorUtility.DisplayDialog("PoemPoetry",
                "已创建并打开 Assets/Scenes/Main.unity，并设为唯一构建场景。\n\n" +
                "提示：先按《SETUP.md》设置中文 TMP 字体，再按 Play 运行。", "好");
        }
    }
}
