using System.Collections.Generic;

namespace PoemPoetry.Data
{
    /// <summary>
    /// 逐词填空 (残句调控) question: one poem line is shown with one or more 名词/动词 keywords
    /// blanked out. The player assembles the missing characters by tapping single-character tiles
    /// from <see cref="TilePool"/> (answer chars + same-类型/same-平仄 distractor chars).
    /// Materialized offline by the content pipeline; shipped in word_questions.json.
    /// </summary>
    public class WordClozeQuestion
    {
        public string Id;                                   // "wc-{poemId}-{lineA}-{lineB}"
        public string PoemId;
        public int BlankLineIndex;                          // primary (first) shown line — back-compat for records/难度
        public List<int> LineIndices = new List<int>();     // all shown lines (≥2), ascending; empty = single [BlankLineIndex]
        public List<WordClozeBlank> Blanks = new List<WordClozeBlank>(); // ascending by (LineIndex, Start)
        public List<string> TilePool = new List<string>();  // shuffled: answer chars + distractors
        public int Difficulty;                              // 1..5

        /// <summary>
        /// Shown lines (distinct, in order), falling back to the single BlankLineIndex for older
        /// single-line data. De-duplicated so a stray repeated index never renders/records a 句 twice.
        /// </summary>
        public IReadOnlyList<int> ShownLines
        {
            get
            {
                var src = (LineIndices != null && LineIndices.Count > 0) ? LineIndices : new List<int> { BlankLineIndex };
                var seen = new HashSet<int>();
                var result = new List<int>();
                foreach (var i in src) if (seen.Add(i)) result.Add(i);
                return result;
            }
        }

        /// <summary>All answer characters in slot order (blank1 ch1, blank1 ch2, blank2 ch1, ...).</summary>
        public IReadOnlyList<string> AnswerSequence()
        {
            var seq = new List<string>();
            if (Blanks != null)
                foreach (var b in Blanks)
                    if (b.AnswerChars != null) seq.AddRange(b.AnswerChars);
            return seq;
        }
    }

    /// <summary>One blanked keyword within a line.</summary>
    public class WordClozeBlank
    {
        public int LineIndex;                    // which poem line this blank is in (for multi-句 questions)
        public int Start;                       // text-element index in the ORIGINAL line text (punctuation NOT stripped)
        public int Count;                        // number of characters blanked (= AnswerChars.Count)
        public List<string> AnswerChars = new List<string>(); // the correct characters, in order
        public string Pos;                       // "n" | "v"
        public string Semantic;                  // 颜色/动物/数字/植物/方位/时间/通用
    }

    /// <summary>One 名词/动词 word in the word bank (used to choose what to blank).</summary>
    public class WordEntry
    {
        public string Text;
        public string Pos;                       // "n" | "v"
        public int CharCount;
        public List<string> Pinyin = new List<string>();
        public string Tone;                      // per-char 平仄 string, e.g. "平仄"
        public string Semantic;                  // 颜色/动物/数字/植物/方位/时间/通用
        public List<string> Sources = new List<string>(); // poem lines the word appears in
    }

    /// <summary>Root object of word_bank.json.</summary>
    public class WordBankFile
    {
        public int SchemaVersion = 1;
        public List<WordEntry> Words = new List<WordEntry>();
    }

    /// <summary>Root object of word_questions.json.</summary>
    public class WordClozeQuestionFile
    {
        public int SchemaVersion = 1;
        public List<WordClozeQuestion> Questions = new List<WordClozeQuestion>();
    }

    /// <summary>Root object of semantic_categories.json: 类别 → 该类的字[].</summary>
    public class SemanticCategoryFile
    {
        public int SchemaVersion = 1;
        public Dictionary<string, List<string>> Categories = new Dictionary<string, List<string>>();
    }
}
