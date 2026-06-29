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
        private int _lineCount = 5;
        private bool _overlap = false;
        private bool _overlapHint = true;
        private bool _famousOnly = false;

        private static readonly (int level, string label)[] Levels =
        {
            (1, "横竖直线"),
            (2, "横竖斜线"),
            (3, "横竖蛇形"),
            (4, "全向蛇形"),
        };
        // 横、纵均为滑动条；横向 8~10，纵向 8~16；诗句数 3~15。
        private const int ColsMin = 8, ColsMax = 10;
        private const int RowsMin = 8, RowsMax = 16;
        private const int LineMin = 3, LineMax = 15;

        private readonly List<Button> _levelBtns = new List<Button>();
        private readonly List<TextMeshProUGUI> _levelLbls = new List<TextMeshProUGUI>();
        private Button _overlapBtn;
        private TextMeshProUGUI _overlapLbl;
        private Button _overlapHintBtn;
        private TextMeshProUGUI _overlapHintLbl;
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

            Design.SectionHead(card, "网格大小（横 × 纵）");
            var sizeRow = UiKit.Panel("SizeRow", card);
            UiKit.Pref(sizeRow, minH: 150);
            UiKit.HorizontalGroup(sizeRow, spacing: 24);
            BuildSliderCell(sizeRow.transform, "横向 {0}", ColsMin, ColsMax, _cols, v => _cols = v);
            BuildSliderCell(sizeRow.transform, "纵向 {0}", RowsMin, RowsMax, _rows, v => _rows = v);

            Design.SectionHead(card, "诗句数量（越多时间越长）");
            BuildSlider(card, "{0} 句诗", LineMin, LineMax, _lineCount, v => _lineCount = v);

            Design.SectionHead(card, "选项");
            var optRow = UiKit.Panel("OptRow", card);
            UiKit.Pref(optRow, minH: 96);
            UiKit.HorizontalGroup(optRow, spacing: 12);
            _overlapBtn = UiKit.Button("Overlap", optRow.transform, "", out _overlapLbl, Design.SurfaceHigh, 26);
            _overlapBtn.onClick.AddListener(() => { _overlap = !_overlap; RefreshOverlap(); });
            RefreshOverlap();
            _overlapHintBtn = UiKit.Button("OverlapHint", optRow.transform, "", out _overlapHintLbl, Design.SurfaceHigh, 26);
            _overlapHintBtn.onClick.AddListener(() => { _overlapHint = !_overlapHint; RefreshOverlapHint(); });
            RefreshOverlapHint();
            _famousBtn = UiKit.Button("Famous", optRow.transform, "", out _famousLbl, Design.SurfaceHigh, 26);
            _famousBtn.onClick.AddListener(() => { _famousOnly = !_famousOnly; RefreshFamous(); });
            RefreshFamous();

            Design.SectionHead(card, "朝代筛选（不选 = 全部）");
            var dynFacets = Services.Content.GetDynastyFacets();
            FacetChips.BuildDynasty(card, dynFacets, _selDyn, null, Mathf.CeilToInt(dynFacets.Count / 2f));
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
            _cols = Mathf.Clamp(s.LastSlideCols, ColsMin, ColsMax);
            _rows = Mathf.Clamp(s.LastSlideRows, RowsMin, RowsMax);
            _lineCount = Mathf.Clamp(s.LastSlideLineCount, LineMin, LineMax);
            _overlap = s.LastSlideOverlap;
            _overlapHint = s.LastSlideOverlapHint;
            _famousOnly = s.LastSlideFamousOnly;
            _selDiff.Clear();
            if (s.LastSlideDifficulties != null) foreach (var t in s.LastSlideDifficulties) _selDiff.Add(t);
            _selDyn.Clear();
            if (s.LastSlideDynasties != null) foreach (var d in s.LastSlideDynasties) _selDyn.Add(d);
            _selType.Clear();
            if (s.LastSlideTypes != null) foreach (var t in s.LastSlideTypes) _selType.Add(t);
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
                s.LastSlideLineCount = _lineCount;
                s.LastSlideOverlap = _overlap; s.LastSlideOverlapHint = _overlapHint;
                s.LastSlideFamousOnly = _famousOnly;
                s.LastSlideDifficulties = diffs; s.LastSlideDynasties = dyns; s.LastSlideTypes = types;
                _ = Services.Settings.SaveAsync();
            }
            Nav.Push<SlidePuzzleScreen>(new SlideStartArgs
            {
                DirectionLevel = _level, GridCols = _cols, GridRows = _rows, LineCount = _lineCount,
                AllowOverlap = _overlap, OverlapHint = _overlapHint,
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

        // A labeled slider row: value text on the left, draggable track filling the rest.
        private void BuildSlider(Transform parent, string fmt, int min, int max, int value, System.Action<int> onChange)
        {
            var p = UiKit.Panel("SliderRow", parent);
            UiKit.Pref(p, minH: 110);
            UiKit.HorizontalGroup(p, spacing: 18);
            var valLbl = UiKit.Text("Val", p.transform, string.Format(fmt, value), 30, TextAlignmentOptions.Center, Design.Ink);
            var vle = valLbl.gameObject.AddComponent<LayoutElement>();
            vle.minWidth = 150; vle.preferredWidth = 150; vle.flexibleWidth = 0f;
            var slider = UiKit.Slider("S", p.transform, min, max, value);
            var sle = slider.gameObject.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f; sle.minHeight = 60;
            slider.onValueChanged.AddListener(v =>
            {
                int iv = Mathf.RoundToInt(v);
                onChange(iv);
                valLbl.text = string.Format(fmt, iv);
            });
        }

        // A compact labeled slider stacked vertically (value text above the track), for placing two
        // sliders side-by-side in one row.
        private void BuildSliderCell(Transform parent, string fmt, int min, int max, int value, System.Action<int> onChange)
        {
            var cell = UiKit.Panel("Cell", parent);
            var vg = UiKit.VerticalGroup(cell, spacing: 8, padX: 0, padY: 0, align: TextAnchor.UpperCenter);
            vg.childForceExpandHeight = false;
            var valLbl = UiKit.Text("Val", cell.transform, string.Format(fmt, value), 30, TextAlignmentOptions.Center, Design.Ink);
            UiKit.Pref(valLbl.gameObject, minH: 44);
            var slider = UiKit.Slider("S", cell.transform, min, max, value);
            UiKit.Pref(slider.gameObject, minH: 56);
            slider.onValueChanged.AddListener(v =>
            {
                int iv = Mathf.RoundToInt(v);
                onChange(iv);
                valLbl.text = string.Format(fmt, iv);
            });
        }

        private void RefreshOverlap()
        {
            _overlapLbl.text = _overlap ? "重叠字 开" : "重叠字 关";
            Design.SetChip(_overlapBtn, _overlapLbl, _overlap);
        }

        private void RefreshOverlapHint()
        {
            _overlapHintLbl.text = _overlapHint ? "重叠提示 开" : "重叠提示 关";
            Design.SetChip(_overlapHintBtn, _overlapHintLbl, _overlapHint);
        }

        private void RefreshFamous()
        {
            _famousLbl.text = _famousOnly ? "仅名句 开" : "仅名句 关";
            Design.SetChip(_famousBtn, _famousLbl, _famousOnly);
        }
    }
}
