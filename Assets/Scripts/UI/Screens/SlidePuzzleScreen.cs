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
        // Canvas reference width is 1080; keep a comfortable side margin (grid frame adds +28).
        private const float WidthBudget = 920f;
        private const float HeightBudget = 1400f;
        private const float Spacing = 6f;
        private static readonly Color RevealColor = new Color(0.92f, 0.74f, 0.30f);
        private static readonly Color SlidBodyColor = new Color(0.78f, 0.87f, 0.96f); // 滑过普通字（统一浅蓝）
        private static readonly Color OverFoundColor = new Color(0.58f, 0.80f, 0.58f); // 划过已找到的字（浅绿）
        private static readonly Color HintFlashColor = new Color(0.98f, 0.85f, 0.32f); // 提示闪烁
        // 按「该格被找到的交叉次数」着色：1 次=绿（默认最常见），2 次起依次取后续颜色。
        private static readonly Color[] CountColors =
        {
            UiKit.Good,                     // 1 次：绿
            new Color(0.16f, 0.58f, 0.56f), // 2 次：青
            new Color(0.78f, 0.42f, 0.55f), // 3 次：玫
            new Color(0.45f, 0.40f, 0.78f), // 4 次：紫
            new Color(0.90f, 0.60f, 0.20f), // 5 次：橙
            new Color(0.30f, 0.55f, 0.80f), // 6 次：蓝
        };
        private static Color CountColor(int n) => CountColors[Mathf.Clamp(n - 1, 0, CountColors.Length - 1)];
        private static Color Lighten(Color c) => Color.Lerp(c, Color.white, 0.62f);

        private readonly System.Random _rng = new System.Random();

        private SlideStartArgs _args;
        private int _level = 1;
        private int _cols = 9;
        private int _rows = 9;
        private bool _overlap;
        private bool _overlapHint = true;
        private bool _replay;
        private bool _straight;     // straight-line snap selection (L1/L2)
        private GridWordSearch _game;

        private TextMeshProUGUI _timerText;
        private TextMeshProUGUI _statusText;
        private RectTransform _bodyRoot;
        private RectTransform _gridPanel;
        private GridSelector _selector;
        private float _cellSize;

        private readonly List<Image> _cellBg = new List<Image>();
        private readonly List<TextMeshProUGUI> _cellTxt = new List<TextMeshProUGUI>();
        private readonly List<int> _path = new List<int>();
        private readonly HashSet<int> _foundCells = new HashSet<int>();
        private readonly List<int> _painted = new List<int>();      // 当前临时高亮的格子（用于复原）
        private readonly List<int> _paintedFound = new List<int>(); // 临时划过的已找到格子（复原回找到色）
        private readonly Dictionary<int, Color> _foundColor = new Dictionary<int, Color>(); // 找到格子的静息色
        private readonly Dictionary<int, int> _crossCount = new Dictionary<int, int>(); // 每格被几句诗穿过（总交叉数）
        private readonly Dictionary<int, int> _foundCount = new Dictionary<int, int>(); // 每格已被几句已找到的诗穿过
        private readonly HashSet<int> _revealedLines = new HashSet<int>(); // 已用「显示答案」揭晓的句序
        private int _startCell = -1;

        private float _endTime;
        private float _totalSeconds;
        private bool _running;
        private bool _practice;
        private GameObject _resultOverlay;
        private GameObject _endButton;      // 结束游戏（进行中可提前结算）
        private GameObject _revealRow;      // 结算/回看后留在棋盘上的 提示/显示答案/显示全部 行

        private float _backArmedUntil = -1f;   // 第一次按返回后给的二次确认窗口
        private TextMeshProUGUI _backHint;

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
            _overlapHint = _args.OverlapHint;
            BuildChrome();
            if (_replay) LoadReplay(_args.Replay);
            else NewGame();
        }

        private void BuildChrome()
        {
            _bodyRoot = Design.Chrome(gameObject, OnBackPressed, () => Nav.Push<SettingsScreen>(),
                _replay ? "找诗回看" : "划线寻踪");

            _timerText = UiKit.Text("Timer", _bodyRoot, "", 42, TextAlignmentOptions.Center, Design.Ink);
            UiKit.AnchorTop(_timerText.gameObject, 60, 20, 30);

            _statusText = UiKit.Text("Status", _bodyRoot, "", 26, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
            UiKit.AnchorTop(_statusText.gameObject, 46, 88, 30);

            _backHint = UiKit.Text("BackHint", _bodyRoot, "再按一次返回退出", 26, TextAlignmentOptions.Center, Design.Primary);
            UiKit.AnchorTop(_backHint.gameObject, 40, 138, 30);
            _backHint.gameObject.SetActive(false);

            if (!_replay)
            {
                var footer = UiKit.Text("Footer", _bodyRoot, "滑动方块 · 寻找诗篇", 26, TextAlignmentOptions.Center,
                    Design.Alpha(Design.OnSurfaceVariant, 0.6f));
                footer.characterSpacing = 4f;
                var frt = UiKit.Rect(footer.gameObject);
                frt.anchorMin = new Vector2(0.5f, 0); frt.anchorMax = new Vector2(0.5f, 0); frt.pivot = new Vector2(0.5f, 0);
                frt.sizeDelta = new Vector2(700, 40); frt.anchoredPosition = new Vector2(0, 36);

                // 结束游戏：进行中可提前结算（不像返回那样丢弃本局）。
                var end = new GameObject("EndGame", typeof(RectTransform), typeof(Image), typeof(Button));
                end.transform.SetParent(_bodyRoot, false);
                var ert = UiKit.Rect(end);
                ert.anchorMin = new Vector2(0.5f, 0); ert.anchorMax = new Vector2(0.5f, 0); ert.pivot = new Vector2(0.5f, 0);
                ert.anchoredPosition = new Vector2(0, 92); ert.sizeDelta = new Vector2(300, 84);
                end.GetComponent<Image>().color = Design.SecondaryFixed;
                var el = UiKit.Text("L", end.transform, "结束游戏", 30, TextAlignmentOptions.Center, Design.Primary);
                UiKit.StretchFull(el.gameObject, 6);
                end.GetComponent<Button>().onClick.AddListener(EndGame);
                _endButton = end;
            }

            _gridPanel = MakeGrid();
        }

        private RectTransform MakeGrid()
        {
            _cellSize = Mathf.Min((WidthBudget - (_cols - 1) * Spacing) / _cols,
                                  (HeightBudget - (_rows - 1) * Spacing) / _rows);
            float w = _cols * _cellSize + (_cols - 1) * Spacing;
            float h = _rows * _cellSize + (_rows - 1) * Spacing;
            var gridPos = new Vector2(0, -10);

            // White "rice paper" mat behind the grid, with lattice corners.
            var frame = UiKit.Panel("GridFrame", _bodyRoot, Design.CardWhite);
            var frt = UiKit.Rect(frame);
            frt.anchorMin = new Vector2(0.5f, 0.5f); frt.anchorMax = new Vector2(0.5f, 0.5f); frt.pivot = new Vector2(0.5f, 0.5f);
            frt.anchoredPosition = gridPos; frt.sizeDelta = new Vector2(w + 28, h + 28);
            Design.Corners(frame, Design.Alpha(Design.Primary, 0.35f), arm: 34, thick: 2, inset: 6);

            var panel = new GameObject("Grid", typeof(RectTransform), typeof(Image), typeof(GridLayoutGroup), typeof(GridSelector));
            panel.transform.SetParent(_bodyRoot, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f); rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = gridPos;
            rt.sizeDelta = new Vector2(w, h);
            panel.GetComponent<Image>().color = Design.Alpha(Design.Outline, 0.18f); // shows through gaps as gridlines

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
            if (_revealRow != null) { Destroy(_revealRow); _revealRow = null; }
            if (_endButton != null) _endButton.SetActive(true);
            _game = new GridWordSearch(_cols, _rows, _level, _overlap, new SystemRandomSource(_rng.Next()));
            _straight = !_game.Snake;

            var poems = FilteredPoems();
            int maxLine = Mathf.Max(_cols, _rows);
            // Target line count comes from config (3~15), capped so a small grid can still hold them
            // (~7 cells budgeted per 诗句); the placement loop below stops early if even fewer fit.
            int capacity = Mathf.Max(3, (_cols * _rows) / 7);
            int target = Mathf.Clamp(_args.LineCount, 3, Mathf.Min(15, capacity));
            var used = new HashSet<string>();
            var placedChars = new HashSet<string>();   // 已放置诗句用到的字，用于刻意制造重叠字
            int placed = 0, guard = 0;
            while (placed < target && guard++ < 1000 && poems.Count > 0)
            {
                var p = poems[_rng.Next(poems.Count)];
                if (p.Lines.Count == 0) continue;
                PoemLine l;
                if (_args.FamousOnly)
                {
                    var fam = p.Lines.FindAll(x => x.Famous);
                    if (fam.Count == 0) continue;
                    l = fam[_rng.Next(fam.Count)];
                }
                else l = p.Lines[_rng.Next(p.Lines.Count)];
                if (l.CharCount < 5 || l.CharCount > maxLine) continue; // 只放五字及以上的诗句
                if (used.Contains(l.Text)) continue;
                // 开启重叠字时，从第二句起约 80% 的句子要求与已放置诗句共享字，制造真正的交叉；
                // 不满足则回退重抽新随机诗句。400 次后放宽，避免共享字稀少时凑不够目标句数。
                if (_overlap && placed > 0 && guard < 400 && _rng.Next(100) < 80 && !SharesPlacedChar(l, placedChars)) continue;
                used.Add(l.Text);
                var chars = SplitChars(l.Text);
                if (_game.TryPlace(l.Text, chars, p.Title, p.Id))
                {
                    placed++;
                    foreach (var ch in chars) placedChars.Add(ch);
                }
            }

            // Filler pool = corpus chars EXCLUDING any character used by a placed target line, so the
            // noise can't spell a target fragment and is easier to tell apart from the real 诗句 (降低难度).
            var targetChars = new HashSet<string>();
            foreach (var t in _game.Targets)
                if (t.Chars != null) foreach (var ch in t.Chars) targetChars.Add(ch);
            var pool = new List<string>();
            foreach (var p in Services.Content.Poems)
                foreach (var l in p.Lines)
                    foreach (var ch in SplitChars(l.Text))
                        if (!targetChars.Contains(ch)) pool.Add(ch);
            if (pool.Count == 0) // extreme fallback: targets consumed the whole corpus
                foreach (var p in Services.Content.Poems)
                    foreach (var l in p.Lines)
                        foreach (var ch in SplitChars(l.Text)) pool.Add(ch);
            _game.FillEmpty(pool);

            RenderGrid();
            // Time grows with the number of 诗句 actually on the board (越多越长), plus a difficulty
            // allowance for harder direction levels and a small grid-size term.
            _totalSeconds = 45f + _level * 12f + placed * 22f + (_cols + _rows);
            _endTime = Time.unscaledTime + _totalSeconds;
            _running = true;
            _practice = false;
            UpdateStatus();
        }

        private List<Poem> FilteredPoems()
        {
            var diff = _args.Difficulties ?? new List<int>();
            var dyn = _args.Dynasties ?? new List<string>();
            var types = _args.Types ?? new List<string>();
            var result = new List<Poem>();
            foreach (var p in Services.Content.Poems)
            {
                if (dyn.Count > 0 && !dyn.Contains(p.Dynasty)) continue;
                if (types.Count > 0 && !types.Contains(p.Type)) continue;
                if (diff.Count > 0 && !diff.Contains(p.Difficulty)) continue;
                if (_args.FamousOnly && !p.Lines.Exists(x => x.Famous)) continue;
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
            _path.Clear(); _painted.Clear(); _paintedFound.Clear(); _foundCells.Clear();
            _foundColor.Clear(); _foundCount.Clear(); _revealedLines.Clear(); _startCell = -1;

            // 每格的交叉数 = 有几句诗穿过它（≥2 即重叠字）。
            _crossCount.Clear();
            foreach (var tg in _game.Targets)
                foreach (var idx in tg.Cells)
                    _crossCount[idx] = (_crossCount.TryGetValue(idx, out var c) ? c : 0) + 1;

            int fontSize = Mathf.RoundToInt(_cellSize * 0.5f);
            for (int i = 0; i < _game.Cells.Length; i++)
            {
                var cell = new GameObject("C" + i, typeof(RectTransform), typeof(Image));
                cell.transform.SetParent(_gridPanel, false);
                var img = cell.GetComponent<Image>();
                img.color = Design.CardWhite;
                var t = UiKit.Text("t", cell.transform, _game.Cells[i] ?? "", fontSize, TextAlignmentOptions.Center, Design.Ink);
                UiKit.StretchFull(t.gameObject, 2);
                _cellBg.Add(img);
                _cellTxt.Add(t);
                _selector.Cells.Add(cell.GetComponent<RectTransform>());
            }
            foreach (var tg in _game.Targets)
                if (tg.Found) PaintFoundLine(tg);
        }

        // ----- selection -----

        private void OnCellDown(int cell)
        {
            if ((!_running && !_practice) || cell < 0) return;
            _startCell = cell;
            ClearPath();
            _path.Add(cell);
            ShowPath();
        }

        private void OnCellMove(int cell)
        {
            if ((!_running && !_practice) || cell < 0 || _startCell < 0) return;
            if (_straight)
            {
                _path.Clear();
                _path.AddRange(_game.StraightPath(_startCell, cell));
                ShowPath();
                return;
            }
            // snake: accumulate
            int last = _path.Count > 0 ? _path[_path.Count - 1] : _startCell;
            if (cell == last) return;
            if (_path.Count >= 2 && cell == _path[_path.Count - 2])
            {
                _path.RemoveAt(_path.Count - 1);   // backtrack
                ShowPath();
                return;
            }
            if (_path.Contains(cell)) return;
            if (!_game.Adjacent(last, cell)) return;
            _path.Add(cell);
            ShowPath();
        }

        private void OnCellUp()
        {
            _startCell = -1;
            if (!_running && !_practice) { ClearPath(); return; }
            var found = _game.TryMatch(_path);
            if (found != null)
            {
                RestoreTransient();                 // drop the trace highlight; cells become "found"
                PaintFoundLine(found);
                _path.Clear();
                if (AudioManager.Instance != null) AudioManager.Instance.PlayCorrect();
                UpdateStatus();
                if (_running && _game.AllFound()) EndGame();
                return;
            }
            if (AudioManager.Instance != null && _path.Count >= 2) AudioManager.Instance.PlayWrong();
            ClearPath();
        }

        // Recolor the current trace. 划过已找到的字 → 浅绿提示；其余普通字统一浅蓝。
        // 重叠提示开时，划过尚未找到的重叠字额外预览为「最终重叠色的浅色版」。
        private void ShowPath()
        {
            RestoreTransient();

            for (int i = 0; i < _path.Count; i++)
            {
                int idx = _path[i];
                if (idx < 0 || idx >= _cellBg.Count) continue;
                if (_foundCells.Contains(idx))
                {
                    // 划过已找到的字：浅绿提示（之后复原回它的找到色）。
                    _cellBg[idx].color = OverFoundColor;
                    _cellTxt[idx].color = Design.Ink;
                    _paintedFound.Add(idx);
                    continue;
                }
                int total = _crossCount.TryGetValue(idx, out var tc) ? tc : 1;
                Color bg = (_overlapHint && total >= 2) ? Lighten(CountColor(total)) : SlidBodyColor;
                _cellBg[idx].color = bg;
                _cellTxt[idx].color = Design.Ink;
                _painted.Add(idx);
            }
        }

        // Restore any cells temporarily recolored by the current trace back to their resting state.
        private void RestoreTransient()
        {
            foreach (var idx in _painted)
                if (!_foundCells.Contains(idx)) { _cellBg[idx].color = Design.CardWhite; _cellTxt[idx].color = Design.Ink; }
            _painted.Clear();
            foreach (var idx in _paintedFound)
                if (_foundColor.TryGetValue(idx, out var c)) { _cellBg[idx].color = c; _cellTxt[idx].color = Color.white; }
            _paintedFound.Clear();
        }

        // Lock in a found 诗句, coloring each cell by crossing count:
        //  · 重叠提示开 → 立即用「该格总交叉数」的颜色（找到一句即显示最终重叠色）；
        //  · 重叠提示关 → 用「该格已找到的交叉数」着色（1 次绿，2 次起逐级换色，随后续诗句被找到而递进）。
        // 普通字（仅 1 句穿过）两种模式下都是绿色。
        private void PaintFoundLine(GridWordSearch.Target t)
        {
            foreach (var idx in t.Cells)
            {
                if (idx < 0 || idx >= _cellBg.Count) continue;
                int fc = (_foundCount.TryGetValue(idx, out var c) ? c : 0) + 1;
                _foundCount[idx] = fc;
                int total = _crossCount.TryGetValue(idx, out var tc) ? tc : 1;
                Color col = CountColor(_overlapHint ? total : fc);
                _foundCells.Add(idx);
                _foundColor[idx] = col;
                _cellBg[idx].color = col;
                _cellTxt[idx].color = Color.white;
            }
        }

        private void ClearPath()
        {
            RestoreTransient();
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

        // While a game is running, the first tap arms a 2s window and warns; a second tap within it
        // actually leaves. Once finished or in replay there's nothing to lose, so a single tap exits.
        private void OnBackPressed()
        {
            if (!_running) { Nav.Pop(); return; }
            if (Time.unscaledTime <= _backArmedUntil) { Nav.Pop(); return; }
            _backArmedUntil = Time.unscaledTime + 2f;
            if (_backHint != null) _backHint.gameObject.SetActive(true);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayWrong();
        }

        private void Update()
        {
            if (_backHint != null && _backHint.gameObject.activeSelf && Time.unscaledTime > _backArmedUntil)
                _backHint.gameObject.SetActive(false);
            if (!_running) return;
            float remain = _endTime - Time.unscaledTime;
            if (remain <= 0f) { _timerText.text = "时间 0:00"; EndGame(); return; }
            _timerText.text = $"时间 {(int)remain / 60}:{(int)remain % 60:00}";
        }

        /// <summary>
        /// 截图工具用（<c>ScreenshotRunner</c>）：把约半数诗句标记为已找到并直接跳到结算面板，
        /// 走与正常通关相同的 <see cref="EndGame"/> 路径，用于复现「滑动找诗结束」界面。正常玩法不调用。
        /// </summary>
        public void CaptureForceResult()
        {
            if (_game == null || !_running) return;
            int half = Mathf.CeilToInt(_game.Targets.Count / 2f), i = 0;
            foreach (var t in _game.Targets) { if (i++ >= half) break; t.Found = true; }
            RenderGrid();
            EndGame();
        }

        private void EndGame()
        {
            if (!_running) return;
            _running = false;
            if (_endButton != null) _endButton.SetActive(false);
            // 留在棋盘上的 提示/显示答案/显示全部（结算页关闭后即可反复使用）。
            if (!AllFoundOrComplete()) AddRevealButton();
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

        private bool AllFoundOrComplete()
        {
            foreach (var t in _game.Targets) if (!t.Found) return false;
            return true;
        }

        // 提示：随机挑一句未划出的诗句，仅让其中一个尚未找到的字闪烁 2 次（不揭晓整句）。
        private void Hint()
        {
            if (isActiveAndEnabled) StartCoroutine(HintFlash());
        }

        private System.Collections.IEnumerator HintFlash()
        {
            var unfound = new List<GridWordSearch.Target>();
            foreach (var t in _game.Targets) if (!t.Found) unfound.Add(t);
            if (unfound.Count == 0) yield break;
            var tg = unfound[_rng.Next(unfound.Count)];

            // 仅提示一个字：挑该句第一个尚未找到的格子。
            int cell = -1;
            foreach (var idx in tg.Cells)
                if (!_foundCells.Contains(idx)) { cell = idx; break; }
            if (cell < 0) yield break;
            Color orig = _cellBg[cell].color;
            for (int blink = 0; blink < 2; blink++)
            {
                _cellBg[cell].color = HintFlashColor;
                yield return new WaitForSecondsRealtime(0.32f);
                _cellBg[cell].color = orig;
                yield return new WaitForSecondsRealtime(0.20f);
            }
        }

        // 显示答案：每点一次揭晓一句随机的、尚未揭晓的未完成诗句。
        private void RevealOneRandom()
        {
            var candidates = new List<int>();
            for (int i = 0; i < _game.Targets.Count; i++)
                if (!_game.Targets[i].Found && !_revealedLines.Contains(i)) candidates.Add(i);
            if (candidates.Count == 0) return;
            int pick = candidates[_rng.Next(candidates.Count)];
            _revealedLines.Add(pick);
            foreach (var idx in _game.Targets[pick].Cells)
                if (!_foundCells.Contains(idx)) _cellBg[idx].color = RevealColor;
        }

        // 回看 / 结算后留在棋盘上的辅助按钮：未完成时给「提示 / 显示答案」，并保留「显示全部」。
        private void AddRevealButton()
        {
            if (_revealRow != null) { Destroy(_revealRow); _revealRow = null; }
            var row = UiKit.Panel("RevealRow", _bodyRoot);
            _revealRow = row;
            var rt = UiKit.Rect(row);
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0); rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 96); rt.sizeDelta = new Vector2(720, 96);
            var hg = UiKit.HorizontalGroup(row, spacing: 12); hg.childForceExpandHeight = true;

            bool incomplete = !AllFoundOrComplete();
            if (incomplete)
            {
                var hint = UiKit.Button("Hint", row.transform, "提示", out var hL, Design.SurfaceHigh, 30);
                hL.color = Design.Ink; hint.onClick.AddListener(Hint);
                var one = UiKit.Button("RevealOne", row.transform, "显示答案", out var oL, Design.SurfaceHigh, 30);
                oL.color = Design.Ink; one.onClick.AddListener(RevealOneRandom);
            }
            var all = UiKit.Button("RevealAll", row.transform, "显示全部", out var aL, Design.SurfaceHigh, 30);
            aL.color = Design.Ink; all.onClick.AddListener(RevealUnfound);
        }

        private void ShowResult()
        {
            // Full-screen parchment result page (designs/stitch/滑动找诗结束), overlaid on the grid so
            // 继续查看 / 提示 / 显示答案 / 显示全部 can dismiss it and return to the board.
            _resultOverlay = UiKit.Panel("Result", transform, Design.Paper);
            UiKit.StretchFull(_resultOverlay);
            float safeTop = UiKit.SafeTopInset(gameObject);

            int found = 0;
            foreach (var t in _game.Targets) if (t.Found) found++;
            int total = _game.Targets.Count;
            int pct = total > 0 ? found * 100 / total : 0;
            int dur = Mathf.RoundToInt(_totalSeconds - Mathf.Max(0f, _endTime - Time.unscaledTime));

            // Top bar: title + close (× = 继续查看).
            var title = UiKit.Text("Title", _resultOverlay.transform, "挑战结果", 44, TextAlignmentOptions.Center, Design.Primary);
            title.characterSpacing = 8f;
            UiKit.AnchorTop(title.gameObject, height: 76, topOffset: safeTop + 28, sideMargin: 200);
            var hairline = UiKit.Panel("HeaderBorder", _resultOverlay.transform, Design.Alpha(Design.Outline, 0.2f));
            var hlr = UiKit.Rect(hairline);
            hlr.anchorMin = new Vector2(0, 1); hlr.anchorMax = new Vector2(1, 1); hlr.pivot = new Vector2(0.5f, 1);
            hlr.sizeDelta = new Vector2(0, 2); hlr.anchoredPosition = new Vector2(0, -(safeTop + 120));
            Design.IconButton(_resultOverlay.transform, "Close", "×", 56, Design.Secondary, new Vector2(1, 1),
                new Vector2(-24, -(safeTop + 18)), () => { CloseResult(); _practice = true; UpdateStatus(); });

            // Body below the header.
            var bodyPanel = UiKit.Panel("Body", _resultOverlay.transform);
            var bprt = UiKit.Rect(bodyPanel);
            bprt.anchorMin = Vector2.zero; bprt.anchorMax = Vector2.one;
            bprt.offsetMin = Vector2.zero; bprt.offsetMax = new Vector2(0, -(safeTop + 120));
            UiKit.VerticalGroup(bodyPanel, spacing: 8, padX: 40, padY: 24, align: TextAnchor.UpperCenter);

            UiKit.MinHeight(UiKit.Text("H", bodyPanel.transform, $"找到 {found}/{total} 句", 64, TextAlignmentOptions.Center, Design.PrimaryContainer).gameObject, 100);
            UiKit.MinHeight(UiKit.Text("Grade", bodyPanel.transform, Grade(pct), 32, TextAlignmentOptions.Center, Design.Primary).gameObject, 50);
            UiKit.MinHeight(UiKit.Text("Stat", bodyPanel.transform, $"用时 {UiFormat.Duration(dur)}　·　正确率 {pct}%", 28,
                TextAlignmentOptions.Center, Design.OnSurfaceVariant).gameObject, 56);
            UiKit.MinHeight(Design.HLine(bodyPanel.transform, 0.16f), 2);

            var list = UiKit.ScrollList("L", bodyPanel.transform, out _);
            string greenHex = ColorUtility.ToHtmlStringRGB(UiKit.Good);
            string redHex = ColorUtility.ToHtmlStringRGB(UiKit.Bad);
            string mutedHex = ColorUtility.ToHtmlStringRGB(Design.OnSurfaceVariant);
            foreach (var t in _game.Targets)
            {
                string mark = t.Found ? $"<color=#{greenHex}>✓</color>" : $"<color=#{redHex}>×</color>";
                var row = UiKit.Text("R", list, $"{mark}　{t.Text}　<size=78%><color=#{mutedHex}>· {PoemFormat.DisplayTitle(t.Title)}</color></size>",
                    30, TextAlignmentOptions.Left, Design.Ink);
                UiKit.Pref(row.gameObject, minH: 76);
            }

            // Footer: paired action rows. When the board still has unfound 诗句, prepend a 提示/显示答案
            // row (both close the result page first so the action is visible on the board behind it).
            bool incomplete = found < total;
            var footer = UiKit.Panel("Footer", bodyPanel.transform);
            UiKit.Pref(footer, minH: incomplete ? 344 : 236).flexibleHeight = 0f;
            var fvg = UiKit.VerticalGroup(footer, spacing: 14, padX: 0, padY: 0, align: TextAnchor.UpperCenter);
            fvg.childForceExpandHeight = false;

            if (incomplete)
            {
                var row0 = UiKit.Panel("R0", footer.transform);
                UiKit.Pref(row0, minH: 108);
                var hg0 = UiKit.HorizontalGroup(row0, spacing: 14); hg0.childForceExpandHeight = true;
                var hint = UiKit.Button("Hint", row0.transform, "提示", out var hintL, Design.SurfaceHigh, 30);
                hintL.color = Design.Ink;
                hint.onClick.AddListener(() => { CloseResult(); _practice = true; UpdateStatus(); Hint(); });
                var revOne = UiKit.Button("RevealOne", row0.transform, "显示答案", out var roL, Design.SurfaceHigh, 30);
                roL.color = Design.Ink;
                revOne.onClick.AddListener(() => { CloseResult(); _practice = true; RevealOneRandom(); UpdateStatus(); });
            }

            var row1 = UiKit.Panel("R1", footer.transform);
            UiKit.Pref(row1, minH: 108);
            var hg1 = UiKit.HorizontalGroup(row1, spacing: 14); hg1.childForceExpandHeight = true;
            var keep = UiKit.Button("Keep", row1.transform, "继续查看", out var keepL, Design.SurfaceHigh, 30);
            keepL.color = Design.Ink;
            keep.onClick.AddListener(() => { CloseResult(); _practice = true; UpdateStatus(); });
            var reveal = UiKit.Button("Reveal", row1.transform, "显示全部", out var revL, Design.SurfaceHigh, 30);
            revL.color = Design.Ink;
            reveal.onClick.AddListener(() => { CloseResult(); _practice = true; RevealUnfound(); UpdateStatus(); });

            var row2 = UiKit.Panel("R2", footer.transform);
            UiKit.Pref(row2, minH: 108);
            var hg2 = UiKit.HorizontalGroup(row2, spacing: 14); hg2.childForceExpandHeight = true;
            var again = Design.PrimaryButton("Again", row2.transform, "再来一局", out _, 32);
            again.onClick.AddListener(NewGame);
            var home = UiKit.Button("Home", row2.transform, "返回主页", out var homeL, Design.SecondaryFixed, 32);
            homeL.color = Design.Primary;
            home.onClick.AddListener(() => Nav.Pop());
        }

        private void CloseResult()
        {
            if (_resultOverlay != null) { Destroy(_resultOverlay); _resultOverlay = null; }
        }

        private static string Grade(int pct) =>
            pct >= 100 ? "妙笔生花" : pct >= 80 ? "出类拔萃" : pct >= 50 ? "渐入佳境" : "勤能补拙";

        private static bool SharesPlacedChar(PoemLine l, HashSet<string> placedChars)
        {
            foreach (var ch in SplitChars(l.Text)) if (placedChars.Contains(ch)) return true;
            return false;
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
