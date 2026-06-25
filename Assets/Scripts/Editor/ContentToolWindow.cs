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
    /// Content pipeline UI: import the seed poems, auto-compute rhyme/字数/平仄型, cluster lines into
    /// shared 干扰项簇 and emit lightweight (v2) questions, write StreamingAssets, and validate the
    /// shipped bank. Uses the same Services code paths that the runtime and test harness use.
    /// </summary>
    public sealed class ContentToolWindow : EditorWindow
    {
        private Vector2 _scroll;
        private string _report = "点击上方按钮开始。";

        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));
        private static string DataDir => Path.Combine(Application.streamingAssetsPath, "PoemData");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        [MenuItem("PoemPoetry/内容工具")]
        public static void Open() => GetWindow<ContentToolWindow>("PoemPoetry 内容工具");

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "① 读取 Tools/SampleContent/poems_seed.json → 计算韵脚/字数/平仄型\n" +
                "   → 按(字数,韵组,平仄型)聚成共享干扰项簇 + 轻量题目(v2)\n" +
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

                var source = new JsonContentSource(Loader());
                var rhyme = RhymeService.LoadAsync(source).GetAwaiter().GetResult();
                var tone = new ToneService(source.LoadCharPinyinAsync().GetAwaiter().GetResult());
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

                // v2: cluster lines by (字数,韵组,平仄型) → shared 干扰项池; emit lightweight questions.
                var gen = new QuestionGenerator(seed.Poems, new SystemRandomSource(20260622));
                var bank = gen.BuildBank(seed.Poems, tone, rhymeLinesOnlyForShi: true);

                var withQuestion = new HashSet<string>();
                foreach (var q in bank.Questions) withQuestion.Add(q.PoemId);
                int skipped = seed.Poems.Count - withQuestion.Count;

                File.WriteAllText(Path.Combine(DataDir, "poems.json"),
                    PoemJson.Serialize(new PoemFile { Poems = seed.Poems }), Utf8);
                File.WriteAllText(Path.Combine(DataDir, "questions.json"), PoemJson.Serialize(bank), Utf8);

                AssetDatabase.Refresh();
                sb.AppendLine($"✓ 生成完成：{seed.Poems.Count} 首诗，{bank.Questions.Count} 道题，{bank.Clusters.Count} 个干扰项簇。");
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
                var bank = source.LoadQuestionBankAsync().GetAwaiter().GetResult();
                var byId = new Dictionary<string, Poem>();
                foreach (var p in poems) byId[p.Id] = p;

                int errors = 0, warns = 0;

                // Clusters: every line shares the cluster's 字数/韵组; build (字数|韵组) → unique texts for distractor counting.
                var clusterById = new Dictionary<int, LineCluster>();
                var bucket = new Dictionary<string, HashSet<string>>();
                foreach (var c in bank.Clusters)
                {
                    clusterById[c.Id] = c;
                    var bk = c.CharCount + "|" + c.RhymeGroup;
                    if (!bucket.TryGetValue(bk, out var set)) { set = new HashSet<string>(); bucket[bk] = set; }
                    foreach (var ln in c.Lines)
                    {
                        set.Add(ln.Text);
                        if (ln.CharCount != c.CharCount || ln.RhymeGroup != c.RhymeGroup)
                        { sb.AppendLine($"ERROR cluster {c.Id}: 成员字数/韵组与簇不符 [{ln.Text}]"); errors++; }
                    }
                }

                foreach (var q in bank.Questions)
                {
                    if (!byId.TryGetValue(q.PoemId, out var poem)) { sb.AppendLine($"ERROR {q.Id}: poemId 悬空"); errors++; continue; }
                    if (q.BlankLineIndex < 0 || q.BlankLineIndex >= poem.Lines.Count) { sb.AppendLine($"ERROR {q.Id}: blankLineIndex 越界"); errors++; continue; }
                    var target = poem.Lines[q.BlankLineIndex];
                    if (q.Correct == null || q.Correct.Text != target.Text) { sb.AppendLine($"ERROR {q.Id}: 正确答案与原诗不一致"); errors++; }
                    if (!clusterById.TryGetValue(q.ClusterId, out var cluster)) { sb.AppendLine($"ERROR {q.Id}: clusterId 悬空"); errors++; continue; }
                    if (cluster.CharCount != target.CharCount || cluster.RhymeGroup != target.RhymeGroup)
                    { sb.AppendLine($"ERROR {q.Id}: 簇字数/韵组与题不符"); errors++; }
                    // The 韵组 bucket must yield ≥3 valid distractors (different poem, distinct, not near-dup).
                    int valid = 0;
                    if (bucket.TryGetValue(target.CharCount + "|" + target.RhymeGroup, out var texts))
                        foreach (var t in texts)
                            if (t != target.Text && !QuestionGenerator.NearDuplicate(t, target.Text)) valid++;
                    if (valid < QuestionGenerator.MinDistractors)
                    { sb.AppendLine($"ERROR {q.Id}: 同字数同韵组候选不足 ({valid})"); errors++; }
                }
                sb.Insert(0, $"校验：{poems.Count} 首诗 / {bank.Questions.Count} 题 / {bank.Clusters.Count} 簇 → {errors} 错误，{warns} 警告。\n\n");
            }
            catch (System.Exception e)
            {
                sb.AppendLine("出错：" + e);
            }
            _report = sb.ToString();
        }
    }
}
