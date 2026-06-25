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

        [MenuItem("PoemPoetry/词性统计")]
        public static void Open() => GetWindow<PosToolWindow>("词性统计");

        private void OnGUI()
        {
#if POEM_JIEBA
            EditorGUILayout.HelpBox(
                "jieba.NET 已启用。jieba 按现代汉语训练，文言/诗词约 80-85% 准确，仅作参考。", MessageType.Info);
            if (GUILayout.Button("① 统计词性数量", GUILayout.Height(34))) CountPos();
            if (GUILayout.Button("② 统计并回填每句 posPattern", GUILayout.Height(30))) Backfill();
            if (GUILayout.Button("③ 抽取名词/动词 → word_bank_seed.json", GUILayout.Height(30))) BuildWordBank();
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

        // 抽取语料中的名词/动词，标注语义类型，写出 word_bank_seed.json（残句调控玩法的词库种子）。
        // 平仄/拼音/来源由 TestHarness 离线管线回填；这里只产出 text/pos/semantic 供人工审阅。
        private static readonly string[] SemPriority = { "颜色", "动物", "植物", "方位", "数字", "时间" };

        private void BuildWordBank()
        {
            // 字 → 语义类（按优先级，高优先级先占）。
            var charCat = new Dictionary<string, string>();
            var catPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/semantic_categories.json"));
            if (File.Exists(catPath))
            {
                var cf = JObject.Parse(File.ReadAllText(catPath, Encoding.UTF8));
                var cats = cf["categories"] as JObject;
                if (cats != null)
                    foreach (var cat in SemPriority)
                    {
                        if (!(cats[cat] is JArray arr)) continue;
                        foreach (var ch in arr)
                        {
                            var s = (string)ch;
                            if (!string.IsNullOrEmpty(s) && !charCat.ContainsKey(s)) charCat[s] = cat;
                        }
                    }
            }

            var root = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            var seen = new HashSet<string>();
            var words = new JArray();
            int n = 0, v = 0;
            foreach (var p in (JArray)root["poems"])
                foreach (var ln in (JArray)p["lines"])
                {
                    string text = PoemPoetry.Services.RhymeService.StripPunct((string)((JObject)ln)["text"]);
                    foreach (var tok in Seg.Cut(text))
                    {
                        var pos = Coarse(tok.Flag);
                        if (pos != "n" && pos != "v") continue;
                        var w = tok.Word;
                        if (string.IsNullOrEmpty(w) || !seen.Add(w)) continue;
                        if (pos == "n") n++; else v++;

                        // 语义：词中任一字命中类别（按优先级）；否则用 jieba 标签兜底；再否则 通用。
                        string sem = "通用";
                        var chars = SplitChars(w);
                        foreach (var cat in SemPriority)
                        {
                            bool hit = false;
                            foreach (var ch in chars) if (charCat.TryGetValue(ch, out var c) && c == cat) { hit = true; break; }
                            if (hit) { sem = cat; break; }
                        }
                        if (sem == "通用")
                        {
                            if (tok.Flag.StartsWith("m")) sem = "数字";
                            else if (tok.Flag.StartsWith("t")) sem = "时间";
                            else if (tok.Flag.StartsWith("f") || tok.Flag.StartsWith("s")) sem = "方位";
                        }

                        words.Add(new JObject { ["text"] = w, ["pos"] = pos, ["semantic"] = sem });
                    }
                }

            var outObj = new JObject { ["schemaVersion"] = 1, ["words"] = words };
            var outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/word_bank_seed.json"));
            File.WriteAllText(outPath, outObj.ToString(Newtonsoft.Json.Formatting.Indented), Utf8);
            AssetDatabase.Refresh();
            _report = $"已抽取 {words.Count} 个词（名词 {n} / 动词 {v}）→ word_bank_seed.json。\n" +
                      "请人工审阅语义/词性，再运行 Tools/TestHarness/build_and_test.ps1 生成 word_bank.json 与 word_questions.json。";
        }

        private static string[] SplitChars(string s)
        {
            var si = new System.Globalization.StringInfo(s);
            int len = si.LengthInTextElements;
            var r = new string[len];
            for (int i = 0; i < len; i++) r[i] = si.SubstringByTextElements(i, 1);
            return r;
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
