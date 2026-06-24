using System.Collections.Generic;
using System.IO;
using System.Text;
using PoemPoetry.Data;
using PoemPoetry.Services;
using UnityEditor;
using UnityEngine;

namespace PoemPoetry.Editor
{
    /// <summary>
    /// Content pipeline UI: import the seed poems, auto-compute rhyme/字数, generate distractor
    /// questions, write StreamingAssets, and validate the shipped bank. Uses the same Services
    /// code paths that the runtime and the standalone test harness use.
    /// </summary>
    public sealed class ContentToolWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _report = "点击上方按钮开始。";

        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));
        private static string DataDir => Path.Combine(Application.streamingAssetsPath, "PoemData");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [MenuItem("唐诗宋词/内容工具")]
        public static void Open() => GetWindow<ContentToolWindow>("唐诗宋词 内容工具");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "① 读取 Tools/SampleContent/poems_seed.json → 计算韵脚/字数 → 生成同字数同韵的干扰项\n" +
                "   → 写入 Assets/StreamingAssets/PoemData/{poems,questions,rhyme_groups}.json\n" +
                "扩充题库：编辑 poems_seed.json（含 char_pinyin.json 覆盖韵脚字），再点①。",
                MessageType.Info);

            if (GUILayout.Button("① 生成题库（标注 + 出题 + 写盘）", GUILayout.Height(40))) Generate();
            if (GUILayout.Button("② 校验现有题库", GUILayout.Height(30))) Validate();

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_report, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private static IRawTextLoader Loader() => new FileRawTextLoader(Application.streamingAssetsPath);

        private void Generate()
        {
            var sb = new StringBuilder();
            try
            {
                Directory.CreateDirectory(DataDir);

                // rhyme_groups.json from the built-in default map (must exist before RhymeService loads).
                var rg = new RhymeGroupFile();
                foreach (var kv in PinyinRhyme.DefaultGroupMap) rg.Groups[kv.Key] = kv.Value;
                File.WriteAllText(Path.Combine(DataDir, "rhyme_groups.json"), PoemJson.Serialize(rg), Utf8);

                if (!File.Exists(SeedPath)) { _report = "找不到种子文件：" + SeedPath; return; }
                var seed = PoemJson.Deserialize<PoemFile>(File.ReadAllText(SeedPath, Encoding.UTF8));

                var rhyme = RhymeService.LoadAsync(new JsonContentSource(Loader())).GetAwaiter().GetResult();
                foreach (var poem in seed.Poems)
                {
                    foreach (var line in poem.Lines) rhyme.Annotate(line);
                    var groups = new HashSet<string>();
                    var pingshui = new HashSet<string>();
                    foreach (var line in poem.Lines)
                        if (line.IsRhymeLine)
                        {
                            if (!string.IsNullOrEmpty(line.RhymeGroup)) groups.Add(line.RhymeGroup);
                            if (!string.IsNullOrEmpty(line.PingshuiRhyme)) pingshui.Add(line.PingshuiRhyme);
                        }
                    foreach (var line in poem.Lines)
                        if (line.IsRhymeLine) rhyme.Annotate(line, groups, pingshui);
                }

                var gen = new QuestionGenerator(seed.Poems, new SystemRandomSource(20260622));
                var questions = new List<Question>();
                int skipped = 0;
                foreach (var poem in seed.Poems)
                {
                    // 诗：只挖韵脚句；词/曲：可挖任意句（含中间句）。
                    var qs = gen.GenerateForPoem(poem, 3, rhymeLinesOnly: poem.Type == "诗");
                    questions.AddRange(qs);
                    if (qs.Count == 0) skipped++;
                }

                File.WriteAllText(Path.Combine(DataDir, "poems.json"),
                    PoemJson.Serialize(new PoemFile { Poems = seed.Poems }), Utf8);
                File.WriteAllText(Path.Combine(DataDir, "questions.json"),
                    PoemJson.Serialize(new QuestionFile { Questions = questions }), Utf8);

                AssetDatabase.Refresh();
                sb.AppendLine($"✓ 生成完成：{seed.Poems.Count} 首诗，{questions.Count} 道题。");
                sb.AppendLine($"  {skipped} 首因同韵同字数候选不足而未出题（属正常，题库越大越少）。");
                sb.AppendLine("  已写入 StreamingAssets/PoemData/。");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("出错：" + e);
            }
            _report = sb.ToString();
        }

        private void Validate()
        {
            var sb = new StringBuilder();
            try
            {
                var source = new JsonContentSource(Loader());
                var poems = source.LoadPoemsAsync().GetAwaiter().GetResult();
                var questions = source.LoadQuestionsAsync().GetAwaiter().GetResult();
                var byId = new Dictionary<string, Poem>();
                foreach (var p in poems) byId[p.Id] = p;

                int errors = 0, warns = 0;
                foreach (var q in questions)
                {
                    if (!byId.TryGetValue(q.PoemId, out var poem)) { sb.AppendLine($"ERROR {q.Id}: poemId 悬空"); errors++; continue; }
                    if (q.BlankLineIndex < 0 || q.BlankLineIndex >= poem.Lines.Count) { sb.AppendLine($"ERROR {q.Id}: blankLineIndex 越界"); errors++; continue; }
                    var target = poem.Lines[q.BlankLineIndex];
                    if (q.Correct == null || q.Correct.Text != target.Text) { sb.AppendLine($"ERROR {q.Id}: 正确答案与原诗不一致"); errors++; }
                    if (q.Distractors == null || q.Distractors.Count != 3) { sb.AppendLine($"ERROR {q.Id}: 干扰项数 ≠ 3"); errors++; continue; }
                    var seen = new HashSet<string> { q.Correct.Text };
                    foreach (var d in q.Distractors)
                    {
                        if (d.CharCount != target.CharCount) { sb.AppendLine($"ERROR {q.Id}: 干扰项字数不符 [{d.Text}]"); errors++; }
                        if (d.RhymeGroup != target.RhymeGroup) { sb.AppendLine($"ERROR {q.Id}: 干扰项韵组不符 [{d.Text}]"); errors++; }
                        if (!seen.Add(d.Text)) { sb.AppendLine($"ERROR {q.Id}: 干扰项重复/同正确项 [{d.Text}]"); errors++; }
                        else if (d.RhymeFinal != target.RhymeFinal) { sb.AppendLine($"WARN  {q.Id}: 韵母不同(同韵组) [{d.Text}]"); warns++; }
                    }
                }
                sb.Insert(0, $"校验：{poems.Count} 首诗 / {questions.Count} 题 → {errors} 错误，{warns} 警告。\n\n");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("出错：" + e);
            }
            _report = sb.ToString();
        }
    }
}
