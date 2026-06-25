using System.Collections.Generic;
using Newtonsoft.Json;

namespace PoemPoetry.Data
{
    /// <summary>
    /// A fill-in-the-blank multiple-choice question: one line of a poem is hidden and
    /// must be picked from 4 options (1 correct + 3 distractors). Schema v2: the question is
    /// lightweight — it only references its <see cref="ClusterId"/> (a shared pool of
    /// 同字数/同韵组/相近平仄 lines). Distractors are NOT serialized; <see cref="Services.ContentService"/>
    /// fills the transient <see cref="Distractors"/> pool from the cluster at session-build time,
    /// and <see cref="Services.QuizService.Prepare"/> samples 3 per attempt.
    /// Materialized by the offline content pipeline; shipped in questions.json.
    /// </summary>
    public class Question
    {
        public string Id;
        public string PoemId;
        public int BlankLineIndex;                 // which poem line is hidden
        public int ClusterId = -1;                 // → LineCluster providing the distractor pool (v2)
        public QuestionOption Correct;
        public int Difficulty;                     // 1..5
        public string Explanation;                 // optional teaching note
        public string SourceMode;                  // "corpus" | "authored"

        /// <summary>Runtime-only distractor pool, filled from the cluster (not serialized). The quiz
        /// samples a 3-distractor subset per attempt; see PoemPoetry.Services.QuizService.Prepare.</summary>
        [JsonIgnore]
        public List<QuestionOption> Distractors = new List<QuestionOption>();

        /// <summary>Correct answer followed by the populated distractor pool (correct first).</summary>
        [JsonIgnore]
        public IReadOnlyList<QuestionOption> AllOptions
        {
            get
            {
                var list = new List<QuestionOption>(4) { Correct };
                if (Distractors != null) list.AddRange(Distractors);
                return list;
            }
        }
    }

    /// <summary>
    /// A shared pool of interchangeable lines for distractors: every line has the same 字数 +
    /// 新韵韵组 + 平仄型 (起/收). Any line is a plausible distractor for any other in the cluster,
    /// so a question only stores its <see cref="Question.ClusterId"/> instead of an embedded pool
    /// (kills the ~14× duplication of schema v1). Each corpus line is stored once across the bank.
    /// </summary>
    public class LineCluster
    {
        public int Id;
        public int CharCount;
        public string RhymeGroup;
        public string ToneType;                    // 平仄型, e.g. "仄起平收"
        public List<QuestionOption> Lines = new List<QuestionOption>();
    }

    /// <summary>One answer option, carrying its own rhyme/structure metadata for validation.</summary>
    public class QuestionOption
    {
        public string Text;
        public int CharCount;
        public string LastChar;
        public string RhymeFinal;
        public string RhymeGroup;
        public string Pingshui;       // 平水韵 韵部 id, for same-韵部 distractor preference
        public string PosPattern;
        public string SourcePoemId;   // provenance when pulled from corpus, "" if authored

        public static QuestionOption FromLine(PoemLine line, string sourcePoemId)
        {
            return new QuestionOption
            {
                Text = line.Text,
                CharCount = line.CharCount,
                LastChar = line.LastChar,
                RhymeFinal = line.RhymeFinal,
                RhymeGroup = line.RhymeGroup,
                Pingshui = line.PingshuiRhyme,
                PosPattern = line.PosPattern,
                SourcePoemId = sourcePoemId,
            };
        }
    }
}
