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
    /// 编辑「词牌名 → 句组(group)模板」。同一词牌的所有词句数与分句结构一致（如 浣溪沙 = [0,0,0,1,1,1]），
    /// 本工具集中维护每个词牌的分组模板，并可一键写回 poems_seed.json 里所有同词牌的词，省去逐首手填句组。
    ///
    /// 模板存到 Tools/SampleContent/cipai_templates.json（在 Assets 之外，不会打进 APK）。
    /// </summary>
    public sealed class CipaiTemplateWindow : EditorWindow
    {
        private JObject _seedRoot;
        private JArray _poems;
        private JObject _templates;     // { "词牌名": [0,0,1,...] }

        private string[] _cipaiList = new string[0];   // 排过拼音序的词牌名
        private int _cipaiIndex = -1;
        private List<string> _refLines = new List<string>();   // 参考词(首匹配)的每句文本
        private List<int> _groups = new List<int>();           // 正在编辑的句组模板
        private Vector2 _scroll;
        private string _status = "";
        private bool _dirty;            // 有未写盘的模板改动 → 标题/全部保存按钮显示星号

        private static string SeedPath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/poems_seed.json"));
        private static string TemplatePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "../Tools/SampleContent/cipai_templates.json"));

        private static readonly System.Globalization.CultureInfo ZhCulture =
            System.Globalization.CultureInfo.GetCultureInfo("zh-CN");

        [MenuItem("PoemPoetry/词牌分组模板", priority = 5)]
        public static void Open() => GetWindow<CipaiTemplateWindow>("词牌分组模板");

        private void OnEnable() => Load();

        private void Load()
        {
            _status = "";
            if (!File.Exists(SeedPath)) { _seedRoot = null; _poems = null; return; }
            _seedRoot = JObject.Parse(File.ReadAllText(SeedPath, Encoding.UTF8));
            _poems = _seedRoot["poems"] as JArray;

            _templates = File.Exists(TemplatePath)
                ? (JObject.Parse(File.ReadAllText(TemplatePath, Encoding.UTF8))["templates"] as JObject) ?? new JObject()
                : new JObject();

            // 收集所有词牌名（type==词，cipai 非空），按拼音排序。
            var set = new List<string>();
            foreach (var item in _poems)
            {
                var p = (JObject)item;
                if ((string)p["type"] != "词") continue;
                string c = (string)p["cipai"];
                if (!string.IsNullOrEmpty(c) && !set.Contains(c)) set.Add(c);
            }
            set.Sort((a, b) => string.Compare(a, b, ZhCulture, System.Globalization.CompareOptions.None));
            _cipaiList = set.ToArray();

            _cipaiIndex = _cipaiList.Length > 0 ? 0 : -1;
            SelectCipai();
            _dirty = false;
        }

        /// <summary>切换词牌：取首匹配词的句文本作参考，并载入其模板(无则从该词现有 group 推导)。</summary>
        private void SelectCipai()
        {
            _refLines.Clear();
            _groups.Clear();
            if (_cipaiIndex < 0) return;
            string cipai = _cipaiList[_cipaiIndex];

            JObject first = null;
            foreach (var item in _poems)
            {
                var p = (JObject)item;
                if ((string)p["type"] == "词" && (string)p["cipai"] == cipai) { first = p; break; }
            }
            if (first == null || !(first["lines"] is JArray lines)) return;

            foreach (var ln in lines) _refLines.Add((string)((JObject)ln)["text"]);

            var saved = _templates[cipai] as JArray;
            for (int i = 0; i < lines.Count; i++)
            {
                if (saved != null && i < saved.Count) _groups.Add((int)saved[i]);
                else
                {
                    var ln = (JObject)lines[i];
                    _groups.Add(ln["group"] != null ? (int)ln["group"] : i / 2);
                }
            }
        }

        private void OnGUI()
        {
            if (_poems == null)
            {
                EditorGUILayout.HelpBox("找不到或无法解析 poems_seed.json", MessageType.Error);
                if (GUILayout.Button("重新加载")) Load();
                return;
            }
            titleContent.text = _dirty ? "词牌分组模板 *" : "词牌分组模板";
            HandleShortcuts();   // before layout so the (possibly) new index draws consistently
            if (_cipaiList.Length == 0)
            {
                EditorGUILayout.HelpBox("种子里没有带词牌名的词。", MessageType.Info);
                if (GUILayout.Button("重新加载")) Load();
                return;
            }

            EditorGUILayout.HelpBox(
                "选择词牌(← / → 切换) → 用 < / > 调整每句的「句组」号(同号=同一句号，改某句会顺延其后)。\n"
                + "改动会即时记入内存(可跨词牌)，标题/按钮显示 * 表示未写盘；「全部保存」一次写入 cipai_templates.json。\n"
                + "「应用到全部」只处理当前词牌；「全部写入DB」(Ctrl+Shift+S)把所有模板写回各自的词→生成题库→重建 content.db，进游戏即生效。"
                + "Ctrl+S=全部保存(仅模板文件)。模板文件在 Assets 之外，不进 APK。",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("词牌", GUILayout.Width(34));
                int ni = EditorGUILayout.Popup(_cipaiIndex, CipaiLabels(), GUILayout.Width(220));
                if (ni != _cipaiIndex) { _cipaiIndex = ni; SelectCipai(); _scroll = Vector2.zero; _status = ""; }
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField($"共 {_cipaiList.Length} 个词牌", GUILayout.Width(110));
            }

            string cipai = _cipaiList[_cipaiIndex];
            int poemCount = CountPoems(cipai);
            EditorGUILayout.LabelField(
                $"{cipai}　{poemCount} 首　模板 {_groups.Count} 句　{(_templates[cipai] != null ? "已存模板" : "未存模板")}",
                EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("从首词导入分组", GUILayout.Width(120))) SelectCipaiFromPoem();
                if (GUILayout.Button(_dirty ? "全部保存 *" : "全部保存", GUILayout.Width(100))) SaveAll();
                if (GUILayout.Button($"应用到全部「{cipai}」", GUILayout.Width(170))) ApplyToAll();
                if (GUILayout.Button("全部写入DB (Ctrl+Shift+S)", GUILayout.Width(190))) WriteAllToDb();
                if (GUILayout.Button("重新加载", GUILayout.Width(80))) Load();
            }
            if (!string.IsNullOrEmpty(_status)) EditorGUILayout.HelpBox(_status, MessageType.Info);

            EditorGUILayout.Space();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _groups.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("[" + i + "]", GUILayout.Width(28));
                    EditorGUILayout.LabelField(i < _refLines.Count ? _refLines[i] : "(无参考句)", GUILayout.Width(200));
                    EditorGUILayout.LabelField("句组", GUILayout.Width(30));
                    if (GUILayout.Button("<", GUILayout.Width(24))) ShiftFrom(i, -1);
                    int ng = EditorGUILayout.DelayedIntField(_groups[i], GUILayout.Width(36));
                    if (ng != _groups[i]) ShiftFrom(i, ng - _groups[i]);
                    if (GUILayout.Button(">", GUILayout.Width(24))) ShiftFrom(i, +1);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        /// <summary>← / → 切换上/下一个词牌（编辑句组数字时不触发）。</summary>
        private void HandleShortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.S && (e.control || e.command))
            {
                if (e.shift) WriteAllToDb(); else SaveAll();   // Ctrl+Shift+S = 全部写入DB；Ctrl+S = 全部保存
                e.Use(); return;
            }
            if (_cipaiList.Length == 0 || EditorGUIUtility.editingTextField) return;
            int dir = e.keyCode == KeyCode.LeftArrow ? -1 : (e.keyCode == KeyCode.RightArrow ? 1 : 0);
            if (dir == 0) return;
            int ni = Mathf.Clamp(_cipaiIndex + dir, 0, _cipaiList.Length - 1);
            if (ni != _cipaiIndex) { _cipaiIndex = ni; SelectCipai(); _scroll = Vector2.zero; _status = ""; e.Use(); Repaint(); }
        }

        /// <summary>把第 start 句及其后所有句子的句组号加上 delta（与难度配置一致的顺延语义）。</summary>
        private void ShiftFrom(int start, int delta)
        {
            if (delta == 0) return;
            for (int j = start; j < _groups.Count; j++) _groups[j] += delta;
            CommitCurrent(); _dirty = true;   // 即时记入内存模板，跨词牌不丢；待「全部保存」写盘
        }

        /// <summary>把当前正在编辑的句组提交进内存模板表（不写盘）。</summary>
        private void CommitCurrent()
        {
            if (_cipaiIndex >= 0 && _cipaiIndex < _cipaiList.Length)
                _templates[_cipaiList[_cipaiIndex]] = new JArray(_groups.ToArray());
        }

        /// <summary>把内存模板表写入 cipai_templates.json。</summary>
        private void WriteTemplatesFile()
        {
            var root = new JObject { ["schemaVersion"] = 1, ["templates"] = _templates };
            File.WriteAllText(TemplatePath, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            AssetDatabase.Refresh();
        }

        /// <summary>全部保存：提交当前编辑并把所有词牌模板一次写盘。</summary>
        private void SaveAll()
        {
            CommitCurrent();
            WriteTemplatesFile();
            _dirty = false;
            _status = "已保存全部词牌模板到 cipai_templates.json。";
        }

        /// <summary>全部写入DB：保存所有模板 → 把每个模板句组写回同词牌的词(seed) → 生成题库 → 重建 content.db。</summary>
        private void WriteAllToDb()
        {
            CommitCurrent();
            WriteTemplatesFile();   // 落盘所有模板
            _dirty = false;

            var detail = new StringBuilder();
            int totalApplied = 0, totalSkipped = 0;
            foreach (var prop in _templates.Properties())
            {
                if (!(prop.Value is JArray arr)) continue;
                string cp = prop.Name;
                int applied = 0, skipped = 0;
                foreach (var item in _poems)
                {
                    var p = (JObject)item;
                    if ((string)p["type"] != "词" || (string)p["cipai"] != cp) continue;
                    if (!(p["lines"] is JArray lines) || lines.Count != arr.Count) { skipped++; continue; }
                    for (int i = 0; i < lines.Count; i++) ((JObject)lines[i])["group"] = (int)arr[i];
                    applied++;
                }
                totalApplied += applied; totalSkipped += skipped;
                if (applied > 0 || skipped > 0)
                    detail.AppendLine($"  {cp}: 写回 {applied}{(skipped > 0 ? $"，跳过 {skipped}" : "")}");
            }
            File.WriteAllText(SeedPath, _seedRoot.ToString(Formatting.Indented), new UTF8Encoding(false));

            var sb = new StringBuilder();
            sb.AppendLine($"已应用 {_templates.Count} 个词牌模板：写回 {totalApplied} 首"
                + (totalSkipped > 0 ? $"，{totalSkipped} 首句数不符跳过（变体）" : "") + "。");
            sb.Append(detail);
            if (ContentToolWindow.GenerateBank(out string gen))
            {
                sb.AppendLine("✓ 已生成题库 poems.json。");
                if (ContentToolWindow.RunBuildDb(out string db))
                    sb.AppendLine("✓ 已重建 content.db。进游戏即可看到全部新分组。");
                else
                    sb.AppendLine("✗ content.db 未重建，请手动运行：\n  python Tools/ChinesePoetryImport/build_db.py\n" + db);
            }
            else
            {
                sb.AppendLine("✗ 生成题库失败（poems.json 未更新）：\n" + gen);
            }
            _status = sb.ToString();
        }

        private string[] CipaiLabels()
        {
            var labels = new string[_cipaiList.Length];
            for (int i = 0; i < _cipaiList.Length; i++)
            {
                string c = _cipaiList[i];
                labels[i] = _templates[c] != null ? c + " ✓" : c;
            }
            return labels;
        }

        private int CountPoems(string cipai)
        {
            int n = 0;
            foreach (var item in _poems)
            {
                var p = (JObject)item;
                if ((string)p["type"] == "词" && (string)p["cipai"] == cipai) n++;
            }
            return n;
        }

        /// <summary>放弃未存编辑，按首词现有 group 重新填充模板行。</summary>
        private void SelectCipaiFromPoem()
        {
            string cipai = _cipaiList[_cipaiIndex];
            _refLines.Clear(); _groups.Clear();
            foreach (var item in _poems)
            {
                var p = (JObject)item;
                if ((string)p["type"] != "词" || (string)p["cipai"] != cipai) continue;
                if (!(p["lines"] is JArray lines)) break;
                for (int i = 0; i < lines.Count; i++)
                {
                    var ln = (JObject)lines[i];
                    _refLines.Add((string)ln["text"]);
                    _groups.Add(ln["group"] != null ? (int)ln["group"] : i / 2);
                }
                break;
            }
            CommitCurrent(); _dirty = true;
            _status = "已从首词导入分组（未写盘，点「全部保存」保存）。";
        }

        /// <summary>把当前模板的句组写回所有同词牌且句数一致的词，并保存 poems_seed.json。</summary>
        private void ApplyToAll()
        {
            string cipai = _cipaiList[_cipaiIndex];
            int applied = 0, skipped = 0;
            var skippedTitles = new List<string>();
            foreach (var item in _poems)
            {
                var p = (JObject)item;
                if ((string)p["type"] != "词" || (string)p["cipai"] != cipai) continue;
                if (!(p["lines"] is JArray lines) || lines.Count != _groups.Count)
                {
                    skipped++;
                    if (skippedTitles.Count < 6) skippedTitles.Add((string)p["title"]);
                    continue;
                }
                for (int i = 0; i < lines.Count; i++) ((JObject)lines[i])["group"] = _groups[i];
                applied++;
            }

            File.WriteAllText(SeedPath, _seedRoot.ToString(Formatting.Indented), new UTF8Encoding(false));
            CommitCurrent(); WriteTemplatesFile(); _dirty = false;   // 应用即视为该模板有效，一并存档
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.Append($"「{cipai}」已写回 {applied} 首");
            if (skipped > 0)
            {
                sb.Append($"，{skipped} 首句数不符未改");
                if (skippedTitles.Count > 0) sb.Append("（" + string.Join("、", skippedTitles) + (skipped > skippedTitles.Count ? "…" : "") + "）");
                sb.Append("——多为「又一体」变体，需单独建模板或手改。");
            }
            sb.AppendLine("。已保存 poems_seed.json。");

            // 一条龙：① seed→poems.json(生成题库)  ② poems.json→content.db(build_db.py)
            if (ContentToolWindow.GenerateBank(out string genReport))
            {
                sb.AppendLine("✓ 已重新生成题库 poems.json。");
                if (ContentToolWindow.RunBuildDb(out string dbOut))
                    sb.AppendLine("✓ 已重建 content.db，直接进游戏即可看到新分组。");
                else
                    sb.AppendLine("✗ content.db 未重建，请手动运行：\n  python Tools/ChinesePoetryImport/build_db.py\n" + dbOut);
            }
            else
            {
                sb.AppendLine("✗ 生成题库失败（poems.json 未更新）：\n" + genReport);
            }
            _status = sb.ToString();
        }
    }
}
