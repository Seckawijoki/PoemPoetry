using System.Collections.Generic;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Generates fill-in-the-blank questions from a corpus of poem lines. Distractors are
    /// chosen to satisfy the hard constraints — same character count and same 新韵 rhyme
    /// group — while preferring identical rhyme final and parallel POS structure, and
    /// avoiding near-duplicates of the answer or of each other.
    ///
    /// Used offline by the editor content tool to materialize questions.json, and reusable
    /// at runtime for an endless/practice mode. Pure, UnityEngine-free, deterministic given
    /// a seeded <see cref="IRandomSource"/>.
    /// </summary>
    public sealed class QuestionGenerator
    {
        public sealed class CorpusLine
        {
            public string PoemId;
            public PoemLine Line;
            public string Type;   // 诗 / 词 / 曲 — for 同体裁 distractor preference
            public string Cipai;  // 词牌名 ("" for 诗/曲) — for 同词牌 distractor preference
        }

        private readonly IRandomSource _rng;
        private readonly Dictionary<string, List<CorpusLine>> _index = new Dictionary<string, List<CorpusLine>>();

        public QuestionGenerator(IEnumerable<Poem> poems, IRandomSource rng)
        {
            _rng = rng ?? new SystemRandomSource();
            foreach (var p in poems)
            {
                if (p.Lines == null) continue;
                foreach (var line in p.Lines)
                {
                    var cl = new CorpusLine { PoemId = p.Id, Line = line, Type = p.Type, Cipai = p.Cipai };
                    var key = Key(line.CharCount, line.RhymeGroup);
                    if (!_index.TryGetValue(key, out var list))
                    {
                        list = new List<CorpusLine>();
                        _index[key] = list;
                    }
                    list.Add(cl);
                }
            }
        }

        private static string Key(int charCount, string group) => charCount + "|" + (group ?? "");

        /// <summary>Pick up to <paramref name="count"/> distractor options for a target line.</summary>
        public List<QuestionOption> SelectDistractors(string poemId, PoemLine target, int count = 3,
            string targetType = null, string targetCipai = null)
        {
            var result = new List<QuestionOption>();
            if (target == null || target.CharCount <= 0 || string.IsNullOrEmpty(target.RhymeGroup))
                return result;
            if (!_index.TryGetValue(Key(target.CharCount, target.RhymeGroup), out var bucket))
                return result;

            // Filter candidates: different poem, not (near-)identical, deduped text.
            var seen = new HashSet<string>();
            var unique = new List<CorpusLine>();
            foreach (var cl in bucket)
            {
                if (cl.PoemId == poemId) continue;
                if (cl.Line.Text == target.Text) continue;
                if (Levenshtein(cl.Line.Text, target.Text) <= 1) continue;
                if (seen.Add(cl.Line.Text)) unique.Add(cl);
            }

            // Shuffle for variety, then stable-sort by descending suitability score.
            ShuffleUtil.ShuffleInPlace(unique, _rng);
            unique.Sort((a, b) => Score(b, target, targetType, targetCipai).CompareTo(Score(a, target, targetType, targetCipai)));

            // Greedy pick with diversity: avoid two distractors that overlap heavily.
            var picked = new List<CorpusLine>();
            foreach (var c in unique)
            {
                bool tooSimilar = false;
                foreach (var pk in picked)
                {
                    if (CharOverlap(c.Line.Text, pk.Line.Text) > 0.6) { tooSimilar = true; break; }
                }
                if (tooSimilar) continue;
                picked.Add(c);
                if (picked.Count >= count) break;
            }
            // If diversity filtering left us short, top up ignoring diversity.
            if (picked.Count < count)
            {
                foreach (var c in unique)
                {
                    if (picked.Contains(c)) continue;
                    picked.Add(c);
                    if (picked.Count >= count) break;
                }
            }

            foreach (var c in picked) result.Add(QuestionOption.FromLine(c.Line, c.PoemId));
            return result;
        }

        /// <summary>Build one question; returns null if the blank is unsuitable or has too few distractors.</summary>
        public Question Generate(Poem poem, int blankLineIndex, int distractorCount = 3)
        {
            if (poem == null || poem.Lines == null) return null;
            if (blankLineIndex < 0 || blankLineIndex >= poem.Lines.Count) return null;

            var target = poem.Lines[blankLineIndex];
            if (string.IsNullOrEmpty(target.RhymeGroup)) return null; // rhyme uncomputable -> skip

            var distractors = SelectDistractors(poem.Id, target, distractorCount, poem.Type, poem.Cipai);
            if (distractors.Count < distractorCount) return null;

            return new Question
            {
                Id = "q-" + poem.Id + "-" + blankLineIndex,
                PoemId = poem.Id,
                BlankLineIndex = blankLineIndex,
                Correct = QuestionOption.FromLine(target, poem.Id),
                Distractors = distractors,
                Difficulty = DifficultyFor(poem, target),
                Explanation = "",
                SourceMode = "corpus",
            };
        }

        /// <summary>Generate questions for every viable blank line of a poem.</summary>
        public List<Question> GenerateForPoem(Poem poem, int distractorCount = 3, bool rhymeLinesOnly = false)
        {
            var list = new List<Question>();
            if (poem == null || poem.Lines == null) return list;
            for (int i = 0; i < poem.Lines.Count; i++)
            {
                if (rhymeLinesOnly && !poem.Lines[i].IsRhymeLine) continue;
                var q = Generate(poem, i, distractorCount);
                if (q != null) list.Add(q);
            }
            return list;
        }

        private static int DifficultyFor(Poem poem, PoemLine target)
        {
            int d = 2;
            if (poem.Fame == "obscure") d += 2;
            else if (poem.Fame == "common") d += 1;
            if (target.CharCount >= 7) d += 1;
            return System.Math.Min(5, System.Math.Max(1, d));
        }

        private static int Score(CorpusLine cand, PoemLine target, string targetType, string targetCipai)
        {
            int s = 0;
            var c = cand.Line;
            // 韵脚细分 (#2): 同平水韵韵部最权威 (+4, 跳过 RhymeFinal 避免双计)；平水韵未知时回退到同拼音韵母 (+3)。
            if (!string.IsNullOrEmpty(c.PingshuiRhyme) && !string.IsNullOrEmpty(target.PingshuiRhyme))
            {
                if (c.PingshuiRhyme == target.PingshuiRhyme) s += 4;
            }
            else if (!string.IsNullOrEmpty(c.RhymeFinal) && c.RhymeFinal == target.RhymeFinal) s += 3;
            if (!string.IsNullOrEmpty(c.PosPattern) && c.PosPattern == target.PosPattern) s += 2;

            // 体裁/词牌偏好 (#4): 同词牌最相配，否则同体裁略加分。小语料下自动退化为现有行为。
            if (!string.IsNullOrEmpty(targetCipai) && !string.IsNullOrEmpty(cand.Cipai) && cand.Cipai == targetCipai) s += 3;
            else if (!string.IsNullOrEmpty(targetType) && cand.Type == targetType) s += 1;

            var overlap = CharOverlap(c.Text, target.Text);
            if (overlap > 0.6) s -= 2;
            else if (overlap > 0.4) s -= 1;
            return s;
        }

        // Jaccard overlap on character sets.
        private static double CharOverlap(string a, string b)
        {
            var sa = new HashSet<char>(a);
            var sb = new HashSet<char>(b);
            if (sa.Count == 0 || sb.Count == 0) return 0;
            int inter = 0;
            foreach (var c in sa) if (sb.Contains(c)) inter++;
            int union = sa.Count + sb.Count - inter;
            return union == 0 ? 0 : (double)inter / union;
        }

        private static int Levenshtein(string a, string b)
        {
            if (a == b) return 0;
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var prev = new int[m + 1];
            var cur = new int[m + 1];
            for (int j = 0; j <= m; j++) prev[j] = j;
            for (int i = 1; i <= n; i++)
            {
                cur[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = System.Math.Min(System.Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }
                var tmp = prev; prev = cur; cur = tmp;
            }
            return prev[m];
        }
    }
}
