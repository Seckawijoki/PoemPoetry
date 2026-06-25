using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>滑动找诗设置 (按图索骥): 方向难度 L1-L4 + 网格 + 重叠/名句 + 朝代/难度 → 开始.</summary>
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
        // 横(列)、竖(行) 独立可选；竖向可更高用作进阶难度。
        private static readonly int[] ColsOpts = { 8, 9, 10 };
        private static readonly int[] RowsOpts = { 8, 9, 10, 12, 14, 16 };

        private readonly List<Button> _levelBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _levelLbls = new List<TextMeshProUGUI>();
        private readonly List<Button> _colsBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _colsLbls = new List<TextMeshProUGUI>();
        private readonly List<Button> _rowsBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _rowsLbls = new List<TextMeshProUGUI>();
        private Button _overlapBtn;
        private TextMeshProUGUI _overlapLbl;
        private Button _famousBtn;
        private TextMeshProUGUI _famousLbl;
        private readonly HashSet<int> _selDiff = new HashSet<int>();
        private readonly HashSet<string> _selDyn = new HashSet<string>();
        private readonly HashSet<string> _selType = new HashSet<string>();

        protected override void OnShow(object args)
        {
            var body = Design.Chrome(gameObject, () => Nav.Pop(), () => Nav.Push<SettingsScreen>(), "划线寻踪");
            UiKit.VerticalGroup(body.gameObject, spacing: 14, padX: 28, padY: 16, align: TextAnchor.UpperCenter);

            RestoreLast();

            var scroll = UiKit.ScrollList("Cfg", body, out _);

            var card = Design.Card(scroll);
            Design.SectionHead(card, "方向难度");
            BuildLevelRows(card);

            Design.SectionHead(card, "横向格数");
            BuildSizeRow(card, ColsOpts, _colsBtns, _colsLbls, isCols: true);
            Design.SectionHead(card, "纵向格数");
            BuildSizeRow(card, RowsOpts, _rowsBtns, _rowsLbls, isCols: false);

            Design.SectionHead(card, "选项");
            var optRow = UiKit.Panel("OptRow", card);
            UiKit.Pref(optRow, minH: 96);
            UiKit.HorizontalGroup(optRow, spacing: 14);
            _overlapBtn = UiKit.Button("Overlap", optRow.transform, "", out _overlapLbl, Design.SurfaceHigh, 30);
            _overlapBtn.onClick.AddListener(() => { _overlap = !_overlap; RefreshOverlap(); });
            RefreshOverlap();
            _famousBtn = UiKit.Button("Famous", optRow.transform, "", out _famousLbl, Design.SurfaceHigh, 30);
            _famousBtn.onClick.AddListener(() => { _famousOnly = !_famousOnly; RefreshFamous(); });
            RefreshFamous();

            Design.SectionHead(card, "朝代筛选（不选 = 全部）");
            var dyn = Services.Content.GetDynasties();
            AddChips(card, dyn, d => d, _selDyn, Mathf.CeilToInt(dyn.Count / 2f));
            Design.SectionHead(card, "体裁范畴（不选 = 全部）");
            AddChips(card, Services.Content.GetTypes(), tp => tp, _selType);
            Design.SectionHead(card, "难易程度（不选 = 全部）");
            var tiers = new List<int> { 0, 1, 2, 3 };
            AddChips(card, tiers, QuizConfigScreen.TierLabel, _selDiff, tiers.Count);

            var hist = UiKit.Button("Hist", scroll, "历史记录", out var histLbl, Design.SurfaceHigh, 32);
            histLbl.color = Design.Ink;
            UiKit.Pref(hist.gameObject, minH: 92);
            hist.onClick.AddListener(() => Nav.Push<RecordsScreen>(new RecordsArgs { Mode = "slide", Title = "找诗记录" }));

            var start = Design.PrimaryButton("Start", body, "开始", out _, 42);
            var le = UiKit.Pref(start.gameObject, minH: 120);
            le.flexibleHeight = 0f;
            start.onClick.AddListener(StartGame);
        }

        private void RestoreLast()
        {
            var s = Services.Settings != null ? Services.Settings.Current : null;
            if (s == null) return;
            if (s.LastSlideLevel >= 1 && s.LastSlideLevel <= 4) _level = s.LastSlideLevel;
            _cols = ClampToOpts(s.LastSlideCols, ColsOpts, 9);
            _rows = ClampToOpts(s.LastSlideRows, RowsOpts, 9);
            _overlap = s.LastSlideOverlap;
            _famousOnly = s.LastSlideFamousOnly;
            _selDiff.Clear();
            if (s.LastSlideDifficulties != null) foreach (var t in s.LastSlideDifficulties) _selDiff.Add(t);
            _selDyn.Clear();
            if (s.LastSlideDynasties != null) foreach (var d in s.LastSlideDynasties) _selDyn.Add(d);
            _selType.Clear();
            if (s.LastSlideTypes != null) foreach (var t in s.LastSlideTypes) _selType.Add(t);
        }

        // Snap a stored size to the nearest available option (old presets like 12×16 are gone).
        private static int ClampToOpts(int value, int[] opts, int fallback)
        {
            int best = fallback, bestDist = int.MaxValue;
            foreach (var o in opts)
            {
                int d = System.Math.Abs(o - value);
                if (d < bestDist) { bestDist = d; best = o; }
            }
            return best;
        }

        private void StartGame()
        {
            var diffs = new List<int>(_selDiff);
            var dyns = new List<string>(_selDyn);
            var types = new List<string>(_selType);
            var s = Services.Settings != null ? Services.Settings.Current : null;
            if (s != null)
            {
                s.LastSlideLevel = _level; s.LastSlideCols = _cols; s.LastSlideRows = _rows;
                s.LastSlideOverlap = _overlap; s.LastSlideFamousOnly = _famousOnly;
                s.LastSlideDifficulties = diffs; s.LastSlideDynasties = dyns; s.LastSlideTypes = types;
                _ = Services.Settings.SaveAsync();
            }
            Nav.Push<SlidePuzzleScreen>(new SlideStartArgs
            {
                DirectionLevel = _level, GridCols = _cols, GridRows = _rows, AllowOverlap = _overlap,
                FamousOnly = _famousOnly, Difficulties = diffs, Dynasties = dyns, Types = types,
            });
        }

        private void AddChips<T>(Transform parent, List<T> items, System.Func<T, string> label, HashSet<T> selected, int perRow = 3)
        {
            if (items == null || items.Count == 0)
            {
                UiKit.Text("none", parent, "（无）", 26, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
                return;
            }
            Transform row = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (i % perRow == 0)
                {
                    var p = UiKit.Panel("Row", parent);
                    UiKit.Pref(p, minH: 90);
                    UiKit.HorizontalGroup(p, spacing: 14);
                    row = p.transform;
                }
                var item = items[i];
                var b = UiKit.Button("Chip", row, label(item), out var lbl, Design.SurfaceHigh, 28);
                Design.SetChip(b, lbl, selected.Contains(item));
                b.onClick.AddListener(() =>
                {
                    if (!selected.Remove(item)) selected.Add(item);
                    Design.SetChip(b, lbl, selected.Contains(item));
                });
            }
        }

        private void BuildLevelRows(Transform parent)
        {
            _levelBtns.Clear(); _levelLbls.Clear();
            var row = UiKit.Panel("LevelRow", parent);
            UiKit.Pref(row, minH: 100);
            UiKit.HorizontalGroup(row, spacing: 10);
            for (int i = 0; i < Levels.Length; i++)
            {
                int lv = Levels[i].level;
                var b = UiKit.Button("L" + lv, row.transform, Levels[i].label, out var lbl, Design.SurfaceHigh, 26);
                _levelBtns.Add(b); _levelLbls.Add(lbl);
                b.onClick.AddListener(() => { _level = lv; RefreshLevel(); });
            }
            RefreshLevel();
        }

        private void RefreshLevel()
        {
            for (int i = 0; i < _levelBtns.Count; i++)
                Design.SetChip(_levelBtns[i], _levelLbls[i], Levels[i].level == _level);
        }

        private void BuildSizeRow(Transform parent, int[] opts, List<Button> btns, List<TextMeshProUGUI> lbls, bool isCols)
        {
            btns.Clear(); lbls.Clear();
            var p = UiKit.Panel("SizeRow", parent);
            UiKit.Pref(p, minH: 100);
            UiKit.HorizontalGroup(p, spacing: 12);
            for (int i = 0; i < opts.Length; i++)
            {
                int v = opts[i];
                var b = UiKit.Button("S" + (isCols ? "C" : "R") + v, p.transform, v.ToString(), out var lbl, Design.SurfaceHigh, 30);
                btns.Add(b); lbls.Add(lbl);
                b.onClick.AddListener(() => { if (isCols) _cols = v; else _rows = v; RefreshSizes(); });
            }
            RefreshSizes();
        }

        private void RefreshSizes()
        {
            for (int i = 0; i < _colsBtns.Count; i++)
                Design.SetChip(_colsBtns[i], _colsLbls[i], ColsOpts[i] == _cols);
            for (int i = 0; i < _rowsBtns.Count; i++)
                Design.SetChip(_rowsBtns[i], _rowsLbls[i], RowsOpts[i] == _rows);
        }

        private void RefreshOverlap()
        {
            _overlapLbl.text = _overlap ? "允许重叠字：开" : "允许重叠字：关";
            Design.SetChip(_overlapBtn, _overlapLbl, _overlap);
        }

        private void RefreshFamous()
        {
            _famousLbl.text = _famousOnly ? "仅名句：开" : "仅名句：关";
            Design.SetChip(_famousBtn, _famousLbl, _famousOnly);
        }
    }
}
