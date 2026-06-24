using System.Collections.Generic;
using PoemPoetry.Data;

namespace PoemPoetry.UI
{
    /// <summary>Parameters for starting a quiz (challenge, 错题本 review, etc.).</summary>
    public sealed class QuizStartArgs
    {
        public int QuestionCount = 10;
        public List<int> Difficulties = new List<int>();    // empty = all tiers
        public List<string> Dynasties = new List<string>(); // empty = all dynasties
        public List<string> Types = new List<string>();     // empty = all 体裁 (诗/词/曲)
        public string Mode = "challenge";    // "challenge" | "wrongbook"
        public List<string> QuestionIds;     // when set, use these specific questions (review mode)
    }

    /// <summary>Parameters for starting a 逐词填空 (残句调控) game.</summary>
    public sealed class WordClozeStartArgs
    {
        public int QuestionCount = 10;
        public List<int> Difficulties = new List<int>();    // empty = all tiers
        public List<string> Dynasties = new List<string>(); // empty = all dynasties
        public List<string> Types = new List<string>();     // empty = all 体裁 (诗/词/曲)
        public string Mode = "wordcloze";    // "wordcloze" | "wrongbook"
        public List<string> QuestionIds;     // when set, use these specific questions (review mode)
    }

    /// <summary>Parameters for a 滑动找诗 (grid word-search) game.</summary>
    public sealed class SlideStartArgs
    {
        public int DirectionLevel = 1; // 1=straight4, 2=straight8, 3=snake4, 4=snake8
        public int GridCols = 9;
        public int GridRows = 9;
        public bool AllowOverlap = false;
        public bool FamousOnly = false;        // 名句: only place lines marked as 名句
        public List<int> Difficulties = new List<int>();
        public List<string> Dynasties = new List<string>();
        public SlideSnapshot Replay;   // when set, view/practice a past game (no recording)
    }

    public sealed class ResultArgs
    {
        public ChallengeRecord Record;
    }

    public sealed class RecordDetailArgs
    {
        public string RecordId;
        public List<string> Siblings;   // ordered record ids for prev/next jumping
        public int Index;
    }

    /// <summary>Optional filter for the records list: null/empty = all modes.</summary>
    public sealed class RecordsArgs
    {
        public string Mode;   // "challenge" | "slide" | null
        public string Title;  // optional screen title override
    }

    public sealed class PoemDetailArgs
    {
        public string PoemId;
        public QuestionResult ResultContext; // optional: "your answer" when arrived from a review row
        public List<string> Siblings;        // optional ordered poem ids for prev/next navigation
        public int Index;                    // index of PoemId within Siblings
    }
}
