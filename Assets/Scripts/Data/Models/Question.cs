using System.Collections.Generic;
using Newtonsoft.Json;

namespace PoemPoetry.Data
{
    /// <summary>
    /// A fill-in-the-blank multiple-choice question: one line of a poem is hidden and
    /// must be picked from 4 options (1 correct + 3 distractors).
    /// Materialized by the offline content pipeline; shipped in questions.json.
    /// </summary>
    public class Question
    {
        public string Id;
        public string PoemId;
        public int BlankLineIndex;                 // which poem line is hidden
        public QuestionOption Correct;
        public List<QuestionOption> Distractors = new List<QuestionOption>(); // exactly 3
        public int Difficulty;                     // 1..5
        public string Explanation;                 // optional teaching note
        public string SourceMode;                  // "corpus" | "authored"

        /// <summary>All four options in stored order (correct first). Shuffle at runtime.</summary>
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

    /// <summary>One answer option, carrying its own rhyme/structure metadata for validation.</summary>
    public class QuestionOption
    {
        public string Text;
        public int CharCount;
        public string LastChar;
        public string RhymeFinal;
        public string RhymeGroup;
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
                PosPattern = line.PosPattern,
                SourcePoemId = sourcePoemId,
            };
        }
    }
}
