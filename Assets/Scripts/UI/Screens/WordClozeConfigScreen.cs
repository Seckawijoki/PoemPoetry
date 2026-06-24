using System;
using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>逐词填空设置: 题数 + 朝代/体裁/难度多选 → 开始。难度同时决定每题挖空数（越难越多空）。</summary>
    public sealed class WordClozeConfigScreen : UIScreen
    {
        private int _count = 10;
        private readonly HashSet<int> _selTiers = new HashSet<int> { 0 };
        private readonly HashSet<string> _selDynasties = new HashSet<string>();
        private readonly HashSet<string> _selTypes = new HashSet<string>();
        private readonly List<Button> _countBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _countLbls = new List<TextMeshProUGUI>();
        private TextMeshProUGUI _poolLabel;
        private static readonly int[] Counts = { 5, 10, 20 };

        protected override void OnShow(object args)
        {
            var body = UiKit.ScreenRoot(gameObject, "逐词填空设置", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 12, align: TextAnchor.UpperCenter);

            var scroll = UiKit.ScrollList("Cfg", body, out _);

            SectionLabel(scroll, "题数");
            BuildCountRow(scroll);

            SectionLabel(scroll, "朝代（不选 = 全部）");
            AddChips(scroll, Services.Content.GetDynasties(), d => d, _selDynasties);

            SectionLabel(scroll, "体裁（不选 = 全部）");
            AddChips(scroll, Services.Content.GetTypes(), tp => tp, _selTypes);

            SectionLabel(scroll, "难度（不选 = 全部，越难挖空越多）");
            AddChips(scroll, Services.Content.GetWordClozeDifficultyTiers(), QuizConfigScreen.TierLabel, _selTiers);

            var hist = UiKit.Button("Hist", scroll, "历史记录", out _, UiKit.CardAlt, 32);
            UiKit.Pref(hist.gameObject, minH: 92);
            hist.onClick.AddListener(() => Nav.Push<RecordsScreen>(new RecordsArgs { Mode = "wordcloze", Title = "填空记录" }));

            _poolLabel = UiKit.Text("Pool", body, "", 30, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.MinHeight(_poolLabel.gameObject, 56);

            var start = UiKit.Button("Start", body, "开始填空", out var sl, UiKit.Accent, 40);
            sl.color = Color.white;
            var le = UiKit.Pref(start.gameObject, minH: 120);
            le.flexibleHeight = 0f;
            start.onClick.AddListener(StartGame);

            RefreshPoolCount();
        }

        private void RefreshPoolCount()
        {
            if (_poolLabel == null) return;
            int n = Services.Content.CountWordClozePool(new ChallengeSettings
            {
                Difficulties = new List<int>(_selTiers),
                Dynasties = new List<string>(_selDynasties),
                Types = new List<string>(_selTypes),
            });
            _poolLabel.text = $"当前可出题 {n} 道";
        }

        private void StartGame()
        {
            Nav.Push<WordClozeScreen>(new WordClozeStartArgs
            {
                QuestionCount = _count,
                Difficulties = new List<int>(_selTiers),
                Dynasties = new List<string>(_selDynasties),
                Types = new List<string>(_selTypes),
                Mode = "wordcloze",
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
