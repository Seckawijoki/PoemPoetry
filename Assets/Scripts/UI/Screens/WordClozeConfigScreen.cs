using System;
using System.Collections.Generic;
using PoemPoetry.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>逐字填空设置 (推敲炼字): 题量 + 朝代/体裁/难度多选 → 开始填空。难度同时决定每题挖空数。</summary>
    public sealed class WordClozeConfigScreen : UIScreen
    {
        private int _count = 10;
        private readonly HashSet<int> _selTiers = new HashSet<int> { 0 };
        private readonly HashSet<string> _selDynasties = new HashSet<string>();
        private readonly HashSet<string> _selTypes = new HashSet<string>();
        private readonly HashSet<int> _selBlanks = new HashSet<int>();   // 挖空数; empty = all
        private readonly List<Button> _countBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _countLbls = new List<TextMeshProUGUI>();
        private TextMeshProUGUI _poolLabel;
        private static readonly int[] Counts = { 5, 10, 20 };

        protected override void OnShow(object args)
        {
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), "墨意填空");
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            var scroll = UiKit.ScrollList("Cfg", body, out _);

            var card = Design.Card(scroll);
            Design.SectionHead(card, "题量选择");
            BuildCountRow(card);
            Design.SectionHead(card, "朝代筛选（不选 = 全部）");
            var dyn = Services.Content.GetDynasties();
            AddChips(card, dyn, d => d, _selDynasties, Mathf.CeilToInt(dyn.Count / 2f));
            Design.SectionHead(card, "体裁范畴（不选 = 全部）");
            AddChips(card, Services.Content.GetTypes(), tp => tp, _selTypes);
            Design.SectionHead(card, "难易程度（不选 = 全部）");
            var tiers = new List<int> { 0, 1, 2, 3 };
            AddChips(card, tiers, QuizConfigScreen.TierLabel, _selTiers, tiers.Count);
            Design.SectionHead(card, "挖空数（不选 = 全部）");
            var blanks = Services.Content.GetWordClozeBlankCounts().FindAll(c => c >= 1 && c <= 4);
            if (blanks.Count == 0) blanks = new List<int> { 1, 2, 3, 4 };
            AddChips(card, blanks, BlankLabel, _selBlanks, Mathf.Max(1, blanks.Count));

            var hist = UiKit.Button("Hist", scroll, "历史记录", out var histLbl, Design.SurfaceHigh, 32);
            histLbl.color = Design.Ink;
            UiKit.Pref(hist.gameObject, minH: 92);
            hist.onClick.AddListener(() => Nav.Push<RecordsScreen>(new RecordsArgs { Mode = "wordcloze", Title = "填空记录" }));

            _poolLabel = UiKit.Text("Pool", body, "", 30, TextAlignmentOptions.Center, Design.Primary);
            UiKit.MinHeight(_poolLabel.gameObject, 56);

            var start = Design.PrimaryButton("Start", body, "开始填空", out _, 42);
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
            }, _selBlanks);
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
                BlankCounts = new List<int>(_selBlanks),
                Mode = "wordcloze",
            });
        }

        private static string BlankLabel(int c) => c + " 空";

        private void BuildCountRow(Transform parent)
        {
            var row = UiKit.Panel("CountRow", parent);
            UiKit.Pref(row, minH: 104);
            UiKit.HorizontalGroup(row, spacing: 16);
            _countBtns.Clear();
            _countLbls.Clear();
            foreach (var n in Counts)
            {
                int cnt = n;
                var b = UiKit.Button("N" + n, row.transform, n + " 题", out var lbl, Design.SurfaceHigh, 34);
                _countBtns.Add(b);
                _countLbls.Add(lbl);
                b.onClick.AddListener(() => { _count = cnt; RefreshCount(); });
            }
            RefreshCount();
        }

        private void RefreshCount()
        {
            for (int i = 0; i < _countBtns.Count; i++)
                Design.SetChip(_countBtns[i], _countLbls[i], Counts[i] == _count);
        }

        private void AddChips<T>(Transform parent, List<T> items, Func<T, string> label, HashSet<T> selected, int perRow = 3)
        {
            if (items == null || items.Count == 0)
            {
                UiKit.Text("none", parent, "（无）", 28, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }
            Transform row = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % perRow == 0)
                {
                    var p = UiKit.Panel("Row", parent);
                    UiKit.Pref(p, minH: 92);
                    UiKit.HorizontalGroup(p, spacing: 14);
                    row = p.transform;
                }
                var item = items[i];
                var b = UiKit.Button("Chip", row, label(item), out var lbl, Design.SurfaceHigh, 30);
                Design.SetChip(b, lbl, selected.Contains(item));
                b.onClick.AddListener(() =>
                {
                    if (!selected.Remove(item)) selected.Add(item);
                    Design.SetChip(b, lbl, selected.Contains(item));
                    RefreshPoolCount();
                });
            }
        }
    }
}
