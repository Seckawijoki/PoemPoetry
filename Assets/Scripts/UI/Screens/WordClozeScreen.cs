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
        private RectTransform _linesPanel;
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
                : Services.Content.GetWordClozePool(settings, _args.BlankCounts);

            // Shuffle, then take up to QuestionCount avoiding repeating the same poem when possible.
            var rng = new System.Random();
            for (int i = pool.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); (pool[i], pool[j]) = (pool[j], pool[i]); }

            int want = _args.QuestionIds != null ? pool.Count : Mathf.Max(1, _args.QuestionCount);
            // One question per 诗词 in a game (避免同首重复). If distinct poems run short the session is
            // simply shorter rather than repeating a poem. Review (QuestionIds) keeps every due item.
            var seenPoems = new HashSet<string>();
            foreach (var q in pool)
            {
                if (_questions.Count >= want) break;
                if (_args.QuestionIds == null && !seenPoems.Add(q.PoemId)) continue;
                _questions.Add(q);
            }
        }

        private void BuildChrome()
        {
            var body = Design.Chrome(gameObject, Quit, () => Nav.Push<SettingsScreen>(), "墨意填空");

            var title = UiKit.Text("Title", body, "补全诗句", 50, TextAlignmentOptions.Center, Design.Primary);
            title.characterSpacing = 6f;
            UiKit.AnchorTop(title.gameObject, height: 70, topOffset: 14, sideMargin: 40);

            _progress = UiKit.Text("Progress", body, "", 26, TextAlignmentOptions.Center, Design.OnSurfaceVariant);
            UiKit.AnchorTop(_progress.gameObject, height: 38, topOffset: 92, sideMargin: 40);

            _timerText = UiKit.Text("Timer", body, "", 24, TextAlignmentOptions.Center, Design.Primary);
            UiKit.AnchorTop(_timerText.gameObject, height: 34, topOffset: 134, sideMargin: 40);

            var barBg = UiKit.Panel("TimerBar", body, Design.Alpha(Design.Outline, 0.18f));
            var brt = UiKit.Rect(barBg);
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(-96, 6); brt.anchoredPosition = new Vector2(0, -178);
            var fill = UiKit.Panel("Fill", barBg.transform, Design.PrimaryContainer);
            _timerFill = UiKit.Rect(fill);
            _timerFill.anchorMin = new Vector2(0, 0); _timerFill.anchorMax = new Vector2(1, 1);
            _timerFill.offsetMin = Vector2.zero; _timerFill.offsetMax = Vector2.zero;

            // Game canvas card: poem couplet (≥2 句). Holds ONLY the line rows so nothing overlaps.
            var card = UiKit.Panel("Canvas", body, Design.SurfaceLow);
            var crt = UiKit.Rect(card);
            crt.anchorMin = new Vector2(0, 1); crt.anchorMax = new Vector2(1, 1); crt.pivot = new Vector2(0.5f, 1);
            crt.sizeDelta = new Vector2(-48, 372); crt.anchoredPosition = new Vector2(0, -212);
            card.AddComponent<RectMask2D>(); // keep line rows from ever spilling onto the 词牌/提示 band
            Design.Corners(card, Design.Alpha(Design.PrimaryContainer, 0.4f), arm: 36, thick: 2, inset: 14);

            // Vertical stack of line rows (one horizontal row per shown 句), filling the card with padding.
            var lines = UiKit.Panel("Lines", card.transform);
            _linesPanel = UiKit.Rect(lines);
            _linesPanel.anchorMin = Vector2.zero; _linesPanel.anchorMax = Vector2.one; _linesPanel.pivot = new Vector2(0.5f, 0.5f);
            _linesPanel.offsetMin = new Vector2(24, 24); _linesPanel.offsetMax = new Vector2(-24, -24);
            var vg = UiKit.VerticalGroup(lines, spacing: 16, align: TextAnchor.MiddleCenter);
            vg.childForceExpandHeight = false; vg.childControlHeight = true; vg.childControlWidth = true;

            // 词牌/作者 caption, BELOW the card (separate band, no longer overlapping the lines).
            _meta = UiKit.Text("Meta", body, "", 28, TextAlignmentOptions.Center,
                Design.Alpha(Design.OnSurfaceVariant, 0.85f), wrap: true);
            UiKit.AnchorTop(_meta.gameObject, height: 52, topOffset: 600, sideMargin: 40);

            var pick = UiKit.Text("Pick", body, "请选择正确的字", 26, TextAlignmentOptions.Center, Design.Secondary);
            pick.characterSpacing = 4f;
            UiKit.AnchorTop(pick.gameObject, height: 40, topOffset: 668, sideMargin: 40);

            // Tile pool grid, clearly below the prompt.
            var tiles = new GameObject("Tiles", typeof(RectTransform), typeof(GridLayoutGroup));
            tiles.transform.SetParent(body, false);
            _tilePanel = tiles.GetComponent<RectTransform>();
            _tilePanel.anchorMin = new Vector2(0.5f, 1); _tilePanel.anchorMax = new Vector2(0.5f, 1);
            _tilePanel.pivot = new Vector2(0.5f, 1); _tilePanel.anchoredPosition = new Vector2(0, -740);
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
            _progress.text = $"第 {_index + 1}/{_questions.Count} 题 · 连胜 {_streak}";
            _meta.text = poem == null ? "" : (isCi
                ? $"《{(string.IsNullOrEmpty(poem.Cipai) ? poem.Title : poem.Cipai)}》 · {poem.Author}"
                : $"《{poem.Title}》 {poem.Dynasty}·{poem.Author}");

            BuildLineRows(poem);
            BuildTiles();

            _qStartTime = Time.unscaledTime;
            _timeLimit = 20f + 6f * _answerSeq.Count + 1.5f * _q.TilePool.Count;
            _qEndTime = _qStartTime + _timeLimit;
            _running = true;
        }

        // Render each shown 句 as its own row of fixed-size cells: static chars + blank slots.
        // Slots are appended in (line, position) order, matching AnswerSequence / the Blanks order.
        private void BuildLineRows(Poem poem)
        {
            UiKit.ClearChildren(_linesPanel);
            _slots.Clear();

            // Distinct shown lines (guard against any duplicate index → repeated 句).
            var shown = new List<int>();
            foreach (var li in _q.ShownLines) if (!shown.Contains(li)) shown.Add(li);

            // Uniform cell size across rows, sized to the longest shown line AND constrained so every
            // row fits the card's inner height (≈324) — otherwise extra rows spill over the 词牌/提示 band.
            int maxLen = 1;
            foreach (var li in shown)
                if (poem != null && li >= 0 && li < poem.Lines.Count)
                    maxLen = Mathf.Max(maxLen, Chars(poem.Lines[li].Text).Count);
            int rows = Mathf.Max(1, shown.Count);
            float heightCap = (324f - 6f * rows - 16f * (rows - 1)) / rows;
            int perRowCap = rows >= 3 ? 90 : 120;
            float cell = Mathf.Min(perRowCap, 880f / Mathf.Max(1, maxLen), heightCap);
            int fontSize = Mathf.RoundToInt(cell * 0.54f);

            foreach (var li in shown)
            {
                var text = poem != null && li >= 0 && li < poem.Lines.Count ? poem.Lines[li].Text : "";
                var elems = Chars(text);

                var blanked = new HashSet<int>();
                foreach (var b in _q.Blanks)
                    if (LineOf(b) == li)
                        for (int k = 0; k < b.Count; k++) blanked.Add(b.Start + k);

                var rowGo = UiKit.Panel("LineRow", _linesPanel);
                UiKit.Pref(rowGo, minH: cell + 6);
                var hg = UiKit.HorizontalGroup(rowGo, spacing: 10, align: TextAnchor.MiddleCenter);
                hg.childForceExpandWidth = false; hg.childControlWidth = true; hg.childControlHeight = true;
                var row = rowGo.transform;

                for (int i = 0; i < elems.Count; i++)
                {
                    if (blanked.Contains(i))
                    {
                        var go = new GameObject("Slot" + li + "_" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                        go.transform.SetParent(row, false);
                        var img = go.GetComponent<Image>();
                        img.color = Design.Alpha(Design.Primary, 0.06f); // empty blank
                        UiKit.Pref(go, minW: cell, minH: cell);
                        var txt = UiKit.Text("t", go.transform, "", fontSize, TextAlignmentOptions.Center, Design.Primary);
                        UiKit.StretchFull(txt.gameObject, 2);
                        var slot = new SlotCell { Bg = img, Txt = txt };
                        int slotIdx = _slots.Count;
                        go.GetComponent<Button>().onClick.AddListener(() => OnSlotClick(slotIdx));
                        _slots.Add(slot);
                    }
                    else
                    {
                        var go = UiKit.Panel("Ch" + li + "_" + i, row, Design.SurfaceHighest); // char frame
                        UiKit.Pref(go, minW: cell, minH: cell);
                        var txt = UiKit.Text("t", go.transform, elems[i], fontSize, TextAlignmentOptions.Center, Design.Ink);
                        UiKit.StretchFull(txt.gameObject, 2);
                    }
                }
            }
        }

        // Back-compat: older single-line data has blanks without an explicit LineIndex (defaults 0).
        private int LineOf(WordClozeBlank b) =>
            (_q.LineIndices != null && _q.LineIndices.Count > 0) ? b.LineIndex : _q.BlankLineIndex;

        private void BuildTiles()
        {
            UiKit.ClearChildren(_tilePanel);
            int n = _q.TilePool.Count;
            // Shared grid policy with the generator (≥2 rows, ≤8 cols) so the pool fills a full rectangle.
            int rows = WordClozeGenerator.GridRows(n);
            int cols = Mathf.CeilToInt(n / (float)rows);
            float spacing = 12f;
            float cell = Mathf.Min(132f, (1000f - (cols - 1) * spacing) / cols);

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
                img.color = Design.SecondaryFixed;
                var txt = UiKit.Text("t", go.transform, _q.TilePool[i], fontSize, TextAlignmentOptions.Center, Design.Ink);
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
            tile.Bg.color = Design.Paper;
            tile.Txt.color = Design.Alpha(Design.Secondary, 0.5f);
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
            tile.Bg.color = Design.SecondaryFixed;
            tile.Txt.color = Design.Ink;
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
            string lineText = ShownLinesText(poem);
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

        // Joined text of all shown 句 (for the saved record / 错题本 line preview).
        private string ShownLinesText(Poem poem)
        {
            if (poem == null) return "";
            var sb = new StringBuilder();
            foreach (var li in _q.ShownLines)
                if (li >= 0 && li < poem.Lines.Count)
                {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append(poem.Lines[li].Text);
                }
            return sb.ToString();
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
