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
            HandleShortcuts();   // before layout so the (possibly) new index draws consistently
            if (_cipaiList.Length == 0)
            {
                EditorGUILayout.HelpBox("种子里没有带词牌名的词。", MessageType.Info);
                if (GUILayout.Button("重新加载")) Load();
                return;
            }

            EditorGUILayout.HelpBox(
                "选择词牌(← / → 切换) → 用 < / > 调整每句的「句组」号(同号=同一句号，改某句会顺延其后)。\n"
                + "「保存模板」写入 cipai_templates.json；「应用到全部」把模板句组写回所有同词牌的词(句数一致才写)，"
                + "再到「内容工具 ▸ ① 生成题库」重新生成。模板文件在 Assets 之外，不进 APK。",
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
                if (GUILayout.Button("从首词导入分组", GUILayout.Width(120))) { SelectCipaiFromPoem(); }
                if (GUILayout.Button("保存模板", GUILayout.Width(90))) SaveTemplate();
                if (GUILayout.Button($"应用到全部「{cipai}」", GUILayout.Width(180))) ApplyToAll();
                if (GUILayout.Button("重新加载", GUILayout.Width(90))) Load();
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
            if (e.type != EventType.KeyDown || _cipaiList.Length == 0 || EditorGUIUtility.editingTextField) return;
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
            _status = "已从首词导入分组（未保存）。";
        }

        private void SaveTemplate()
        {
            string cipai = _cipaiList[_cipaiIndex];
            _templates[cipai] = new JArray(_groups.ToArray());
            var root = new JObject { ["schemaVersion"] = 1, ["templates"] = _templates };
            File.WriteAllText(TemplatePath, root.ToString(Formatting.Indented), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            _status = $"已保存「{cipai}」模板（{_groups.Count} 句）到 cipai_templates.json。";
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
            SaveTemplate();   // 应用即视为该模板有效，一并存档
            AssetDatabase.Refresh();

            var sb = new StringBuilder();
            sb.Append($"「{cipai}」已写回 {applied} 首");
            if (skipped > 0)
            {
                sb.Append($"，{skipped} 首句数不符未改");
                if (skippedTitles.Count > 0) sb.Append("（" + string.Join("、", skippedTitles) + (skipped > skippedTitles.Count ? "…" : "") + "）");
                sb.Append("——多为「又一体」变体，需单独建模板或手改。");
            }
            sb.Append("\n已保存 poems_seed.json，请到「内容工具 ▸ ① 生成题库」重新生成。");
            _status = sb.ToString();
        }
    }
}
