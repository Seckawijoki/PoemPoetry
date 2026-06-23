using System.Collections.Generic;

namespace PoemPoetry.Data
{
    /// <summary>A completed challenge, persisted to the user's local records.</summary>
    public class ChallengeRecord
    {
        public string Id;
        public string CompletedAtUtc;     // ISO-8601 round-trip string ("o")
        public string Mode;               // "challenge" | "wrongbook" | "slide"
        public ChallengeSettings Settings = new ChallengeSettings();
        public int AccuracyPercent;       // 0..100, rounded
        public int CorrectCount;
        public int Total;
        public int DurationSeconds;
        public int BestStreak;
        public List<QuestionResult> Items = new List<QuestionResult>();
        public SlideSnapshot Slide;       // populated only for mode == "slide"

        public ChallengeRecordSummary ToSummary()
        {
            return new ChallengeRecordSummary
            {
                Id = Id,
                CompletedAtUtc = CompletedAtUtc,
                Mode = Mode,
                AccuracyPercent = AccuracyPercent,
                CorrectCount = CorrectCount,
                Total = Total,
                DurationSeconds = DurationSeconds,
                BestStreak = BestStreak,
            };
        }
    }

    /// <summary>Lightweight row for the records list (records/index.json).</summary>
    public class ChallengeRecordSummary
    {
        public string Id;
        public string CompletedAtUtc;
        public string Mode;
        public int AccuracyPercent;
        public int CorrectCount;
        public int Total;
        public int DurationSeconds;
        public int BestStreak;
    }

    /// <summary>The filters/length a challenge was started with.</summary>
    public class ChallengeSettings
    {
        public int QuestionCount = 10;
        public List<int> Difficulties = new List<int>();    // empty = all difficulty tiers
        public List<string> Dynasties = new List<string>(); // empty = all dynasties
        public List<string> Types = new List<string>();     // empty = all 体裁 (诗/词/曲)
    }

    /// <summary>A replayable snapshot of a 滑动找诗 game (stored on slide records).</summary>
    public class SlideSnapshot
    {
        public int Size;   // columns
        public int Rows;   // rows (0 = square, == Size)
        public List<string> Cells = new List<string>();        // length Size*Rows (row-major)
        public List<SlideTargetSnapshot> Targets = new List<SlideTargetSnapshot>();
    }

    public class SlideTargetSnapshot
    {
        public string Text;
        public string Title;
        public string PoemId;
        public List<int> Cells = new List<int>();
        public bool Found;
    }

    /// <summary>Per-question outcome inside a record; powers the review view and 错题本.</summary>
    public class QuestionResult
    {
        public string QuestionId;
        public string PoemId;
        public int BlankLineIndex;
        public string BlankedText;   // the correct line that was hidden
        public string ChosenText;
        public string CorrectText;
        public bool IsCorrect;
        public int TimeMs;
    }
}
