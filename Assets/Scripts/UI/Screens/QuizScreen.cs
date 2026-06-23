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

        private TextMeshProUGUI _progress;
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

            // Quit (top-left) — returns without saving a record.
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

            // Timer bar: background + left-anchored fill whose width = remaining/total.
            var barBg = UiKit.Panel("TimerBar", transform, UiKit.CardAlt);
            var brt = UiKit.Rect(barBg);
            brt.anchorMin = new Vector2(0, 1); brt.anchorMax = new Vector2(1, 1); brt.pivot = new Vector2(0.5f, 1);
            brt.sizeDelta = new Vector2(-120, 16); brt.anchoredPosition = new Vector2(0, -156);
            var fill = UiKit.Panel("Fill", barBg.transform, UiKit.Accent);
            _timerFill = UiKit.Rect(fill);
            _timerFill.anchorMin = new Vector2(0, 0); _timerFill.anchorMax = new Vector2(1, 1);
            _timerFill.offsetMin = Vector2.zero; _timerFill.offsetMax = Vector2.zero;

            // Poem card (only context line + blank line).
            var card = UiKit.Panel("Card", transform, UiKit.Card);
            var crt = UiKit.Rect(card);
            crt.anchorMin = new Vector2(0, 0); crt.anchorMax = new Vector2(1, 1);
            crt.offsetMin = new Vector2(40, 760); crt.offsetMax = new Vector2(-40, -200);
            _meta = UiKit.Text("Meta", card.transform, "", 30, TextAlignmentOptions.Center, UiKit.Muted);
            UiKit.AnchorTop(_meta.gameObject, height: 54, topOffset: 18, sideMargin: 18);
            _poemText = UiKit.Text("Poem", card.transform, "", 58, TextAlignmentOptions.Center, UiKit.Ink);
            var prt = UiKit.Rect(_poemText.gameObject);
            prt.anchorMin = new Vector2(0, 0); prt.anchorMax = new Vector2(1, 1);
            prt.offsetMin = new Vector2(20, 20); prt.offsetMax = new Vector2(-20, -78);

            // Options.
            var options = UiKit.Panel("Options", transform);
            var ort = UiKit.Rect(options);
            ort.anchorMin = new Vector2(0, 0); ort.anchorMax = new Vector2(1, 0); ort.pivot = new Vector2(0.5f, 0);
            ort.sizeDelta = new Vector2(-80, 540); ort.anchoredPosition = new Vector2(0, 40);
            UiKit.VerticalGroup(options, spacing: 16, padX: 0, padY: 0, align: TextAnchor.UpperCenter);
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var btn = UiKit.Button("Opt" + i, options.transform, "", out var lbl, UiKit.CardAlt, 40);
                UiKit.Pref(btn.gameObject, minH: 116);
                btn.onClick.AddListener(() => Resolve(idx));
                _optionButtons.Add(btn);
                _optionLabels.Add(lbl);
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
            _progress.text = $"第 {_index + 1}/{_session.Total} 题   连胜 {_streak}";
            _meta.text = poem == null ? "" : (isCi
                ? (string.IsNullOrEmpty(poem.Cipai) ? poem.Title : poem.Cipai) + " · " + poem.Author
                : $"{poem.Title} · {poem.Dynasty}·{poem.Author}");
            _poemText.text = BuildMinimalText(poem, q.BlankLineIndex, null);

            for (int i = 0; i < 4; i++)
            {
                _optionButtons[i].interactable = true;
                _optionButtons[i].image.color = UiKit.CardAlt;
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
                    sb.Append(fill ?? "<color=#BF4038>" + new string('_', Mathf.Max(4, poem.Lines[i].CharCount * 2)) + "</color>");
                else
                    sb.Append(poem.Lines[i].Text);
            }
            return sb.ToString();
        }
    }
}
