using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PoemPoetry.Data;
using PoemPoetry.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// 逐词填空 (残句调控): a poem line is shown with one or more 名词/动词 keywords blanked as empty
    /// slots; the player taps single-character tiles to fill the slots in order. Distractor tiles are
    /// same-类型 + 平仄相当 chars (baked offline). Tapping a filled slot returns its char to the pool.
    /// Mirrors <see cref="QuizScreen"/>'s session/timer/streak/record/错题本 wiring (mode "wordcloze").
    /// </summary>
    public sealed class WordClozeScreen : UIScreen
    {
        private const float AdvanceDelay = 1.2f;
        private const string GreenHex = "#2E8B47";

        private WordClozeStartArgs _args;
        private readonly List<WordClozeQuestion> _questions = new List<WordClozeQuestion>();
        private int _index;
        private int _streak;
        private int _bestStreak;
        private bool _locked;
        private bool _running;
        private float _qStartTime;
        private float _qEndTime;
        private float _timeLimit;
        private float _sessionStart;
        private readonly List<QuestionResult> _results = new List<QuestionResult>();

        private TextMeshProUGUI _progress;
        private TextMeshProUGUI _timerText;
        private RectTransform _timerFill;
        private TextMeshProUGUI _meta;
        private RectTransform _lineRow;
        private RectTransform _tilePanel;

        private WordClozeQuestion _q;
        private readonly List<string> _answerSeq = new List<string>();
        private readonly List<SlotCell> _slots = new List<SlotCell>();
        private readonly List<TileCell> _tiles = new List<TileCell>();

        private sealed class SlotCell { public Image Bg; public TextMeshProUGUI Txt; public int FilledTile = -1; }
        private sealed class TileCell { public Button Btn; public Image Bg; public TextMeshProUGUI Txt; public string Ch; public bool Used; }

        protected override void OnShow(object args)
        {
            _args = args as WordClozeStartArgs ?? new WordClozeStartArgs();
            BuildSession();
            BuildChrome();
            if (_questions.Count == 0)
            {
                _meta.text = "题库暂无符合条件的题目。\n请返回切换筛选，或扩充词库。";
                return;
            }
            _sessionStart = Time.unscaledTime;
            ShowQuestion();
        }

        private void BuildSession()
        {
            var settings = new ChallengeSettings
            {
                QuestionCount = _args.QuestionCount,
                Difficulties = _args.Difficulties ?? new List<int>(),
                Dynasties = _args.Dynasties ?? new List<string>(),
                Types = _args.Types ?? new List<string>(),
            };
            List<WordClozeQuestion> pool = (_args.QuestionIds != null && _args.QuestionIds.Count > 0)
                ? Services.Content.GetWordClozeByIds(_args.QuestionIds)
                : Services.Content.GetWordClozePool(settings);

            // Shuffle, then take up to QuestionCount avoiding repeating the same poem when possible.
            var rng = new System.Random();
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

            int want = _args.QuestionIds != null ? pool.Count : Mathf.Max(1, _args.QuestionCount);
            var seenPoems = new HashSet<string>();
            foreach (var q in pool)
            {
                if (_questions.Count >= want) break;
                if (_args.QuestionIds == null && !seenPoems.Add(q.PoemId)) continue;
                _questions.Add(q);
            }
            // Top up ignoring poem-dedup if filtering left us short.
            if (_questions.Count < want)
                foreach (var q in pool)
                {
                    if (_questions.Count >= want) break;
                    if (!_questions.Contains(q)) _questions.Add(q);
                }
        }

        private void BuildChrome()
        {
            var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            bg.color = UiKit.Paper;

            var quit = new GameObject("Quit", typeof(RectTransform), typeof(Image), typeof(Button));
            quit.transform.SetParent(transform, false);
            var qrt = UiKit.Rect(quit);
            qrt.anchorMin = new Vector2(0, 1); qrt.anchorMax = new Vector2(0, 1); qrt.pivot = new Vector2(0, 1);
            qrt.anchoredPosition = new Vector2(24, -24); qrt.sizeDelta = new Vector2(150, 72);
            quit.GetComponent<Image>().color = UiKit.CardAlt;
            var ql = UiKit.Text("L", quit.transform, "退出", 32, TextAlignmentOptions.Center, UiKit.Ink);
            UiKit.StretchFull(ql.gameObject, 6);
            quit.GetComponent<Button>().onClick.AddListener(Quit);

            _progress = UiKit.Text("Progress", transform, "", 34, TextAlignmentOptions.Center, UiKit.Muted);
            UiKit.AnchorTop(_progress.gameObject, height: 70, topOffset: 30, sideMargin: 200);

            _timerText = UiKit.Text("Timer", transform, "", 30, TextAlignmentOptions.Center, UiKit.Accent);
            UiKit.AnchorTop(_timerText.gameObject, height: 44, topOffset: 104, sideMargin: 40);

            var barBg = UiKit.Panel("TimerBar", transform, UiKit.CardAlt);
            var brt = UiKit.Rect(barBg);
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(-120, 16); brt.anchoredPosition = new Vector2(0, -156);
            var fill = UiKit.Panel("Fill", barBg.transform, UiKit.Accent);
            _timerFill = UiKit.Rect(fill);
            _timerFill.anchorMin = new Vector2(0, 0); _timerFill.anchorMax = new Vector2(1, 1);
            _timerFill.offsetMin = Vector2.zero; _timerFill.offsetMax = Vector2.zero;

            _meta = UiKit.Text("Meta", transform, "", 32, TextAlignmentOptions.Center, UiKit.Muted, wrap: true);
            UiKit.AnchorTop(_meta.gameObject, height: 110, topOffset: 210, sideMargin: 40);

            // Poem line (static chars + blank slots), centered horizontally.
            var line = UiKit.Panel("Line", transform);
            _lineRow = UiKit.Rect(line);
            _lineRow.anchorMin = new Vector2(0, 1); _lineRow.anchorMax = new Vector2(1, 1); _lineRow.pivot = new Vector2(0.5f, 1);
            _lineRow.sizeDelta = new Vector2(-60, 180); _lineRow.anchoredPosition = new Vector2(0, -360);
            var hg = UiKit.HorizontalGroup(line, spacing: 10, align: TextAnchor.MiddleCenter);
            hg.childForceExpandWidth = false; hg.childControlWidth = true; hg.childControlHeight = true;

            // Tile pool grid, centered in the lower half.
            var tiles = new GameObject("Tiles", typeof(RectTransform), typeof(GridLayoutGroup));
            tiles.transform.SetParent(transform, false);
            _tilePanel = tiles.GetComponent<RectTransform>();
            _tilePanel.anchorMin = new Vector2(0.5f, 0.5f); _tilePanel.anchorMax = new Vector2(0.5f, 0.5f);
            _tilePanel.pivot = new Vector2(0.5f, 0.5f); _tilePanel.anchoredPosition = new Vector2(0, -300);
        }

        private WordClozeQuestion Current => _questions[_index];

        private void ShowQuestion()
        {
            _q = Current;
            _locked = false;
            _slots.Clear();
            _tiles.Clear();
            _answerSeq.Clear();
            foreach (var a in _q.AnswerSequence()) _answerSeq.Add(a);

            var poem = Services.Content.GetPoem(_q.PoemId);
            bool isCi = poem != null && poem.Type == "词";
            _progress.text = $"第 {_index + 1}/{_questions.Count} 题   连胜 {_streak}";
            _meta.text = poem == null ? "" : (isCi
                ? (string.IsNullOrEmpty(poem.Cipai) ? poem.Title : poem.Cipai) + " · " + poem.Author
                : $"《{poem.Title}》 {poem.Dynasty}·{poem.Author}");

            BuildLineRow(poem);
            BuildTiles();

            _qStartTime = Time.unscaledTime;
            _timeLimit = 20f + 6f * _answerSeq.Count + 1.5f * _q.TilePool.Count;
            _qEndTime = _qStartTime + _timeLimit;
            _running = true;
        }

        // Render the line as a row of fixed-size cells: static chars + blank slots (in answer order).
        private void BuildLineRow(Poem poem)
        {
            UiKit.ClearChildren(_lineRow);
            var text = poem != null && _q.BlankLineIndex < poem.Lines.Count ? poem.Lines[_q.BlankLineIndex].Text : "";
            var elems = Chars(text);

            // Which original positions are blanked, mapped to their slot order.
            var blanked = new HashSet<int>();
            foreach (var b in _q.Blanks)
                for (int k = 0; k < b.Count; k++) blanked.Add(b.Start + k);

            float cell = Mathf.Min(120f, 980f / Mathf.Max(1, elems.Count));
            int fontSize = Mathf.RoundToInt(cell * 0.52f);
            for (int i = 0; i < elems.Count; i++)
            {
                if (blanked.Contains(i))
                {
                    var go = new GameObject("Slot" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                    go.transform.SetParent(_lineRow, false);
                    var img = go.GetComponent<Image>();
                    img.color = UiKit.Card;
                    UiKit.Pref(go, minW: cell, minH: cell);
                    var txt = UiKit.Text("t", go.transform, "", fontSize, TextAlignmentOptions.Center, UiKit.Accent);
                    UiKit.StretchFull(txt.gameObject, 2);
                    var slot = new SlotCell { Bg = img, Txt = txt };
                    int slotIdx = _slots.Count;
                    go.GetComponent<Button>().onClick.AddListener(() => OnSlotClick(slotIdx));
                    _slots.Add(slot);
                }
                else
                {
                    var go = UiKit.Panel("Ch" + i, _lineRow, new Color(0, 0, 0, 0));
                    UiKit.Pref(go, minW: cell, minH: cell);
                    var txt = UiKit.Text("t", go.transform, elems[i], fontSize, TextAlignmentOptions.Center, UiKit.Ink);
                    UiKit.StretchFull(txt.gameObject, 2);
                }
            }
        }

        private void BuildTiles()
        {
            UiKit.ClearChildren(_tilePanel);
            int n = _q.TilePool.Count;
            // Always two rows: pool is even (padded offline), so it tiles cleanly into 2×N.
            const int rows = 2;
            int cols = Mathf.CeilToInt(n / (float)rows);
            float spacing = 12f;
            float cell = Mathf.Min(140f, (1000f - (cols - 1) * spacing) / cols);

            var glg = _tilePanel.GetComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(cell, cell);
            glg.spacing = new Vector2(spacing, spacing);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = cols;
            glg.childAlignment = TextAnchor.MiddleCenter;
            _tilePanel.sizeDelta = new Vector2(cols * cell + (cols - 1) * spacing, rows * cell + (rows - 1) * spacing);

            int fontSize = Mathf.RoundToInt(cell * 0.46f);
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var go = new GameObject("Tile" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_tilePanel, false);
                var img = go.GetComponent<Image>();
                img.color = UiKit.CardAlt;
                var txt = UiKit.Text("t", go.transform, _q.TilePool[i], fontSize, TextAlignmentOptions.Center, UiKit.Ink);
                UiKit.StretchFull(txt.gameObject, 4);
                var tile = new TileCell { Btn = go.GetComponent<Button>(), Bg = img, Txt = txt, Ch = _q.TilePool[i] };
                tile.Btn.onClick.AddListener(() => OnTileClick(idx));
                _tiles.Add(tile);
            }
        }

        private void OnTileClick(int tileIndex)
        {
            if (_locked || tileIndex < 0 || tileIndex >= _tiles.Count) return;
            var tile = _tiles[tileIndex];
            if (tile.Used) return;
            int slot = FirstEmptySlot();
            if (slot < 0) return;

            _slots[slot].Txt.text = tile.Ch;
            _slots[slot].FilledTile = tileIndex;
            tile.Used = true;
            tile.Bg.color = UiKit.Paper;
            tile.Txt.color = UiKit.Muted;
            tile.Btn.interactable = false;

            if (AudioManager.Instance != null) AudioManager.Instance.PlayClick();
            if (FirstEmptySlot() < 0) CheckAnswer();
        }

        private void OnSlotClick(int slotIndex)
        {
            if (_locked || slotIndex < 0 || slotIndex >= _slots.Count) return;
            var slot = _slots[slotIndex];
            if (slot.FilledTile < 0) return;
            var tile = _tiles[slot.FilledTile];
            tile.Used = false;
            tile.Bg.color = UiKit.CardAlt;
            tile.Txt.color = UiKit.Ink;
            tile.Btn.interactable = true;
            slot.Txt.text = "";
            slot.FilledTile = -1;
        }

        private int FirstEmptySlot()
        {
            for (int i = 0; i < _slots.Count; i++)
                if (_slots[i].FilledTile < 0) return i;
            return -1;
        }

        private void Update()
        {
            if (!_running) return;
            float remain = _qEndTime - Time.unscaledTime;
            if (remain < 0) remain = 0;
            _timerText.text = $"剩余 {Mathf.CeilToInt(remain)} 秒";
            if (_timerFill != null)
                _timerFill.anchorMax = new Vector2(_timeLimit > 0 ? Mathf.Clamp01(remain / _timeLimit) : 0f, 1f);
            if (remain <= 0f) CheckAnswer(true);
        }

        private void CheckAnswer(bool timedOut = false)
        {
            if (_locked) return;
            _locked = true;
            _running = false;

            bool correct = !timedOut;
            var chosen = new StringBuilder();
            for (int i = 0; i < _slots.Count; i++)
            {
                string c = _slots[i].FilledTile >= 0 ? _tiles[_slots[i].FilledTile].Ch : "";
                chosen.Append(c);
                if (i >= _answerSeq.Count || c != _answerSeq[i]) correct = false;
            }

            // Reveal the correct answer in the slots, green where matched, red where not.
            for (int i = 0; i < _slots.Count; i++)
            {
                string want = i < _answerSeq.Count ? _answerSeq[i] : "";
                bool ok = _slots[i].FilledTile >= 0 && _tiles[_slots[i].FilledTile].Ch == want;
                _slots[i].Txt.text = want;
                _slots[i].Bg.color = ok ? UiKit.Good : UiKit.Bad;
                _slots[i].Txt.color = Color.white;
            }

            int ms = Mathf.RoundToInt((Time.unscaledTime - _qStartTime) * 1000f);
            var poem = Services.Content.GetPoem(_q.PoemId);
            string lineText = poem != null && _q.BlankLineIndex < poem.Lines.Count ? poem.Lines[_q.BlankLineIndex].Text : "";
            _results.Add(new QuestionResult
            {
                QuestionId = _q.Id,
                PoemId = _q.PoemId,
                BlankLineIndex = _q.BlankLineIndex,
                BlankedText = lineText,
                CorrectText = lineText,
                ChosenText = Join(_answerSeq) == chosen.ToString() ? Join(_answerSeq) : chosen.ToString(),
                IsCorrect = correct,
                TimeMs = ms,
            });

            if (correct) { _streak++; if (_streak > _bestStreak) _bestStreak = _streak; }
            else _streak = 0;

            if (AudioManager.Instance != null) { if (correct) AudioManager.Instance.PlayCorrect(); else AudioManager.Instance.PlayWrong(); }
            UpdateLeitner(_q.Id, _q.PoemId, correct);
            StartCoroutine(AdvanceAfter(AdvanceDelay));
        }

        private async void UpdateLeitner(string questionId, string poemId, bool correct)
        {
            if (_args.Mode == "wrongbook")
                await Services.WrongBook.RegisterReviewResultAsync(questionId, correct);
            else if (!correct)
                await Services.WrongBook.RegisterWrongAsync(questionId, poemId);
        }

        private IEnumerator AdvanceAfter(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _index++;
            if (_index >= _questions.Count) Finish();
            else ShowQuestion();
        }

        private async void Finish()
        {
            int duration = Mathf.RoundToInt(Time.unscaledTime - _sessionStart);
            var settings = new ChallengeSettings
            {
                QuestionCount = _questions.Count,
                Difficulties = _args.Difficulties ?? new List<int>(),
                Dynasties = _args.Dynasties ?? new List<string>(),
                Types = _args.Types ?? new List<string>(),
            };
            var record = await Services.Records.SaveCompletedAsync(_results, settings, duration, "wordcloze");
            record.BestStreak = Mathf.Max(record.BestStreak, _bestStreak);
            Nav.Replace<ResultScreen>(new ResultArgs { Record = record });
        }

        private void Quit()
        {
            _running = false;
            _locked = true;
            Nav.Pop();
        }

        private static string Join(List<string> parts)
        {
            var sb = new StringBuilder();
            foreach (var p in parts) sb.Append(p);
            return sb.ToString();
        }

        private static List<string> Chars(string s)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(s)) return r;
            var si = new StringInfo(s);
            int n = si.LengthInTextElements;
            for (int i = 0; i < n; i++) r.Add(si.SubstringByTextElements(i, 1));
            return r;
        }
    }
}
