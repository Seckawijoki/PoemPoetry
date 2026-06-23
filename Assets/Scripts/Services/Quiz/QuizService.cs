using System.Collections.Generic;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>A question with its options shuffled for presentation, tracking the correct slot.</summary>
    public sealed class QuizQuestion
    {
        public Question Source;
        public IReadOnlyList<QuestionOption> Options;
        public int CorrectIndex;

        public string PoemId => Source != null ? Source.PoemId : null;
        public int BlankLineIndex => Source != null ? Source.BlankLineIndex : -1;
    }

    public sealed class QuizSession
    {
        public IReadOnlyList<QuizQuestion> Questions;
        public ChallengeSettings Settings;
        public int Total => Questions != null ? Questions.Count : 0;
    }

    /// <summary>
    /// Builds quiz sessions (no-repeat selection, option shuffling that preserves the correct
    /// index) and turns answers into <see cref="QuestionResult"/>. Pure and deterministic
    /// given a seeded <see cref="IRandomSource"/>.
    /// </summary>
    public sealed class QuizService
    {
        private readonly IRandomSource _rng;

        public QuizService(IRandomSource rng) { _rng = rng ?? new SystemRandomSource(); }

        public QuizSession BuildSession(IReadOnlyList<Question> pool, ChallengeSettings settings)
        {
            int count = settings != null ? settings.QuestionCount : 10;
            var shuffled = new List<Question>(pool ?? new List<Question>());
            ShuffleUtil.ShuffleInPlace(shuffled, _rng);

            // Prefer at most one question per poem to avoid testing the same poem twice.
            var usedPoems = new HashSet<string>();
            var chosen = new List<Question>();
            foreach (var q in shuffled)
            {
                if (!string.IsNullOrEmpty(q.PoemId) && !usedPoems.Add(q.PoemId)) continue;
                chosen.Add(q);
                if (chosen.Count >= count) break;
            }
            // Top up with remaining questions if the unique-poem pool was too small.
            if (chosen.Count < count)
            {
                var chosenSet = new HashSet<Question>(chosen);
                foreach (var q in shuffled)
                {
                    if (chosenSet.Contains(q)) continue;
                    chosen.Add(q);
                    if (chosen.Count >= count) break;
                }
            }

            var prepared = new List<QuizQuestion>(chosen.Count);
            foreach (var q in chosen) prepared.Add(Prepare(q));
            return new QuizSession { Questions = prepared, Settings = settings };
        }

        /// <summary>Shuffle a single question's four options, tracking where the correct one landed.</summary>
        public QuizQuestion Prepare(Question q)
        {
            var options = new List<QuestionOption>(q.AllOptions); // correct first, same references
            ShuffleUtil.ShuffleInPlace(options, _rng);
            int idx = options.IndexOf(q.Correct);
            return new QuizQuestion { Source = q, Options = options, CorrectIndex = idx };
        }

        public QuestionResult BuildResult(QuizQuestion qq, int chosenIndex, int timeMs)
        {
            string chosen = (chosenIndex >= 0 && qq.Options != null && chosenIndex < qq.Options.Count)
                ? qq.Options[chosenIndex].Text : "";
            return new QuestionResult
            {
                QuestionId = qq.Source.Id,
                PoemId = qq.Source.PoemId,
                BlankLineIndex = qq.Source.BlankLineIndex,
                BlankedText = qq.Source.Correct.Text,
                ChosenText = chosen,
                CorrectText = qq.Source.Correct.Text,
                IsCorrect = chosenIndex == qq.CorrectIndex,
                TimeMs = timeMs,
            };
        }

        /// <summary>
        /// Per-question time budget: enough to read four ~N-char options plus think, scaled by
        /// the question's internal difficulty. Clamped to a sane range. Pure & deterministic.
        /// </summary>
        public int TimeLimitSeconds(QuizQuestion qq)
        {
            int charCount = (qq != null && qq.Source != null && qq.Source.Correct != null)
                ? qq.Source.Correct.CharCount : 5;
            int difficulty = (qq != null && qq.Source != null) ? qq.Source.Difficulty : 2;
            double seconds = 5.0 + 1.5 * charCount + (difficulty - 1);
            seconds = System.Math.Max(6.0, System.Math.Min(30.0, seconds));
            return (int)System.Math.Round(seconds, System.MidpointRounding.AwayFromZero);
        }
    }
}
