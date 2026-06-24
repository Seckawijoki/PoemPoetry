using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>滑动找诗设置: 方向难度 L1-L4 + 网格边长 + 允许重叠 → 开始.</summary>
    public sealed class SlideConfigScreen : UIScreen
    {
        private int _level = 1;
        private int _cols = 9;
        private int _rows = 9;
        private bool _overlap = false;
        private bool _famousOnly = false;

        private static readonly (int level, string label)[] Levels =
        {
            (1, "① 横竖直线"),
            (2, "② 横竖斜线"),
            (3, "③ 横竖蛇形"),
            (4, "④ 全向蛇形"),
        };
        // (cols, rows): the taller ones use the extra vertical space for higher difficulty.
        private static readonly (int cols, int rows, string label)[] Grids =
        {
            (8, 8, "8×8"),
            (10, 10, "10×10"),
            (12, 12, "12×12"),
            (12, 16, "12×16高"),
            (12, 20, "12×20更高"),
        };

        private readonly List<Button> _levelBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _levelLbls = new List<TextMeshProUGUI>();
        private readonly List<Button> _sizeBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _sizeLbls = new List<TextMeshProUGUI>();
        private Button _overlapBtn;
        private TextMeshProUGUI _overlapLbl;
        private Button _famousBtn;
        private TextMeshProUGUI _famousLbl;
        private readonly HashSet<int> _selDiff = new HashSet<int>();
        private readonly HashSet<string> _selDyn = new HashSet<string>();

        protected override void OnShow(object args)
        {
            var body = UiKit.ScreenRoot(gameObject, "滑动找诗设置", () => Nav.Pop());
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 12, align: TextAnchor.UpperCenter);

            RestoreLast();

            var scroll = UiKit.ScrollList("Cfg", body, out _);

            Section(scroll, "方向难度");
            BuildLevelRows(scroll);

            Section(scroll, "网格大小");
            BuildGridRow(scroll);

            Section(scroll, "重叠");
            _overlapBtn = UiKit.Button("Overlap", scroll, "", out _overlapLbl, UiKit.Card, 32);
            UiKit.Pref(_overlapBtn.gameObject, minH: 96);
            _overlapBtn.onClick.AddListener(() => { _overlap = !_overlap; RefreshOverlap(); });
            RefreshOverlap();

            Section(scroll, "名句");
            _famousBtn = UiKit.Button("Famous", scroll, "", out _famousLbl, UiKit.Card, 32);
            UiKit.Pref(_famousBtn.gameObject, minH: 96);
            _famousBtn.onClick.AddListener(() => { _famousOnly = !_famousOnly; RefreshFamous(); });
            RefreshFamous();

            Section(scroll, "朝代（不选 = 全部）");
            AddChips(scroll, Services.Content.GetDynasties(), d => d, _selDyn);
            Section(scroll, "难度（不选 = 全部）");
            AddChips(scroll, Services.Content.GetDifficultyTiers(), QuizConfigScreen.TierLabel, _selDiff);

            var hist = UiKit.Button("Hist", scroll, "历史记录", out _, UiKit.CardAlt, 32);
            UiKit.Pref(hist.gameObject, minH: 92);
            hist.onClick.AddListener(() => Nav.Push<RecordsScreen>(new RecordsArgs { Mode = "slide", Title = "找诗记录" }));

            var start = UiKit.Button("Start", body, "开始", out var sl, UiKit.Accent, 40);
            sl.color = Color.white;
            var le = UiKit.Pref(start.gameObject, minH: 120);
            le.flexibleHeight = 0f;
            start.onClick.AddListener(StartGame);
        }

        private void RestoreLast()
        {
            var s = Services.Settings != null ? Services.Settings.Current : null;
            if (s == null) return;
            if (s.LastSlideLevel >= 1 && s.LastSlideLevel <= 4) _level = s.LastSlideLevel;
            if (s.LastSlideCols >= 4) _cols = s.LastSlideCols;
            if (s.LastSlideRows >= 4) _rows = s.LastSlideRows;
            _overlap = s.LastSlideOverlap;
            _famousOnly = s.LastSlideFamousOnly;
            _selDiff.Clear();
            if (s.LastSlideDifficulties != null) foreach (var t in s.LastSlideDifficulties) _selDiff.Add(t);
            _selDyn.Clear();
            if (s.LastSlideDynasties != null) foreach (var d in s.LastSlideDynasties) _selDyn.Add(d);
        }

        private void StartGame()
        {
            var diffs = new List<int>(_selDiff);
            var dyns = new List<string>(_selDyn);
            var s = Services.Settings != null ? Services.Settings.Current : null;
            if (s != null)
            {
                s.LastSlideLevel = _level; s.LastSlideCols = _cols; s.LastSlideRows = _rows;
                s.LastSlideOverlap = _overlap; s.LastSlideFamousOnly = _famousOnly;
                s.LastSlideDifficulties = diffs; s.LastSlideDynasties = dyns;
                _ = Services.Settings.SaveAsync();
            }
            Nav.Push<SlidePuzzleScreen>(new SlideStartArgs
            {
                DirectionLevel = _level, GridCols = _cols, GridRows = _rows, AllowOverlap = _overlap,
                FamousOnly = _famousOnly, Difficulties = diffs, Dynasties = dyns,
            });
        }

        private void AddChips<T>(Transform parent, List<T> items, System.Func<T, string> label, HashSet<T> selected)
        {
            if (items == null || items.Count == 0)
            {
                UiKit.Text("none", parent, "（无）", 26, TextAlignmentOptions.Center, UiKit.Muted);
                return;
            }
            Transform row = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % 3 == 0)
                {
                    var p = UiKit.Panel("Row", parent);
                    UiKit.Pref(p, minH: 90);
                    UiKit.HorizontalGroup(p, spacing: 12);
                    row = p.transform;
                }
                var item = items[i];
                var b = UiKit.Button("Chip", row, label(item), out var lbl, UiKit.Card, 28);
                UiKit.SetChipSelected(b, lbl, selected.Contains(item));
                b.onClick.AddListener(() =>
                {
                    if (!selected.Remove(item)) selected.Add(item);
                    UiKit.SetChipSelected(b, lbl, selected.Contains(item));
                });
            }
        }

        private void Section(Transform parent, string text)
        {
            var t = UiKit.Text("Sec", parent, text, 30, TextAlignmentOptions.Left, UiKit.Accent);
            UiKit.MinHeight(t.gameObject, 56);
        }

        private void BuildLevelRows(Transform parent)
        {
            _levelBtns.Clear(); _levelLbls.Clear();
            Transform row = null;
            for (int i = 0; i < Levels.Length; i++)
            {
                if (i % 2 == 0)
                {
                    var p = UiKit.Panel("Row", parent);
                    UiKit.Pref(p, minH: 100);
                    UiKit.HorizontalGroup(p, spacing: 12);
                    row = p.transform;
                }
                int lv = Levels[i].level;
                var b = UiKit.Button("L" + lv, row, Levels[i].label, out var lbl, UiKit.Card, 30);
                _levelBtns.Add(b); _levelLbls.Add(lbl);
                b.onClick.AddListener(() => { _level = lv; RefreshLevel(); });
            }
            RefreshLevel();
        }

        private void RefreshLevel()
        {
            for (int i = 0; i < _levelBtns.Count; i++)
                UiKit.SetChipSelected(_levelBtns[i], _levelLbls[i], Levels[i].level == _level);
        }

        private void BuildGridRow(Transform parent)
        {
            _sizeBtns.Clear(); _sizeLbls.Clear();
            Transform row = null;
            for (int i = 0; i < Grids.Length; i++)
            {
                if (i % 3 == 0)
                {
                    var p = UiKit.Panel("GridRow", parent);
                    UiKit.Pref(p, minH: 100);
                    UiKit.HorizontalGroup(p, spacing: 12);
                    row = p.transform;
                }
                int cols = Grids[i].cols, rows = Grids[i].rows;
                var b = UiKit.Button("G" + i, row, Grids[i].label, out var lbl, UiKit.Card, 28);
                _sizeBtns.Add(b); _sizeLbls.Add(lbl);
                b.onClick.AddListener(() => { _cols = cols; _rows = rows; RefreshSize(); });
            }
            RefreshSize();
        }

        private void RefreshSize()
        {
            for (int i = 0; i < _sizeBtns.Count; i++)
                UiKit.SetChipSelected(_sizeBtns[i], _sizeLbls[i], Grids[i].cols == _cols && Grids[i].rows == _rows);
        }

        private void RefreshOverlap()
        {
            _overlapLbl.text = _overlap ? "允许重叠字：开" : "允许重叠字：关";
            UiKit.SetChipSelected(_overlapBtn, _overlapLbl, _overlap);
        }

        private void RefreshFamous()
        {
            _famousLbl.text = _famousOnly ? "仅名句：开" : "仅名句：关";
            UiKit.SetChipSelected(_famousBtn, _famousLbl, _famousOnly);
        }
    }
}
