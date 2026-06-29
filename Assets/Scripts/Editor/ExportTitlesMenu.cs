using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace PoemPoetry.Editor
{
    /// <summary>
    /// 导出所有诗词标题为 JSON。默认存到工程根目录（Assets 之外），不会被打进 APK。
    /// 数据源为 Tools/SampleContent/poems_seed.json（与「难度配置」一致）。
    /// </summary>
    public static class ExportTitlesMenu
    {
        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));

        // 工程根目录（Assets 的上一级）—— 在打包范围之外。
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [MenuItem("PoemPoetry/导出诗词标题 (JSON)", priority = 4)]
        public static void Export()
        {
            if (!File.Exists(SeedPath))
            {
                EditorUtility.DisplayDialog("导出诗词标题", "找不到 poems_seed.json：\n" + SeedPath, "好");
                return;
            }

            JArray poems;
            try
            {
                var root = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
                poems = root["poems"] as JArray;
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog("导出诗词标题", "解析 poems_seed.json 失败：\n" + ex.Message, "好");
                return;
            }
            if (poems == null)
            {
                EditorUtility.DisplayDialog("导出诗词标题", "poems_seed.json 中没有 poems 数组。", "好");
                return;
            }

            var titles = new List<JObject>();
            foreach (var item in poems)
            {
                var p = (JObject)item;
                titles.Add(new JObject
                {
                    ["id"] = (string)p["id"],
                    ["title"] = (string)p["title"],
                    ["dynasty"] = (string)p["dynasty"],
                    ["author"] = (string)p["author"],
                    ["type"] = (string)p["type"],
                });
            }

            // 默认存到工程根目录（不在 Assets/StreamingAssets 内，故不进 APK）。
            string path = EditorUtility.SaveFilePanel(
                "导出诗词标题为 JSON", ProjectRoot, "poem_titles.json", "json");
            if (string.IsNullOrEmpty(path)) return;   // 用户取消

            var output = new JObject
            {
                ["count"] = titles.Count,
                ["titles"] = new JArray(titles),
            };
            File.WriteAllText(path, output.ToString(Formatting.Indented), new UTF8Encoding(false));

            // 若用户把它存进了 Assets 内，提示一下（会被打包）。
            if (path.Replace('\\', '/').StartsWith(Application.dataPath.Replace('\\', '/')))
                EditorUtility.DisplayDialog("导出诗词标题",
                    $"已导出 {titles.Count} 条标题到：\n{path}\n\n注意：该文件位于 Assets 内，会被打进 APK。如需排除请存到工程外。", "好");
            else
                EditorUtility.DisplayDialog("导出诗词标题",
                    $"已导出 {titles.Count} 条标题到：\n{path}\n（在 Assets 之外，不会进入 APK 打包。）", "好");
        }
    }
}
