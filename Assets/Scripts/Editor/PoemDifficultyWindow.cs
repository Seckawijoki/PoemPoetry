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
    /// Manually assign each poem's difficulty tier (0 = 家喻户晓). Each row can expand to show the
    /// full poem; difficulty is set with left/right (&lt; &gt;) buttons cycling 0/1/2/4. Edits only
    /// the `difficulty` field in poems_seed.json via JObject.
    /// </summary>
    public sealed class PoemDifficultyWindow : EditorWindow
    {
        private static readonly int[] Tiers = { 0, 1, 2, 4 };

        private JObject _root;
        private JArray _poems;
        private Vector2 _scroll;
        private int _detailIndex = -1;   // >=0 → showing the per-line config panel for that poem

        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));

        [MenuItem("唐诗宋词/难度配置")]
        public static void Open() => GetWindow<PoemDifficultyWindow>("诗词难度配置");

        private void OnEnable() => Load();

        private void Load()
        {
            if (!File.Exists(SeedPath)) { _root = null; _poems = null; return; }
            _root = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            _poems = _root["poems"] as JArray;
        }

        private void OnGUI()
        {
            if (_poems == null)
            {
                EditorGUILayout.HelpBox("找不到或无法解析 poems_seed.json", MessageType.Error);
                if (GUILayout.Button("重新加载")) Load();
                return;
            }
            if (_detailIndex >= 0 && _detailIndex < _poems.Count) DrawDetail();
            else DrawList();
        }

        private void DrawList()
        {
            EditorGUILayout.HelpBox(
                "难度档：0=家喻户晓(小学) 1=含名句 2=不含名句 4=生僻。用 < > 设置整首难度档；点「配置」进入逐句配置(句组/名句)。改完点保存，再到「内容工具 ▸ ① 生成题库」。",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("按名气推断 0/1/2")) InferFromFame();
                if (GUILayout.Button("重新加载")) Load();
                if (GUILayout.Button("保存")) Save();
            }
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int idx = 0; idx < _poems.Count; idx++)
            {
                var p = (JObject)_poems[idx];
                int diff = p["difficulty"] != null ? (int)p["difficulty"] : 0;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("配置", GUILayout.Width(44))) { _detailIndex = idx; _scroll = Vector2.zero; return; }
                    EditorGUILayout.LabelField(
                        $"{(string)p["title"]}  ({(string)p["dynasty"]}·{(string)p["author"]})  [{(string)p["fame"]}]",
                        GUILayout.Width(340));
                    if (GUILayout.Button("<", GUILayout.Width(26))) p["difficulty"] = PrevTier(diff);
                    EditorGUILayout.LabelField(TierLabel(diff), GUILayout.Width(78));
                    if (GUILayout.Button(">", GUILayout.Width(26))) p["difficulty"] = NextTier(diff);
                    EditorGUILayout.LabelField("均" + AvgDiff(p), GUILayout.Width(44));
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>Per-line config for one poem: 句组(group) + 名句(famous), with prev/next nav.</summary>
        private void DrawDetail()
        {
            var p = (JObject)_poems[_detailIndex];
            int diff = p["difficulty"] != null ? (int)p["difficulty"] : 0;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("← 返回列表", GUILayout.Width(110))) { _detailIndex = -1; return; }
                using (new EditorGUI.DisabledScope(_detailIndex <= 0))
                    if (GUILayout.Button("上一首", GUILayout.Width(90))) { _detailIndex--; _scroll = Vector2.zero; return; }
                using (new EditorGUI.DisabledScope(_detailIndex >= _poems.Count - 1))
                    if (GUILayout.Button("下一首", GUILayout.Width(90))) { _detailIndex++; _scroll = Vector2.zero; return; }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("保存", GUILayout.Width(80))) Save();
            }
            EditorGUILayout.LabelField(
                $"{_detailIndex + 1}/{_poems.Count}   {(string)p["title"]}  ({(string)p["dynasty"]}·{(string)p["author"]})  [{(string)p["fame"]}]",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("整首难度档", GUILayout.Width(80));
                if (GUILayout.Button("<", GUILayout.Width(26))) p["difficulty"] = PrevTier(diff);
                EditorGUILayout.LabelField(TierLabel(diff), GUILayout.Width(90));
                if (GUILayout.Button(">", GUILayout.Width(26))) p["difficulty"] = NextTier(diff);
                EditorGUILayout.LabelField("平均难度 " + AvgDiff(p), GUILayout.Width(110));
            }
            EditorGUILayout.HelpBox(
                "句组(group)：同一句号内的分句填相同号(出题时该句号的所有分句一起作为上下文显示；单独成句则借相邻一句)。"
                + "名句：勾选则该句按低难度计(档1→1、档2→2)，其余句更高(档1→2、档2→4)。",
                MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (p["lines"] is JArray lines)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = (JObject)lines[i];
                    int g = ln["group"] != null ? (int)ln["group"] : i / 2;
                    bool fam = ln["famous"] != null && (bool)ln["famous"];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("[" + i + "]", GUILayout.Width(30));
                        EditorGUILayout.LabelField((string)ln["text"], GUILayout.Width(230));
                        EditorGUILayout.LabelField("句组", GUILayout.Width(34));
                        int ng = EditorGUILayout.IntField(g, GUILayout.Width(44));
                        if (ng != g) ln["group"] = ng;
                        bool nf = EditorGUILayout.ToggleLeft("名句", fam, GUILayout.Width(64));
                        if (nf != fam) { if (nf) ln["famous"] = true; else ln.Remove("famous"); }
                        EditorGUILayout.LabelField("→ 难度" + LineDiff(diff, nf), GUILayout.Width(70));
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // Mirror of Services.DifficultyRules (kept inline to avoid an Editor→runtime asmdef hop).
        private static int LineDiff(int tier, bool famous)
        {
            switch (tier)
            {
                case 0: return 0;
                case 1: return famous ? 1 : 2;
                case 2: return famous ? 2 : 4;
                case 4: return 4;
                default: return tier;
            }
        }

        private static int AvgDiff(JObject p)
        {
            if (!(p["lines"] is JArray lines) || lines.Count == 0) return 0;
            int tier = p["difficulty"] != null ? (int)p["difficulty"] : 0;
            int sum = 0;
            foreach (var ln in lines)
            {
                bool fam = ((JObject)ln)["famous"] != null && (bool)((JObject)ln)["famous"];
                sum += LineDiff(tier, fam);
            }
            return (sum + lines.Count / 2) / lines.Count;
        }

        private static int PrevTier(int d)
        {
            int i = System.Array.IndexOf(Tiers, d);
            return i <= 0 ? Tiers[0] : Tiers[i - 1];
        }

        private static int NextTier(int d)
        {
            int i = System.Array.IndexOf(Tiers, d);
            return (i < 0 || i >= Tiers.Length - 1) ? Tiers[Tiers.Length - 1] : Tiers[i + 1];
        }

        private static string TierLabel(int t)
        {
            switch (t)
            {
                case 0: return "0·入门";
                case 1: return "1·名句";
                case 2: return "2·进阶";
                case 4: return "4·终极";
                default: return "难度" + t;
            }
        }

        private void InferFromFame()
        {
            foreach (var item in _poems)
            {
                var p = (JObject)item;
                string fame = (string)p["fame"];
                p["difficulty"] = fame == "obscure" ? 2 : (fame == "common" ? 1 : 0);
            }
        }

        private void Save()
        {
            File.WriteAllText(SeedPath, _root.ToString(Formatting.Indented), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("难度配置",
                "已保存到 poems_seed.json。\n请到「唐诗宋词 ▸ 内容工具 ▸ ① 生成题库」重新生成。", "好");
        }
    }
}
