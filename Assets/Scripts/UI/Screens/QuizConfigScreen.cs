using System;
using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>答题设置: 题数 + 朝代多选 + 难度多选 → 开始挑战.</summary>
    public sealed class QuizConfigScreen : UIScreen
    {
        private int _count = 10;
        private readonly HashSet<int> _selTiers = new HashSet<int> { 0 };
        private readonly HashSet<string> _selDynasties = new HashSet<string>();
        private readonly HashSet<string> _selTypes = new HashSet<string>();
        private readonly List<Button> _countBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _countLbls = new List<TextMeshProUGUI>();
        private TextMeshProUGUI _poolLabel;
        private static readonly int[] Counts = { 5, 10, 20 };

        // Tier labels per the project's difficulty definition.
        public static string TierLabel(int t)
        {
            switch (t)
            {
                case 0: return "0·入门";   // 家喻户晓、小学学过
                case 1: return "1·名句";   // 含名句
                case 2: return "2·进阶";   // 不含名句
                case 3: return "3·终极";
                default: return "难度" + t;
            }
        }

        protected override void OnShow(object args)
        {
            var body = UiKit.ScreenRoot(gameObject, "答题设置", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 12, align: TextAnchor.UpperCenter);

            RestoreLastSelections();

            var scroll = UiKit.ScrollList("Cfg", body, out _);

            SectionLabel(scroll, "题数");
            BuildCountRow(scroll);

            SectionLabel(scroll, "朝代（不选 = 全部）");
            AddChips(scroll, Services.Content.GetDynasties(), d => d, _selDynasties);

            SectionLabel(scroll, "体裁（不选 = 全部）");
            AddChips(scroll, Services.Content.GetTypes(), tp => tp, _selTypes);

            SectionLabel(scroll, "难度（不选 = 全部）");
            AddChips(scroll, Services.Content.GetDifficultyTiers(), TierLabel, _selTiers);

            var hist = UiKit.Button("Hist", scroll, "历史记录", out _, UiKit.CardAlt, 32);
            UiKit.Pref(hist.gameObject, minH: 92);
            hist.onClick.AddListener(() => Nav.Push<RecordsScreen>(new RecordsArgs { Mode = "challenge", Title = "答题记录" }));

            _poolLabel = UiKit.Text("Pool", body, "", 30, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.MinHeight(_poolLabel.gameObject, 56);

            var start = UiKit.Button("Start", body, "开始答题", out var sl, UiKit.Accent, 40);
            sl.color = Color.white;
            var le = UiKit.Pref(start.gameObject, minH: 120);
            le.flexibleHeight = 0f;
            start.onClick.AddListener(StartQuiz);

            RefreshPoolCount();
        }

        private void RefreshPoolCount()
        {
            if (_poolLabel == null) return;
            int n = Services.Content.CountPool(new ChallengeSettings
            {
                Difficulties = new List<int>(_selTiers),
                Dynasties = new List<string>(_selDynasties),
                Types = new List<string>(_selTypes),
            });
            _poolLabel.text = $"当前可出题 {n} 道";
        }

        private void RestoreLastSelections()
        {
            var s = Services.Settings != null ? Services.Settings.Current : null;
            if (s == null) return;
            if (s.LastChallengeLength > 0) _count = s.LastChallengeLength;
            if (s.LastDifficulties != null && s.LastDifficulties.Count > 0)
            {
                _selTiers.Clear();
                foreach (var t in s.LastDifficulties) _selTiers.Add(t);
            }
            _selDynasties.Clear();
            if (s.LastDynasties != null) foreach (var d in s.LastDynasties) _selDynasties.Add(d);
            _selTypes.Clear();
            if (s.LastTypes != null) foreach (var t in s.LastTypes) _selTypes.Add(t);
        }

        private void StartQuiz()
        {
            var diffs = new List<int>(_selTiers);
            var dyns = new List<string>(_selDynasties);
            var types = new List<string>(_selTypes);
            var s = Services.Settings != null ? Services.Settings.Current : null;
            if (s != null)
            {
                s.LastChallengeLength = _count;
                s.LastDifficulties = diffs;
                s.LastDynasties = dyns;
                s.LastTypes = types;
                _ = Services.Settings.SaveAsync();
            }
            Nav.Push<QuizScreen>(new QuizStartArgs
            {
                QuestionCount = _count,
                Difficulties = diffs,
                Dynasties = dyns,
                Types = types,
                Mode = "challenge",
            });
        }

        private void SectionLabel(Transform parent, string text)
        {
            var t = UiKit.Text("Sec", parent, text, 30, TextAlignmentOptions.Left, UiKit.Accent);
            UiKit.MinHeight(t.gameObject, 56);
        }

        private void BuildCountRow(Transform parent)
        {
            var row = UiKit.Panel("CountRow", parent);
            UiKit.Pref(row, minH: 110);
            UiKit.HorizontalGroup(row, spacing: 14);
            _countBtns.Clear();
            _countLbls.Clear();
            foreach (var n in Counts)
            {
                int cnt = n;
                var b = UiKit.Button("N" + n, row.transform, n + " 题", out var lbl, UiKit.Card, 34);
                _countBtns.Add(b);
                _countLbls.Add(lbl);
                b.onClick.AddListener(() => { _count = cnt; RefreshCount(); });
            }
            RefreshCount();
        }

        private void RefreshCount()
        {
            for (int i = 0; i < _countBtns.Count; i++)
                UiKit.SetChipSelected(_countBtns[i], _countLbls[i], Counts[i] == _count);
        }

        private void AddChips<T>(Transform parent, List<T> items, Func<T, string> label, HashSet<T> selected)
        {
            if (items == null || items.Count == 0)
            {
                UiKit.Text("none", parent, "（无）", 28, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }
            Transform row = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % 3 == 0)
                {
                    var p = UiKit.Panel("Row", parent);
                    UiKit.Pref(p, minH: 92);
                    UiKit.HorizontalGroup(p, spacing: 12);
                    row = p.transform;
                }
                var item = items[i];
                var b = UiKit.Button("Chip", row, label(item), out var lbl, UiKit.Card, 30);
                UiKit.SetChipSelected(b, lbl, selected.Contains(item));
                b.onClick.AddListener(() =>
                {
                    if (!selected.Remove(item)) selected.Add(item);
                    UiKit.SetChipSelected(b, lbl, selected.Contains(item));
                    RefreshPoolCount();
                });
            }
        }
    }
}
