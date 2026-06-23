using System.Collections.Generic;
using System.Globalization;
using PoemPoetry.Data;
using PoemPoetry.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// 滑动找诗 (grid word-search). Plays a fresh game or replays a snapshot (review/practice, not
    /// recorded). Straight levels (L1/L2) snap the drag to a straight line for easy diagonals;
    /// snake levels (L3/L4) accumulate the traced path. Grids may be non-square (taller).
    /// </summary>
    public sealed class SlidePuzzleScreen : UIScreen
    {
        private const float WidthBudget = 1000f;
        private const float HeightBudget = 1400f;
        private const float Spacing = 6f;
        private static readonly Color RevealColor = new Color(0.92f, 0.74f, 0.30f);

        private readonly System.Random _rng = new System.Random();

        private SlideStartArgs _args;
        private int _level = 1;
        private int _cols = 9;
        private int _rows = 9;
        private bool _overlap;
        private bool _replay;
        private bool _straight;     // straight-line snap selection (L1/L2)
        private GridWordSearch _game;

        private TextMeshProUGUI _timerText;
        private TextMeshProUGUI _statusText;
        private RectTransform _gridPanel;
        private GridSelector _selector;
        private float _cellSize;

        private readonly List<Image> _cellBg = new List<Image>();
        private readonly List<TextMeshProUGUI> _cellTxt = new List<TextMeshProUGUI>();
        private readonly List<int> _path = new List<int>();
        private readonly HashSet<int> _foundCells = new HashSet<int>();
        private int _startCell = -1;

        private float _endTime;
        private float _totalSeconds;
        private bool _running;
        private bool _practice;
        private GameObject _resultOverlay;

        private Color PathColor => _practice ? RevealColor : UiKit.CardAlt;

        protected override void OnShow(object args)
        {
            _args = args as SlideStartArgs ?? new SlideStartArgs();
            _replay = _args.Replay != null;
            _level = _args.DirectionLevel;
            if (_replay)
            {
                _cols = _args.Replay.Size > 0 ? _args.Replay.Size : 9;
                _rows = _args.Replay.Rows > 0 ? _args.Replay.Rows : _cols;
            }
            else { _cols = _args.GridCols; _rows = _args.GridRows; }
            _overlap = _args.AllowOverlap;
            BuildChrome();
            if (_replay) LoadReplay(_args.Replay);
            else NewGame();
        }

        private void BuildChrome()
        {
            var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            bg.color = UiKit.Paper;

            var back = new GameObject("Back", typeof(RectTransform), typeof(Image), typeof(Button));
            back.transform.SetParent(transform, false);
            var brt = UiKit.Rect(back);
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(0, 1); brt.pivot = new Vector2(0, 1);
            brt.anchoredPosition = new Vector2(24, -24); brt.sizeDelta = new Vector2(150, 72);
            back.GetComponent<Image>().color = UiKit.CardAlt;
            var bl = UiKit.Text("L", back.transform, "返回", 32, TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.StretchFull(bl.gameObject, 6);
            back.GetComponent<Button>().onClick.AddListener(() => Nav.Pop());

            var title = UiKit.Text("Title", transform, _replay ? "找诗回看" : "滑动找诗", 50, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.AnchorTop(title.gameObject, 84, 24, 200);

            _timerText = UiKit.Text("Timer", transform, "", 38, TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.AnchorTop(_timerText.gameObject, 56, 118, 30);

            _statusText = UiKit.Text("Status", transform, "", 26, TextAlignmentOptions.Center, UiKit.Muted);
            UiKit.AnchorTop(_statusText.gameObject, 46, 178, 30);

            _gridPanel = MakeGrid();
        }

        private RectTransform MakeGrid()
        {
            _cellSize = Mathf.Min((WidthBudget - (_cols - 1) * Spacing) / _cols,
                                  (HeightBudget - (_rows - 1) * Spacing) / _rows);
            float w = _cols * _cellSize + (_cols - 1) * Spacing;
            float h = _rows * _cellSize + (_rows - 1) * Spacing;
            var panel = new GameObject("Grid", typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup), typeof(GridSelector));
            panel.transform.SetParent(transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, -70);
            rt.sizeDelta = new Vector2(w, h);
            panel.GetComponent<Image>().color = new Color(0, 0, 0, 0.03f);

            var glg = panel.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(_cellSize, _cellSize);
            glg.spacing = new Vector2(Spacing, Spacing);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = _cols;
            glg.childAlignment = TextAnchor.UpperCenter;

            _selector = panel.GetComponent<GridSelector>();
            _selector.Down = OnCellDown;
            _selector.Move = OnCellMove;
            _selector.Up = OnCellUp;
            return rt;
        }

        private void NewGame()
        {
            if (_resultOverlay != null) { Destroy(_resultOverlay); _resultOverlay = null; }
            _game = new GridWordSearch(_cols, _rows, _level, _overlap, new SystemRandomSource(_rng.Next()));
            _straight = !_game.Snake;

            var poems = FilteredPoems();
            int maxLine = Mathf.Max(_cols, _rows);
            int target = Mathf.Clamp((_cols * _rows) / 24, 4, 9);
            var used = new HashSet<string>();
            int placed = 0, guard = 0;
            while (placed < target && guard++ < 1000 && poems.Count > 0)
            {
                var p = poems[_rng.Next(poems.Count)];
                if (p.Lines.Count == 0) continue;
                var l = p.Lines[_rng.Next(p.Lines.Count)];
                if (l.CharCount < 2 || l.CharCount > maxLine) continue;
                if (!used.Add(l.Text)) continue;
                if (_game.TryPlace(l.Text, SplitChars(l.Text), p.Title, p.Id)) placed++;
            }

            var pool = new List<string>();
            foreach (var p in Services.Content.Poems)
                foreach (var l in p.Lines)
                    foreach (var ch in SplitChars(l.Text)) pool.Add(ch);
            _game.FillEmpty(pool);

            RenderGrid();
            _totalSeconds = 90f + _level * 15f + (_cols + _rows) * 2f;
            _endTime = Time.unscaledTime + _totalSeconds;
            _running = true;
            _practice = false;
            UpdateStatus();
        }

        private List<Poem> FilteredPoems()
        {
            var diff = _args.Difficulties ?? new List<int>();
            var dyn = _args.Dynasties ?? new List<string>();
            var result = new List<Poem>();
            foreach (var p in Services.Content.Poems)
            {
                if (dyn.Count > 0 && !dyn.Contains(p.Dynasty)) continue;
                if (diff.Count > 0 && !diff.Contains(p.Difficulty)) continue;
                result.Add(p);
            }
            if (result.Count == 0)
                foreach (var p in Services.Content.Poems) result.Add(p);
            return result;
        }

        private void LoadReplay(SlideSnapshot snap)
        {
            _game = GridWordSearch.FromSnapshot(snap);
            _straight = false; // replay uses 8-dir accumulation so any placed path is traceable
            RenderGrid();
            _running = false;
            _practice = true;
            _timerText.text = "回看";
            UpdateStatus();
            AddRevealButton();
        }

        private void RenderGrid()
        {
            UiKit.ClearChildren(_gridPanel);
            _cellBg.Clear(); _cellTxt.Clear(); _selector.Cells.Clear();
            _path.Clear(); _foundCells.Clear(); _startCell = -1;

            int fontSize = Mathf.RoundToInt(_cellSize * 0.5f);
            for (int i = 0; i < _game.Cells.Length; i++)
            {
                var cell = new GameObject("C" + i, typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(_gridPanel, false);
                var img = cell.GetComponent<Image>();
                img.color = UiKit.Card;
                var t = UiKit.Text("t", cell.transform, _game.Cells[i] ?? "", fontSize, TextAlignmentOptions.Center, UiKit.Ink);
                UiKit.StretchFull(t.gameObject, 2);
                _cellBg.Add(img);
                _cellTxt.Add(t);
                _selector.Cells.Add(cell.GetComponent<RectTransform>());
            }
            foreach (var tg in _game.Targets)
                if (tg.Found)
                    foreach (var idx in tg.Cells)
                    {
                        _foundCells.Add(idx);
                        _cellBg[idx].color = UiKit.Good;
                        _cellTxt[idx].color = Color.white;
                    }
        }

        // ----- selection -----

        private void OnCellDown(int cell)
        {
            if ((!_running && !_practice) || cell < 0) return;
            _startCell = cell;
            ClearPath();
            _path.Add(cell);
            Paint(cell, PathColor);
        }

        private void OnCellMove(int cell)
        {
            if ((!_running && !_practice) || cell < 0 || _startCell < 0) return;
            if (_straight)
            {
                SetHighlight(_game.StraightPath(_startCell, cell));
                return;
            }
            // snake: accumulate
            int last = _path.Count > 0 ? _path[_path.Count - 1] : _startCell;
            if (cell == last) return;
            if (_path.Count >= 2 && cell == _path[_path.Count - 2])
            {
                Repaint(last);
                _path.RemoveAt(_path.Count - 1);
                return;
            }
            if (_path.Contains(cell)) return;
            if (!_game.Adjacent(last, cell)) return;
            _path.Add(cell);
            Paint(cell, PathColor);
        }

        private void OnCellUp()
        {
            _startCell = -1;
            if (!_running && !_practice) { ClearPath(); return; }
            var found = _game.TryMatch(_path);
            if (found != null)
            {
                foreach (var idx in found.Cells)
                {
                    _foundCells.Add(idx);
                    _cellBg[idx].color = UiKit.Good;
                    _cellTxt[idx].color = Color.white;
                }
                _path.Clear();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayCorrect();
                UpdateStatus();
                if (_running && _game.AllFound()) EndGame();
                return;
            }
            if (AudioManager.Instance != null && _path.Count >= 2) AudioManager.Instance.PlayWrong();
            ClearPath();
        }

        private void SetHighlight(List<int> path)
        {
            ClearPath();
            foreach (var idx in path) { _path.Add(idx); Paint(idx, PathColor); }
        }

        private void Paint(int idx, Color c) { if (!_foundCells.Contains(idx)) _cellBg[idx].color = c; }
        private void Repaint(int idx) { if (!_foundCells.Contains(idx)) _cellBg[idx].color = UiKit.Card; }
        private void ClearPath()
        {
            foreach (var idx in _path) Repaint(idx);
            _path.Clear();
        }

        private void UpdateStatus()
        {
            int found = 0;
            foreach (var t in _game.Targets) if (t.Found) found++;
            string tail = _practice ? "练习中 · 不计入记录" : ("滑动选取（" + LevelHint() + "）");
            _statusText.text = $"已找到 {found}/{_game.Targets.Count} 句  ·  {tail}";
        }

        private string LevelHint() =>
            _level == 1 ? "横竖" : _level == 2 ? "横竖斜" : _level == 3 ? "横竖蛇形" : "全向蛇形";

        private void Update()
        {
            if (!_running) return;
            float remain = _endTime - Time.unscaledTime;
            if (remain <= 0f) { _timerText.text = "时间 0:00"; EndGame(); return; }
            _timerText.text = $"时间 {(int)remain / 60}:{(int)remain % 60:00}";
        }

        private void EndGame()
        {
            if (!_running) return;
            _running = false;
            SaveRecord();
            ShowResult();
        }

        private void SaveRecord()
        {
            var items = new List<QuestionResult>();
            foreach (var t in _game.Targets)
                items.Add(new QuestionResult
                {
                    PoemId = t.PoemId, CorrectText = t.Text, BlankedText = t.Text,
                    ChosenText = t.Found ? t.Text : "", IsCorrect = t.Found,
                });
            var settings = new ChallengeSettings
            {
                QuestionCount = _game.Targets.Count,
                Difficulties = _args.Difficulties ?? new List<int>(),
                Dynasties = _args.Dynasties ?? new List<string>(),
            };
            int dur = Mathf.RoundToInt(_totalSeconds - Mathf.Max(0f, _endTime - Time.unscaledTime));
            _ = Services.Records.SaveCompletedAsync(items, settings, dur, "slide", BuildSnapshot());
        }

        private SlideSnapshot BuildSnapshot()
        {
            var snap = new SlideSnapshot { Size = _cols, Rows = _rows };
            foreach (var c in _game.Cells) snap.Cells.Add(c);
            foreach (var t in _game.Targets)
            {
                var ts = new SlideTargetSnapshot { Text = t.Text, Title = t.Title, PoemId = t.PoemId, Found = t.Found };
                foreach (var idx in t.Cells) ts.Cells.Add(idx);
                snap.Targets.Add(ts);
            }
            return snap;
        }

        private void RevealUnfound()
        {
            foreach (var t in _game.Targets)
                if (!t.Found)
                    foreach (var idx in t.Cells)
                        if (!_foundCells.Contains(idx)) _cellBg[idx].color = RevealColor;
        }

        private void AddRevealButton()
        {
            var b = new GameObject("Reveal", typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(transform, false);
            var rt = UiKit.Rect(b);
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 40); rt.sizeDelta = new Vector2(420, 96);
            b.GetComponent<Image>().color = UiKit.CardAlt;
            var l = UiKit.Text("L", b.transform, "显示未滑出的诗句", 30, TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.StretchFull(l.gameObject, 6);
            b.GetComponent<Button>().onClick.AddListener(RevealUnfound);
        }

        private void ShowResult()
        {
            _resultOverlay = UiKit.Panel("Result", transform, new Color(0, 0, 0, 0.78f));
            UiKit.StretchFull(_resultOverlay);

            var box = UiKit.Panel("Box", _resultOverlay.transform, UiKit.Paper);
            var brt = UiKit.Rect(box);
            brt.anchorMin = new Vector2(0.5f, 0.5f); brt.anchorMax = new Vector2(0.5f, 0.5f); brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(940, 1240); brt.anchoredPosition = Vector2.zero;
            UiKit.VerticalGroup(box, spacing: 14, padX: 30, padY: 30, align: TextAnchor.UpperCenter);

            int found = 0;
            foreach (var t in _game.Targets) if (t.Found) found++;
            var head = UiKit.Text("H", box.transform, $"找到 {found}/{_game.Targets.Count} 句", 48, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.MinHeight(head.gameObject, 90);

            var list = UiKit.ScrollList("L", box.transform, out _);
            foreach (var t in _game.Targets)
            {
                string color = t.Found ? "#2E8B47" : "#BF4038";
                string mark = t.Found ? "对" : "错";
                var row = UiKit.Text("R", list, $"<color={color}>[{mark}] {t.Text} · {t.Title}</color>", 32, TextAlignmentOptions.Left, UiKit.Ink);
                UiKit.Pref(row.gameObject, minH: 72);
            }

            var btns = UiKit.Panel("B", box.transform);
            UiKit.Pref(btns, minH: 110).flexibleHeight = 0f;
            var hg = UiKit.HorizontalGroup(btns, spacing: 12);
            hg.childForceExpandHeight = false;

            var keep = UiKit.Button("Keep", btns.transform, "继续查看", out _, UiKit.CardAlt, 30);
            UiKit.Pref(keep.gameObject, minH: 110);
            keep.onClick.AddListener(() => { CloseResult(); _practice = true; UpdateStatus(); });

            var reveal = UiKit.Button("Reveal", btns.transform, "显示未滑出", out _, UiKit.CardAlt, 30);
            UiKit.Pref(reveal.gameObject, minH: 110);
            reveal.onClick.AddListener(() => { CloseResult(); _practice = true; RevealUnfound(); UpdateStatus(); });

            var again = UiKit.Button("Again", btns.transform, "再来一局", out var al, UiKit.Accent, 30);
            UiKit.Pref(again.gameObject, minH: 110);
            al.color = Color.white;
            again.onClick.AddListener(NewGame);

            var home = UiKit.Button("Home", btns.transform, "返回", out _, UiKit.CardAlt, 30);
            UiKit.Pref(home.gameObject, minH: 110);
            home.onClick.AddListener(() => Nav.Pop());
        }

        private void CloseResult()
        {
            if (_resultOverlay != null) { Destroy(_resultOverlay); _resultOverlay = null; }
        }

        private static string[] SplitChars(string s)
        {
            var clean = RhymeService.StripPunct(s);
            var si = new StringInfo(clean);
            int n = si.LengthInTextElements;
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = si.SubstringByTextElements(i, 1);
            return result;
        }
    }
}
