using System.Collections;
using System.Collections.Generic;
using System.Text;
using PoemPoetry.Data;
using PoemPoetry.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoemPoetry.UI
{
    /// <summary>
    /// Core fill-in-the-blank quiz with immediate feedback. Shows only the tested line plus one
    /// neighbor (not the whole poem), runs a per-question countdown, and offers a quit button
    /// that returns without recording the run.
    /// </summary>
    public sealed class QuizScreen : UIScreen
    {
        private const float AdvanceDelay = 1.2f;
        private const string GreenHex = "#2E8B47";

        // Design palette ("Ink & Parchment" — designs/stitch_/DESIGN.md). Defined locally so the
        // shared UiKit palette used by other screens is left untouched.
        private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
        private static readonly Color Primary = Hex("#761519");          // cinnabar seal red
        private static readonly Color PrimaryContainer = Hex("#962d2d"); // selected / timer fill
        private static readonly Color SecondaryFixed = Hex("#e8e2d0");   // option button fill
        private static readonly Color SecondaryTxt = Hex("#625e50");     // labels / meta
        private static readonly Color BarTrack = Hex("#e7e2d4");         // timer track
        private static readonly Color CardWhite = Hex("#ffffff");        // focused "scroll" card
        private static readonly Color Outline = Hex("#8a716f");          // hairline / corner motif

        private QuizStartArgs _args;
        private QuizSession _session;
        private int _index;
        private int _streak;
        private int _bestStreak;
        private bool _locked;
        private bool _running;          // timer active
        private float _qStartTime;
        private float _qEndTime;
        private float _timeLimit;
        private float _sessionStart;
        private readonly List<QuestionResult> _results = new List<QuestionResult>();

        private TextMeshProUGUI _progressValue;
        private TextMeshProUGUI _streakValue;
        private TextMeshProUGUI _timerText;
        private RectTransform _timerFill;
        private TextMeshProUGUI _meta;
        private TextMeshProUGUI _poemText;
        private readonly List<Button> _optionButtons = new List<Button>();
        private readonly List<TextMeshProUGUI> _optionLabels = new List<TextMeshProUGUI>();

        protected override void OnShow(object args)
        {
            _args = args as QuizStartArgs ?? new QuizStartArgs();
            BuildSession();
            BuildLayout();
            if (_session.Total == 0)
            {
                _poemText.text = "题库暂无符合条件的题目。\n请返回切换筛选，或扩充题库。";
                return;
            }
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
            List<Question> pool = (_args.QuestionIds != null && _args.QuestionIds.Count > 0)
                ? Services.Content.GetQuestionsByIds(_args.QuestionIds)
                // distractors computed at runtime from the selected dynasty + difficulty<=max corpus
                : Services.Content.BuildRuntimeQuestions(settings, new SystemRandomSource(), settings.QuestionCount);
            _session = Services.Quiz.BuildSession(pool, settings);
            _sessionStart = Time.unscaledTime;
        }

        private void BuildLayout()
        {
            var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            bg.color = UiKit.Paper;

            float safeTop = UiKit.SafeTopInset(gameObject);
            const float headerH = 132f;
            const float side = 48f;

            // ── Top app bar: back (left) · title (center) · close (right), with a hairline base.
            var header = UiKit.Panel("Header", transform, UiKit.Paper);
            var hrt = UiKit.Rect(header);
            hrt.anchorMin = new Vector2(0, 1); hrt.anchorMax = new Vector2(1, 1); hrt.pivot = new Vector2(0.5f, 1);
            hrt.sizeDelta = new Vector2(0, headerH + safeTop); hrt.anchoredPosition = Vector2.zero;
            var hairline = UiKit.Panel("HeaderBorder", header.transform, new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, 0.10f));
            var hlr = UiKit.Rect(hairline);
            hlr.anchorMin = new Vector2(0, 0); hlr.anchorMax = new Vector2(1, 0); hlr.pivot = new Vector2(0.5f, 0);
            hlr.sizeDelta = new Vector2(0, 2); hlr.anchoredPosition = Vector2.zero;

            IconButton("Back", "←", 52, Primary, new Vector2(0, 1), new Vector2(20, -(safeTop + 24)), Quit);
            IconButton("Close", "×", 60, SecondaryTxt, new Vector2(1, 1), new Vector2(-20, -(safeTop + 24)), Quit);

            // ── Status row: PROGRESS (left) / STREAK (right), then a thin countdown bar.
            float top0 = safeTop + headerH + 40f;
            CapLabel("ProgressCap", "PROGRESS", new Vector2(0, 1), TextAlignmentOptions.TopLeft, new Vector2(side, -top0));
            _progressValue = ValueLabel("ProgressVal", new Vector2(0, 1), TextAlignmentOptions.TopLeft,
                new Vector2(side, -(top0 + 32)), UiKit.Ink);
            CapLabel("StreakCap", "STREAK", new Vector2(1, 1), TextAlignmentOptions.TopRight, new Vector2(-side, -top0));
            _streakValue = ValueLabel("StreakVal", new Vector2(1, 1), TextAlignmentOptions.TopRight,
                new Vector2(-side, -(top0 + 32)), Primary);

            var barBg = UiKit.Panel("TimerBar", transform, BarTrack);
            var brt = UiKit.Rect(barBg);
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(-2 * side, 6); brt.anchoredPosition = new Vector2(0, -(top0 + 108));
            var fill = UiKit.Panel("Fill", barBg.transform, PrimaryContainer);
            _timerFill = UiKit.Rect(fill);
            _timerFill.anchorMin = new Vector2(0, 0); _timerFill.anchorMax = new Vector2(1, 1);
            _timerFill.offsetMin = Vector2.zero; _timerFill.offsetMax = Vector2.zero;

            _timerText = UiKit.Text("Timer", transform, "", 28, TextAlignmentOptions.Center, Primary);
            UiKit.AnchorTop(_timerText.gameObject, height: 40, topOffset: top0 + 128, sideMargin: 40);

            // ── Question card (the "scroll"): white sheet, lattice corners, meta + clue + divider.
            float cardTop = top0 + 188f;
            const float optionsReserve = 600f;
            var card = UiKit.Panel("Card", transform, CardWhite);
            var crt = UiKit.Rect(card);
            crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 1);
            crt.offsetMin = new Vector2(side, optionsReserve); crt.offsetMax = new Vector2(-side, -cardTop);
            AddCorners(card);

            _meta = UiKit.Text("Meta", card.transform, "", 30, TextAlignmentOptions.Center, SecondaryTxt);
            UiKit.AnchorTop(_meta.gameObject, height: 50, topOffset: 44, sideMargin: 30);

            // Clue area: poem lines + a short centered divider, vertically centered as a group.
            var clue = UiKit.Panel("Clue", card.transform);
            var clr = UiKit.Rect(clue);
            clr.anchorMin = new Vector2(0, 0); clr.anchorMax = new Vector2(1, 1);
            clr.offsetMin = new Vector2(40, 40); clr.offsetMax = new Vector2(-40, -120);
            var vg = UiKit.VerticalGroup(clue, spacing: 30, padX: 0, padY: 0, align: TextAnchor.MiddleCenter);
            vg.childForceExpandHeight = false;

            _poemText = UiKit.Text("Poem", clue.transform, "", 92, TextAlignmentOptions.Center, UiKit.Ink, wrap: true);
            _poemText.enableAutoSizing = true;
            _poemText.fontSizeMin = 44; _poemText.fontSizeMax = 96;
            // No object-level tracking: that splits the blank's underscores into separate dashes.
            // Hanzi lines get their tracking via per-run <cspace> tags in BuildMinimalText instead.
            _poemText.characterSpacing = 0f;
            _poemText.lineSpacing = 12f;
            // Fixed-height slot so the layout group doesn't depend on TMP's intrinsic height while
            // auto-sizing; the text centers within and shrinks to fit both width and this box.
            UiKit.Pref(_poemText.gameObject, minH: 300);

            var divSlot = UiKit.Panel("Divider", clue.transform);
            UiKit.Pref(divSlot.gameObject, minH: 2);
            var dline = UiKit.Panel("DividerLine", divSlot.transform, new Color(PrimaryContainer.r, PrimaryContainer.g, PrimaryContainer.b, 0.30f));
            var dlr = UiKit.Rect(dline);
            dlr.anchorMin = dlr.anchorMax = dlr.pivot = new Vector2(0.5f, 0.5f);
            dlr.sizeDelta = new Vector2(160, 2); dlr.anchoredPosition = Vector2.zero;

            // ── Answer options: four full-width parchment buttons, sharp corners.
            var options = UiKit.Panel("Options", transform);
            var ort = UiKit.Rect(options);
            ort.anchorMin = new Vector2(0, 0); ort.anchorMax = new Vector2(1, 0); ort.pivot = new Vector2(0.5f, 0);
            ort.sizeDelta = new Vector2(-2 * side, 512); ort.anchoredPosition = new Vector2(0, 64);
            UiKit.VerticalGroup(options, spacing: 16, padX: 0, padY: 0, align: TextAnchor.UpperCenter);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var btn = UiKit.Button("Opt" + i, options.transform, "", out var lbl, SecondaryFixed, 44);
                UiKit.Pref(btn.gameObject, minH: 116);
                btn.onClick.AddListener(() => Resolve(idx));
                _optionButtons.Add(btn);
                _optionLabels.Add(lbl);
            }
        }

        /// <summary>Borderless icon glyph button anchored to a corner of the screen.</summary>
        private void IconButton(string name, string glyph, float size, Color color,
            Vector2 corner, Vector2 pos, System.Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(transform, false);
            go.GetComponent<Image>().color = new Color(0, 0, 0, 0); // transparent hit area
            var rt = UiKit.Rect(go);
            rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
            rt.sizeDelta = new Vector2(84, 84); rt.anchoredPosition = pos;
            var lbl = UiKit.Text("Glyph", go.transform, glyph, size, TextAlignmentOptions.Center, color);
            UiKit.StretchFull(lbl.gameObject);
            go.GetComponent<Button>().onClick.AddListener(() => onClick());
        }

        /// <summary>Small uppercase caption (PROGRESS / STREAK).</summary>
        private void CapLabel(string name, string text, Vector2 corner, TextAlignmentOptions align, Vector2 pos)
        {
            var t = UiKit.Text(name, transform, text, 22, align, SecondaryTxt);
            t.characterSpacing = 6f;
            var rt = UiKit.Rect(t.gameObject);
            rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
            rt.sizeDelta = new Vector2(420, 28); rt.anchoredPosition = pos;
        }

        private TextMeshProUGUI ValueLabel(string name, Vector2 corner, TextAlignmentOptions align, Vector2 pos, Color color)
        {
            var t = UiKit.Text(name, transform, "", 38, align, color);
            var rt = UiKit.Rect(t.gameObject);
            rt.anchorMin = rt.anchorMax = corner; rt.pivot = corner;
            rt.sizeDelta = new Vector2(520, 48); rt.anchoredPosition = pos;
            return t;
        }

        /// <summary>Window-lattice corner motif (an L-bracket) at each card corner.</summary>
        private static void AddCorners(GameObject card)
        {
            var col = new Color(Outline.r, Outline.g, Outline.b, 0.30f);
            const float arm = 46f, thick = 3f, inset = 20f;
            Vector2[] corners = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) };
            foreach (var a in corners)
            {
                float sx = a.x == 0 ? 1f : -1f;
                float sy = a.y == 1 ? -1f : 1f;
                var pos = new Vector2(sx * inset, sy * inset);
                var h = UiKit.Rect(UiKit.Panel("CornerH", card.transform, col));
                h.anchorMin = h.anchorMax = h.pivot = a; h.sizeDelta = new Vector2(arm, thick); h.anchoredPosition = pos;
                var v = UiKit.Rect(UiKit.Panel("CornerV", card.transform, col));
                v.anchorMin = v.anchorMax = v.pivot = a; v.sizeDelta = new Vector2(thick, arm); v.anchoredPosition = pos;
            }
        }

        private QuizQuestion Current => _session.Questions[_index];

        private void ShowQuestion()
        {
            _locked = false;
            _qStartTime = Time.unscaledTime;
            _timeLimit = Services.Quiz.TimeLimitSeconds(Current);
            _qEndTime = _qStartTime + _timeLimit;
            _running = true;

            var q = Current;
            var poem = Services.Content.GetPoem(q.PoemId);
            bool isCi = poem != null && poem.Type == "词";
            _progressValue.text = $"第 {_index + 1}/{_session.Total} 题";
            _streakValue.text = $"连胜 {_streak}";
            _meta.text = poem == null ? "" : (isCi
                ? (string.IsNullOrEmpty(poem.Cipai) ? poem.Title : poem.Cipai) + " · " + poem.Author
                : $"{poem.Title} · {poem.Dynasty}·{poem.Author}");
            _poemText.text = BuildMinimalText(poem, q.BlankLineIndex, null);

            for (int i = 0; i < 4; i++)
            {
                _optionButtons[i].interactable = true;
                _optionButtons[i].image.color = SecondaryFixed;
                _optionLabels[i].text = q.Options[i].Text;
                _optionLabels[i].color = UiKit.Ink;
            }
        }

        private void Update()
        {
            if (!_running) return;
            float remain = _qEndTime - Time.unscaledTime;
            if (remain < 0) remain = 0;
            _timerText.text = $"剩余 {Mathf.CeilToInt(remain)} 秒";
            if (_timerFill != null)
                _timerFill.anchorMax = new Vector2(_timeLimit > 0 ? Mathf.Clamp01(remain / _timeLimit) : 0f, 1f);
            if (remain <= 0f) Resolve(-1); // timeout = no answer
        }

        /// <summary>Resolve the current question. chosen = -1 means timed out.</summary>
        private void Resolve(int chosen)
        {
            if (_locked) return;
            _locked = true;
            _running = false;

            var q = Current;
            bool correct = chosen == q.CorrectIndex;
            int ms = Mathf.RoundToInt((Time.unscaledTime - _qStartTime) * 1000f);
            _results.Add(Services.Quiz.BuildResult(q, chosen, ms));
            if (correct) { _streak++; if (_streak > _bestStreak) _bestStreak = _streak; }
            else _streak = 0;

            for (int i = 0; i < 4; i++)
            {
                _optionButtons[i].interactable = false;
                if (i == q.CorrectIndex) { _optionButtons[i].image.color = UiKit.Good; _optionLabels[i].color = Color.white; }
                else if (i == chosen) { _optionButtons[i].image.color = UiKit.Bad; _optionLabels[i].color = Color.white; }
                else _optionLabels[i].color = UiKit.Muted;
            }

            var poem = Services.Content.GetPoem(q.PoemId);
            _poemText.text = BuildMinimalText(poem, q.BlankLineIndex, $"<color={GreenHex}>{q.Source.Correct.Text}</color>");

            if (AudioManager.Instance != null) { if (correct) AudioManager.Instance.PlayCorrect(); else AudioManager.Instance.PlayWrong(); }
            UpdateLeitner(q.Source.Id, q.PoemId, correct);
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
            if (_index >= _session.Total) Finish();
            else ShowQuestion();
        }

        private async void Finish()
        {
            int duration = Mathf.RoundToInt(Time.unscaledTime - _sessionStart);
            var settings = _session.Settings ?? new ChallengeSettings();
            var record = await Services.Records.SaveCompletedAsync(_results, settings, duration, _args.Mode);
            record.BestStreak = Mathf.Max(record.BestStreak, _bestStreak);
            Nav.Replace<ResultScreen>(new ResultArgs { Record = record });
        }

        private void Quit()
        {
            _running = false;
            _locked = true;
            Nav.Pop(); // back to config; nothing recorded
        }

        /// <summary>
        /// Show the tested line in the context of its enclosing 句号 sentence (never the whole
        /// poem): every line sharing the blank's group is shown, with the blank in place. If the
        /// group is a single 分句, one adjacent line (prev, else next) is added for context.
        /// </summary>
        private static string BuildMinimalText(Poem poem, int blankIndex, string fill)
        {
            if (poem == null || poem.Lines == null || poem.Lines.Count == 0) return "";
            int count = poem.Lines.Count;
            int g = DifficultyRules.EffectiveGroup(poem, blankIndex);

            var idxs = new List<int>();
            for (int i = 0; i < count; i++)
                if (DifficultyRules.EffectiveGroup(poem, i) == g) idxs.Add(i);
            if (idxs.Count == 1) // singleton sentence → borrow one neighbor
            {
                if (blankIndex > 0) idxs.Insert(0, blankIndex - 1);
                else if (blankIndex + 1 < count) idxs.Add(blankIndex + 1);
            }

            var sb = new StringBuilder();
            for (int k = 0; k < idxs.Count; k++)
            {
                int i = idxs[k];
                if (k > 0) sb.Append('\n');
                if (i == blankIndex)
                {
                    // Blank: plain underscores (no <cspace>) so the glyphs join into one continuous rule.
                    if (fill != null) sb.Append("<cspace=0.1em>").Append(fill).Append("</cspace>");
                    else sb.Append("<color=#BF4038>").Append('_', Mathf.Max(4, poem.Lines[i].CharCount * 2)).Append("</color>");
                }
                else
                {
                    sb.Append("<cspace=0.1em>").Append(poem.Lines[i].Text).Append("</cspace>");
                }
            }
            return sb.ToString();
        }
    }
}
