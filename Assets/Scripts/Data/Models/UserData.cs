using System.Collections.Generic;

namespace PoemPoetry.Data
{
    /// <summary>A favorited poem (poem-level favoriting).</summary>
    public class FavoriteEntry
    {
        public string PoemId;
        public string AddedAtUtc;
    }

    /// <summary>A 错题本 entry tracked with a 3-box Leitner schedule.</summary>
    public class WrongBookEntry
    {
        public string QuestionId;
        public string PoemId;
        public int Box = 1;             // 1..3 (1 = new/most frequent review)
        public string NextReviewUtc;    // ISO-8601; due when now >= this
        public string LastResult;       // "wrong" | "right"
        public string AddedAtUtc;
        public int ReviewCount;
    }

    /// <summary>User preferences + last-used selections, persisted locally.</summary>
    public class UserSettings
    {
        public bool MusicOn = true;
        public bool SfxOn = true;
        public float MasterVolume = 1f;
        public int LastChallengeLength = 10;

        // 全局阅读偏好：诗词正文是否按句号组换行（同组诗句并排一行）。
        public bool GroupedLineBreak = false;

        // Last answer-config selections.
        public List<int> LastDifficulties = new List<int>();
        public List<string> LastDynasties = new List<string>();
        public List<string> LastTypes = new List<string>();

        // Last 滑动找诗 selections.
        public int LastSlideLevel = 1;
        public int LastSlideCols = 9;
        public int LastSlideRows = 9;
        public int LastSlideLineCount = 5;
        public bool LastSlideOverlap = false;
        public bool LastSlideOverlapHint = true;   // 重叠字提示：开=拖动时即用专色标出重叠字
        public bool LastSlideFamousOnly = false;
        public List<int> LastSlideDifficulties = new List<int>();
        public List<string> LastSlideDynasties = new List<string>();
        public List<string> LastSlideTypes = new List<string>();
    }
}
