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
        private static readonly int[] Tiers = { 0, 1, 2, 3 };

        private JObject _root;
        private JArray _poems;
        private Vector2 _scroll;
        private int _detailIndex = -1;   // >=0 → showing the per-line config panel for that poem
        private bool _dirty;             // 有未保存改动 → 标题/保存按钮显示星号
        private string _status = "";     // 「全部写入DB」结果（不弹框，显示在面板上）

        // List filters (index 0 = 全部). Arrays rebuilt on Load.
        private int _typeFilter;
        private int _dynastyFilter;
        private string[] _typeOptions = { "全部" };
        private string[] _dynastyOptions = { "全部" };

        // Sub-category filter that depends on 体裁: 诗 → 句式(五言绝句…)，词 → 词牌名。Index 0 = 全部。
        private int _catFilter;
        private string[] _catOptions = { "全部" };
        private string _catForType = "";   // 体裁 the current _catOptions were built for
        private static readonly string[] ShiCategories =
            { "五言绝句", "五言律诗", "七言绝句", "七言律诗", "其它" };

        // 朝代按历史时间排序的参照表；表外朝代排到最后。
        private static readonly string[] DynastyOrder =
            { "先秦", "汉", "魏晋", "南北朝", "隋", "唐", "五代", "宋", "辽", "金", "元", "明", "清", "近现代" };

        private static readonly System.Globalization.CultureInfo ZhCulture =
            System.Globalization.CultureInfo.GetCultureInfo("zh-CN");

        private static int DynastyRank(string d)
        {
            int i = System.Array.IndexOf(DynastyOrder, d);
            return i < 0 ? int.MaxValue : i;
        }

        /// <summary>按拼音比较中文字符串 (zh-CN)。</summary>
        private static int PinyinCompare(string a, string b) =>
            string.Compare(a, b, ZhCulture, System.Globalization.CompareOptions.None);

        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));

        [MenuItem("PoemPoetry/难度配置", priority = 2)]
        public static void Open() => GetWindow<PoemDifficultyWindow>("诗词难度配置");

        private void OnEnable() => Load();

        private void Load()
        {
            if (!File.Exists(SeedPath)) { _root = null; _poems = null; return; }

            // 记住当前筛选值，重新加载后按值还原（下标可能因数据变化而改变）。
            string selType = SelectedValue(_typeOptions, _typeFilter);
            string selDynasty = SelectedValue(_dynastyOptions, _dynastyFilter);
            string selCat = SelectedValue(_catOptions, _catFilter);

            _root = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            _poems = _root["poems"] as JArray;
            _typeOptions = DistinctValues("type");
            _dynastyOptions = DistinctValues("dynasty", (a, b) =>
            {
                int ra = DynastyRank(a), rb = DynastyRank(b);
                return ra != rb ? ra.CompareTo(rb) : PinyinCompare(a, b);
            });
            _typeFilter = IndexOf(_typeOptions, selType);
            _dynastyFilter = IndexOf(_dynastyOptions, selDynasty);
            _catFilter = 0; RebuildCatOptions();
            _catFilter = IndexOf(_catOptions, selCat);
            _dirty = false; _status = "";
        }

        /// <summary>The value at <paramref name="index"/> (null for 全部/out-of-range).</summary>
        private static string SelectedValue(string[] options, int index) =>
            index > 0 && index < options.Length ? options[index] : null;

        /// <summary>Index of <paramref name="value"/> in options, or 0 (全部) if missing.</summary>
        private static int IndexOf(string[] options, string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;
            int i = System.Array.IndexOf(options, value);
            return i < 0 ? 0 : i;
        }

        /// <summary>句式/词牌名 options for the currently-selected 体裁 (诗 / 词); just {全部} otherwise.</summary>
        private void RebuildCatOptions()
        {
            string type = _typeFilter > 0 ? _typeOptions[_typeFilter] : "";
            var list = new List<string> { "全部" };
            if (type == "诗") list.AddRange(ShiCategories);
            else if (type == "词" && _poems != null)
            {
                var cipai = new List<string>();
                foreach (var item in _poems)
                {
                    var p = (JObject)item;
                    if ((string)p["type"] != "词") continue;
                    string c = (string)p["cipai"];
                    if (!string.IsNullOrEmpty(c) && !cipai.Contains(c)) cipai.Add(c);
                }
                cipai.Sort(PinyinCompare);   // 词牌名按拼音排序
                list.AddRange(cipai);
            }
            _catOptions = list.ToArray();
            _catForType = type;
            if (_catFilter >= _catOptions.Length) _catFilter = 0;
        }

        private string[] DistinctValues(string field, System.Comparison<string> sort = null)
        {
            var list = new List<string>();
            if (_poems != null)
                foreach (var item in _poems)
                {
                    string v = (string)((JObject)item)[field];
                    if (!string.IsNullOrEmpty(v) && !list.Contains(v)) list.Add(v);
                }
            if (sort != null) list.Sort(sort);
            list.Insert(0, "全部");
            return list.ToArray();
        }

        private bool PoemMatches(JObject p)
        {
            if (_typeFilter > 0 && (string)p["type"] != _typeOptions[_typeFilter]) return false;
            if (_dynastyFilter > 0 && (string)p["dynasty"] != _dynastyOptions[_dynastyFilter]) return false;
            if (_catFilter > 0 && _catFilter < _catOptions.Length)
            {
                string cat = _catOptions[_catFilter];
                string type = (string)p["type"];
                if (type == "诗" && PoemCategory(p) != cat) return false;
                if (type == "词" && (string)p["cipai"] != cat) return false;
            }
            return true;
        }

        /// <summary>诗 句式分类：按句数与每句字数判定五/七言绝句/律诗，其余归「其它」。</summary>
        private static string PoemCategory(JObject p)
        {
            if (!(p["lines"] is JArray lines) || lines.Count == 0) return "其它";
            int first = CharLen((string)((JObject)lines[0])["text"]);
            foreach (var item in lines)
                if (CharLen((string)((JObject)item)["text"]) != first) return "其它";
            if (lines.Count == 4 && first == 5) return "五言绝句";
            if (lines.Count == 8 && first == 5) return "五言律诗";
            if (lines.Count == 4 && first == 7) return "七言绝句";
            if (lines.Count == 8 && first == 7) return "七言律诗";
            return "其它";
        }

        /// <summary>Count CJK/letter chars in a line, ignoring punctuation and whitespace.</summary>
        private static int CharLen(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int n = 0;
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(s);
            while (e.MoveNext())
                if (char.IsLetter(((string)e.Current)[0])) n++;
            return n;
        }

        /// <summary>Next poem index from <paramref name="from"/> in direction dir (+1/-1) matching the filter, or -1.</summary>
        private int NextMatching(int from, int dir)
        {
            for (int i = from + dir; i >= 0 && i < _poems.Count; i += dir)
                if (PoemMatches((JObject)_poems[i])) return i;
            return -1;
        }

        private void OnGUI()
        {
            if (_poems == null)
            {
                EditorGUILayout.HelpBox("找不到或无法解析 poems_seed.json", MessageType.Error);
                if (GUILayout.Button("重新加载")) Load();
                return;
            }
            titleContent.text = _dirty ? "诗词难度配置 *" : "诗词难度配置";
            HandleShortcuts();   // before any layout so the (possibly) new index draws consistently
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);
            if (_detailIndex >= 0 && _detailIndex < _poems.Count) DrawDetail();
            else DrawList();
        }

        /// <summary>Ctrl/Cmd+S = 保存 (any mode); ←/→ = 上一首/下一首 (detail mode, not while typing).</summary>
        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.S && (e.control || e.command))
            {
                if (e.shift) WriteAllToDb(); else Save();   // Ctrl+Shift+S = 全部写入DB；Ctrl+S = 仅存 seed
                e.Use(); return;
            }
            if (_detailIndex < 0 || EditorGUIUtility.editingTextField) return;
            if (e.keyCode == KeyCode.LeftArrow)
            {
                int t = NextMatching(_detailIndex, -1);
                if (t >= 0) { _detailIndex = t; _scroll = Vector2.zero; e.Use(); Repaint(); }
            }
            else if (e.keyCode == KeyCode.RightArrow)
            {
                int t = NextMatching(_detailIndex, +1);
                if (t >= 0) { _detailIndex = t; _scroll = Vector2.zero; e.Use(); Repaint(); }
            }
        }

        private void DrawList()
        {
            EditorGUILayout.HelpBox(
                "难度档：0=家喻户晓(小学) 1=含名句 2=不含名句 3=生僻。用 < > 设置整首难度档；点「配置」进入逐句配置(句组/名句)。改完点保存，再到「内容工具 ▸ ① 生成题库」。",
                MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("按名气推断 0/1/2")) { InferFromFame(); _dirty = true; }
                if (GUILayout.Button("重新加载")) Load();
                if (GUILayout.Button(_dirty ? "保存 *" : "保存")) Save();
                if (GUILayout.Button("全部写入DB (Ctrl+Shift+S)")) WriteAllToDb();
            }
            int shown = 0;
            foreach (var item in _poems) if (PoemMatches((JObject)item)) shown++;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("体裁", GUILayout.Width(34));
                int newType = EditorGUILayout.Popup(_typeFilter, _typeOptions, GUILayout.Width(80));
                if (newType != _typeFilter) { _typeFilter = newType; _catFilter = 0; RebuildCatOptions(); }

                string curType = _typeFilter > 0 ? _typeOptions[_typeFilter] : "";
                if (curType == "诗" || curType == "词")
                {
                    if (_catForType != curType) RebuildCatOptions();
                    EditorGUILayout.LabelField(curType == "词" ? "词牌" : "句式", GUILayout.Width(34));
                    _catFilter = EditorGUILayout.Popup(_catFilter, _catOptions, GUILayout.Width(120));
                }

                EditorGUILayout.LabelField("朝代", GUILayout.Width(34));
                _dynastyFilter = EditorGUILayout.Popup(_dynastyFilter, _dynastyOptions, GUILayout.Width(90));
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("共 " + shown + " 首", GUILayout.Width(90));
            }
            EditorGUILayout.Space();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int idx = 0; idx < _poems.Count; idx++)
            {
                var p = (JObject)_poems[idx];
                if (!PoemMatches(p)) continue;
                int diff = p["difficulty"] != null ? (int)p["difficulty"] : 0;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("配置", GUILayout.Width(44))) { _detailIndex = idx; _scroll = Vector2.zero; return; }
                    EditorGUILayout.LabelField(
                        $"{(string)p["title"]}  ({(string)p["dynasty"]}·{(string)p["author"]})  [{(string)p["fame"]}]",
                        GUILayout.Width(340));
                    if (GUILayout.Button("<", GUILayout.Width(26))) { p["difficulty"] = PrevTier(diff); _dirty = true; }
                    EditorGUILayout.LabelField(TierLabel(diff), GUILayout.Width(78));
                    if (GUILayout.Button(">", GUILayout.Width(26))) { p["difficulty"] = NextTier(diff); _dirty = true; }
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
                int prev = NextMatching(_detailIndex, -1);
                int next = NextMatching(_detailIndex, +1);
                if (GUILayout.Button("≡ 列表", GUILayout.Width(90))) { _detailIndex = -1; return; }
                using (new EditorGUI.DisabledScope(prev < 0))
                    if (GUILayout.Button("← 上一首", GUILayout.Width(96))) { _detailIndex = prev; _scroll = Vector2.zero; return; }
                using (new EditorGUI.DisabledScope(next < 0))
                    if (GUILayout.Button("下一首 →", GUILayout.Width(96))) { _detailIndex = next; _scroll = Vector2.zero; return; }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(_dirty ? "保存 * (Ctrl+S)" : "保存 (Ctrl+S)", GUILayout.Width(140))) Save();
                if (GUILayout.Button("全部写入DB (Ctrl+Shift+S)", GUILayout.Width(190))) WriteAllToDb();
            }
            EditorGUILayout.LabelField(
                $"{_detailIndex + 1}/{_poems.Count}   {(string)p["title"]}  ({(string)p["dynasty"]}·{(string)p["author"]})  [{(string)p["fame"]}]",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("整首难度档", GUILayout.Width(80));
                if (GUILayout.Button("<", GUILayout.Width(26))) { p["difficulty"] = PrevTier(diff); _dirty = true; }
                EditorGUILayout.LabelField(TierLabel(diff), GUILayout.Width(90));
                if (GUILayout.Button(">", GUILayout.Width(26))) { p["difficulty"] = NextTier(diff); _dirty = true; }
                EditorGUILayout.LabelField("平均难度 " + AvgDiff(p), GUILayout.Width(110));
            }
            EditorGUILayout.HelpBox(
                "快捷键：← / → 切换上下一首，Ctrl+S 保存(仅 seed)，Ctrl+Shift+S 全部写入DB(seed→题库→content.db)。\n"
                + "押韵：该句是否入韵(诗只考押韵句)。句组(group)：同一句号内的分句填相同号(出题时整句一起作上下文；单独成句则借相邻一句)；"
                + "改某句组号并回车、或点 < / > 加减，会把该句及其后所有句子整体顺延相同差值(如改 1→2，其后 1,2,3,4 依次变 2,3,4,5)。"
                + "名句：勾选则该句按低难度计(档1→1、档2→2)，其余更高(档1→2、档2→3)。"
                + "难度：用 - / + 单独设置；「自n」=按名句/难度档自动推算(值为 n)，再减一档回到自动。",
                MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (p["lines"] is JArray lines)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = (JObject)lines[i];
                    int g = ln["group"] != null ? (int)ln["group"] : i / 2;
                    bool fam = ln["famous"] != null && (bool)ln["famous"];
                    int ov = ln["diff"] != null ? (int)ln["diff"] : -1;     // explicit override, -1 = auto
                    int eff = ov >= 0 ? ov : LineDiff(diff, fam);
                    bool rhyme = ln["isRhymeLine"] != null && (bool)ln["isRhymeLine"];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("[" + i + "]", GUILayout.Width(26));
                        EditorGUILayout.LabelField((string)ln["text"], GUILayout.Width(180));
                        bool nrh = EditorGUILayout.ToggleLeft("押韵", rhyme, GUILayout.Width(56));
                        if (nrh != rhyme) { ln["isRhymeLine"] = nrh; _dirty = true; }
                        EditorGUILayout.LabelField("句组", GUILayout.Width(30));
                        if (GUILayout.Button("<", GUILayout.Width(22))) { ShiftGroupsFrom(lines, i, -1); _dirty = true; }
                        int ng = EditorGUILayout.DelayedIntField(g, GUILayout.Width(34));
                        if (ng != g) { ShiftGroupsFrom(lines, i, ng - g); _dirty = true; }
                        if (GUILayout.Button(">", GUILayout.Width(22))) { ShiftGroupsFrom(lines, i, +1); _dirty = true; }
                        bool nf = EditorGUILayout.ToggleLeft("名句", fam, GUILayout.Width(52));
                        if (nf != fam) { if (nf) ln["famous"] = true; else ln.Remove("famous"); _dirty = true; }
                        EditorGUILayout.LabelField("难", GUILayout.Width(20));
                        if (GUILayout.Button("-", GUILayout.Width(22))) { SetLineDiff(ln, PrevLineTier(ov)); _dirty = true; }
                        EditorGUILayout.LabelField(ov < 0 ? "自" + eff : eff.ToString(), GUILayout.Width(42));
                        if (GUILayout.Button("+", GUILayout.Width(22))) { SetLineDiff(ln, NextLineTier(ov)); _dirty = true; }
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 把第 <paramref name="start"/> 句及其后所有句子的句组号整体加上 <paramref name="delta"/>，
        /// 使后续组号自动顺延：例如把某句从 1 改成 2，则该句及其后同组的 1、以及更后面的 2,3,4… 依次变为
        /// 2,3,4,5…（<paramref name="start"/> 之前的同组句子保持不变，相当于在此处拆出一个新句组）。
        /// 未显式标注 group 的句子先按「索引/2」的默认值落实，再做平移。
        /// </summary>
        private static void ShiftGroupsFrom(JArray lines, int start, int delta)
        {
            if (delta == 0) return;
            for (int j = start; j < lines.Count; j++)
            {
                var ln = (JObject)lines[j];
                int cur = ln["group"] != null ? (int)ln["group"] : j / 2;
                ln["group"] = cur + delta;
            }
        }

        // -1 = 自动(按名句/难度档推算)，其余按 {0,1,2,3} 单独覆盖。
        private static readonly int[] LineTiers = { -1, 0, 1, 2, 3 };

        private static void SetLineDiff(JObject ln, int d)
        {
            if (d < 0) { if (ln["diff"] != null) ln.Remove("diff"); }   // back to auto → drop the key
            else ln["diff"] = d;
        }

        private static int PrevLineTier(int d)
        {
            int i = System.Array.IndexOf(LineTiers, d);
            if (i < 0) i = 1;
            return i <= 0 ? LineTiers[0] : LineTiers[i - 1];
        }

        private static int NextLineTier(int d)
        {
            int i = System.Array.IndexOf(LineTiers, d);
            if (i < 0) i = 1;
            return i >= LineTiers.Length - 1 ? LineTiers[LineTiers.Length - 1] : LineTiers[i + 1];
        }

        // Mirror of Services.DifficultyRules (kept inline to avoid an Editor→runtime asmdef hop).
        private static int LineDiff(int tier, bool famous)
        {
            switch (tier)
            {
                case 0: return 0;
                case 1: return famous ? 1 : 2;
                case 2: return famous ? 2 : 3;
                case 3: return 3;
                default: return tier;
            }
        }

        private static int AvgDiff(JObject p)
        {
            if (!(p["lines"] is JArray lines) || lines.Count == 0) return 0;
            int tier = p["difficulty"] != null ? (int)p["difficulty"] : 0;
            int sum = 0;
            foreach (var item in lines)
            {
                var ln = (JObject)item;
                int ov = ln["diff"] != null ? (int)ln["diff"] : -1;
                bool fam = ln["famous"] != null && (bool)ln["famous"];
                sum += ov >= 0 ? ov : LineDiff(tier, fam);
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
                case 3: return "3·终极";
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
            _dirty = false;   // 不再弹框；星号消失即表示已保存
            Debug.Log("[难度配置] 已保存 poems_seed.json（改完记得到「内容工具 ▸ ① 生成题库」重新生成）。");
        }

        /// <summary>全部写入DB：保存 seed → 生成题库(poems.json) → 重建 content.db。一步到位、进游戏即生效。</summary>
        private void WriteAllToDb()
        {
            Save();   // 写 seed + 清星号
            var sb = new StringBuilder();
            if (ContentToolWindow.GenerateBank(out string gen))
            {
                sb.AppendLine("✓ 已生成题库 poems.json。");
                if (ContentToolWindow.RunBuildDb(out string db))
                    sb.AppendLine("✓ 已重建 content.db（版本标记已更新）。进游戏即可看到改动。");
                else
                    sb.AppendLine("✗ content.db 未重建，请手动运行：\n  python Tools/ChinesePoetryImport/build_db.py\n" + db);
            }
            else
            {
                sb.AppendLine("✗ 生成题库失败（poems.json 未更新）：\n" + gen);
            }
            _status = sb.ToString();
            Debug.Log("[难度配置] " + _status);
        }
    }
}
