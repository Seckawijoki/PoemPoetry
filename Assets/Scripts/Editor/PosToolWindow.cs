using UnityEditor;
using UnityEngine;
#if POEM_JIEBA
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using JiebaNet.Segmenter.PosSeg;
#endif

namespace PoemPoetry.Editor
{
    /// <summary>
    /// 词性统计 (POS) tool. Uses jieba.NET (gated by the POEM_JIEBA scripting define so the build
    /// is unaffected until the DLL is added). Counts coarse POS over the corpus and can backfill
    /// each line's posPattern (used by QuestionGenerator's 对仗 ranking).
    ///
    /// Caveat: jieba is trained on modern Chinese; on 文言/诗词 expect ~80-85% accuracy — treat
    /// the output as a statistic/hint, not ground truth.
    /// </summary>
    public sealed class PosToolWindow : EditorWindow
    {
        private string _report = "点击上方按钮开始。";
        private Vector2 _scroll;

        [MenuItem("唐诗宋词/词性统计")]
        public static void Open() => GetWindow<PosToolWindow>("词性统计");

        private void OnGUI()
        {
#if POEM_JIEBA
            EditorGUILayout.HelpBox(
                "jieba.NET 已启用。jieba 按现代汉语训练，文言/诗词约 80-85% 准确，仅作参考。", MessageType.Info);
            if (GUILayout.Button("① 统计词性数量", GUILayout.Height(34))) CountPos();
            if (GUILayout.Button("② 统计并回填每句 posPattern", GUILayout.Height(30))) Backfill();
#else
            EditorGUILayout.HelpBox(
                "未启用 jieba.NET。启用步骤：\n" +
                "1. NuGet 取 jieba.NET（MIT），把 JiebaNet.*.dll 放到 Assets/Editor/Jieba/，词典 Resources 放同目录。\n" +
                "2. 在 PoemPoetry.Editor.asmdef 的 precompiledReferences 里加上这些 DLL 名。\n" +
                "3. Project Settings ▸ Player ▸ Scripting Define Symbols 添加 POEM_JIEBA。\n" +
                "4. 用 JiebaNet 的 ConfigManager 把词典目录指向 Assets/Editor/Jieba/Resources。\n" +
                "替代方案：纯字典法（解析 jieba dict.txt + 极大匹配分词），无需 DLL。",
                MessageType.Warning);
#endif
            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

#if POEM_JIEBA
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));

        private static PosSegmenter _seg;
        private static PosSegmenter Seg => _seg ?? (_seg = new PosSegmenter());

        // jieba (ICTCLAS) flag -> coarse project tag. Adjust to your tagset as needed.
        private static string Coarse(string flag)
        {
            if (string.IsNullOrEmpty(flag)) return "x";
            switch (flag[0])
            {
                case 'n': return "n";    // 名词
                case 'v': return "v";    // 动词
                case 'a': return "adj";  // 形容词
                case 'm': return "num";  // 数词
                case 'q': return "q";    // 量词
                case 'r': return "pron"; // 代词
                case 'd': return "adv";  // 副词
                case 'f': return "loc";  // 方位
                case 's': return "loc";  // 处所
                case 't': return "t";    // 时间
                case 'p': return "prep";
                case 'c': return "conj";
                case 'u': return "aux";
                default: return "x";
            }
        }

        private void CountPos()
        {
            var root = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            var counts = new SortedDictionary<string, int>();
            int lines = 0;
            foreach (var p in (JArray)root["poems"])
                foreach (var ln in (JArray)p["lines"])
                {
                    string text = (string)((JObject)ln)["text"];
                    lines++;
                    // PosSegmenter.Cut returns word/flag pairs (Pair.Word / Pair.Flag).
                    foreach (var tok in Seg.Cut(text))
                    {
                        var c = Coarse(tok.Flag);
                        counts.TryGetValue(c, out var n);
                        counts[c] = n + 1;
                    }
                }
            var sb = new StringBuilder();
            sb.AppendLine($"共 {lines} 句。词性数量（粗标签）：");
            foreach (var kv in counts) sb.AppendLine($"  {kv.Key} : {kv.Value}");
            _report = sb.ToString();
        }

        private void Backfill()
        {
            var root = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            int lines = 0;
            foreach (var p in (JArray)root["poems"])
                foreach (var ln in (JArray)p["lines"])
                {
                    var lo = (JObject)ln;
                    lo["posPattern"] = PerCharPattern((string)lo["text"]);
                    lines++;
                }
            File.WriteAllText(SeedPath, root.ToString(Newtonsoft.Json.Formatting.Indented), Utf8);
            AssetDatabase.Refresh();
            _report = $"已为 {lines} 句回填 posPattern → poems_seed.json。\n再到「内容工具 ▸ ① 生成题库」重新生成。";
        }

        // Per-character coarse POS, dash-joined (a word's tag applied to each of its characters).
        private static string PerCharPattern(string text)
        {
            var clean = PoemPoetry.Services.RhymeService.StripPunct(text);
            var tags = new List<string>();
            foreach (var tok in Seg.Cut(clean))
            {
                var c = Coarse(tok.Flag);
                var si = new System.Globalization.StringInfo(tok.Word);
                int len = si.LengthInTextElements;
                for (int i = 0; i < len; i++) tags.Add(c);
            }
            return string.Join("-", tags);
        }
#endif
    }
}
