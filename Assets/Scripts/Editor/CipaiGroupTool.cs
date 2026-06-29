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
    /// 词牌分组：按 Tools/SampleContent/cipai_groups.json 的「句读分组」模板（每个句号组的行数）
    /// 一键重写 poems_seed.json 里所有词的 lines[].group。group 决定「按组换行」显示与出题相邻句。
    /// 只改 group 值、不动其它字段；某首词行数与模板之和不符则跳过并告警。
    /// 改完需再跑「内容工具 ▸ ① 生成题库」+ build_db.py 才会进运行时库。
    /// </summary>
    public static class CipaiGroupTool
    {
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string SeedPath => Path.Combine(Root, "Tools", "SampleContent", "poems_seed.json");
        private static string TemplatePath => Path.Combine(Root, "Tools", "SampleContent", "cipai_groups.json");

        [MenuItem("PoemPoetry/内容工具 ▸ 词牌分组（按模板重写所有词的 group）", priority = 2)]
        public static void ApplyTemplate()
        {
            if (!File.Exists(TemplatePath))
            {
                EditorUtility.DisplayDialog("词牌分组", "找不到模板：" + TemplatePath, "好");
                return;
            }
            if (!File.Exists(SeedPath))
            {
                EditorUtility.DisplayDialog("词牌分组", "找不到种子：" + SeedPath, "好");
                return;
            }

            // Parse the template: cipai -> group sizes (e.g. "卜算子": [2,2,2,2]).
            var tmplRoot = JObject.Parse(File.ReadAllText(TemplatePath, Encoding.UTF8));
            var groups = tmplRoot["groups"] as JObject;
            if (groups == null)
            {
                EditorUtility.DisplayDialog("词牌分组", "模板缺少 \"groups\" 节点。", "好");
                return;
            }
            var sizesByCipai = new Dictionary<string, List<int>>();
            foreach (var prop in groups.Properties())
            {
                var sizes = new List<int>();
                foreach (var v in (JArray)prop.Value) sizes.Add((int)v);
                sizesByCipai[prop.Name] = sizes;
            }

            // Walk every 词 in the seed and rewrite group values in place.
            var seedRoot = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            var poems = (JArray)seedRoot["poems"];
            int applied = 0, ci = 0;
            var skipped = new StringBuilder();
            foreach (JObject p in poems)
            {
                if ((string)p["type"] != "词") continue;
                ci++;
                string cipai = (string)p["cipai"] ?? "";
                if (!sizesByCipai.TryGetValue(cipai, out var sizes)) continue;

                var lines = (JArray)p["lines"];
                int sum = 0;
                foreach (var s in sizes) sum += s;
                if (sum != lines.Count)
                {
                    skipped.AppendLine($"  跳过 {cipai}《{(string)p["title"]}》：行数 {lines.Count} ≠ 模板和 {sum}");
                    continue;
                }

                // Expand sizes -> per-line group ids: [2,2,2,2] -> 0,0,1,1,2,2,3,3.
                int gi = 0, remain = sizes[0], idx = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    while (remain == 0 && idx + 1 < sizes.Count) { idx++; gi++; remain = sizes[idx]; }
                    ((JObject)lines[i])["group"] = gi;
                    remain--;
                }
                applied++;
            }

            File.WriteAllText(SeedPath, seedRoot.ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
            AssetDatabase.Refresh();

            string msg =
                $"已按模板重写 group：{applied}/{ci} 首词（模板含 {sizesByCipai.Count} 个词牌）。\n" +
                (skipped.Length > 0 ? "行数不符已跳过（需先统一断句）：\n" + skipped : "无跳过。\n") +
                "\n后续：内容工具 ▸ ① 生成题库 → python Tools/ChinesePoetryImport/build_db.py（自增 CONTENT_VERSION）。";
            Debug.Log("[CipaiGroup] " + msg);
            EditorUtility.DisplayDialog("词牌分组 — 完成", msg, "好");
        }
    }
}
