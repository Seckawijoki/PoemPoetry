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
        /// <summary>A question needs at least this many distractors to be usable (else it's skipped).</summary>
        public const int MinDistractors = 3;

        /// <summary>Upper bound on the distractor pool stored per question; presentation samples 3 of these.</summary>
        public const int MaxDistractorPool = 20;

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

        // ───────────────────────── schema v2: shared 干扰项簇 ─────────────────────────

        /// <summary>
        /// Build the v2 question bank: cluster every corpus line by (字数, 韵组, 平仄型) into a shared
        /// pool, then emit one lightweight question per blankable line that references its cluster.
        /// Distractors are no longer embedded per-question — runtime resolves them from the cluster
        /// (preferring the same 平仄型, widening to the 韵组 bucket when short). Eliminates the v1
        /// per-question duplication. Deterministic given the seeded rng.
        /// </summary>
        /// <param name="tone">Provides the 平仄型 per line; null collapses each 韵组 to one cluster.</param>
        /// <param name="rhymeLinesOnlyForShi">诗 blanks only 韵脚句; 词/曲 may blank any line.</param>
        /// <param name="minDistractors">A line is questionable only if its 韵组 bucket yields ≥ this many valid distractors.</param>
        public QuestionFile BuildBank(IReadOnlyList<Poem> targets, ToneService tone,
            bool rhymeLinesOnlyForShi = true, int minDistractors = MinDistractors)
        {
            // 1. Fine clusters keyed (字数|韵组|平仄型); each line deduped by text. Loose bucket (字数|韵组)
            //    unions sibling 平仄型 for the viability check + runtime widening.
            var fine = new Dictionary<string, LineCluster>();
            var fineSeen = new Dictionary<string, HashSet<string>>();
            var loose = new Dictionary<string, List<QuestionOption>>();
            var looseSeen = new Dictionary<string, HashSet<string>>();
            foreach (var bucket in _index.Values)
                foreach (var cl in bucket)
                {
                    var line = cl.Line;
                    if (line == null || string.IsNullOrEmpty(line.Text) || string.IsNullOrEmpty(line.RhymeGroup)) continue;
                    string toneType = tone != null ? tone.ToneType(line.Text) : "";
                    string fkey = line.CharCount + "|" + line.RhymeGroup + "|" + toneType;
                    if (!fine.TryGetValue(fkey, out var cluster))
                    {
                        cluster = new LineCluster { CharCount = line.CharCount, RhymeGroup = line.RhymeGroup, ToneType = toneType };
                        fine[fkey] = cluster; fineSeen[fkey] = new HashSet<string>();
                    }
                    if (fineSeen[fkey].Add(line.Text))
                        cluster.Lines.Add(QuestionOption.FromLine(line, cl.PoemId));

                    string lkey = line.CharCount + "|" + line.RhymeGroup;
                    if (!loose.TryGetValue(lkey, out var llist)) { llist = new List<QuestionOption>(); loose[lkey] = llist; looseSeen[lkey] = new HashSet<string>(); }
                    if (looseSeen[lkey].Add(line.Text)) llist.Add(QuestionOption.FromLine(line, cl.PoemId));
                }

            // 2. Assign deterministic cluster ids (sorted by key) and index by fine key.
            var orderedKeys = new List<string>(fine.Keys);
            orderedKeys.Sort(System.StringComparer.Ordinal);
            var clusters = new List<LineCluster>(orderedKeys.Count);
            var idByKey = new Dictionary<string, int>();
            for (int i = 0; i < orderedKeys.Count; i++)
            {
                var c = fine[orderedKeys[i]];
                c.Id = i;
                idByKey[orderedKeys[i]] = i;
                clusters.Add(c);
            }

            // 3. Emit one lightweight question per blankable, viable line.
            var questions = new List<Question>();
            foreach (var poem in targets)
            {
                if (poem?.Lines == null) continue;
                bool shi = poem.Type == "诗";
                for (int i = 0; i < poem.Lines.Count; i++)
                {
                    var target = poem.Lines[i];
                    if (shi && rhymeLinesOnlyForShi && !target.IsRhymeLine) continue;
                    if (target == null || string.IsNullOrEmpty(target.RhymeGroup)) continue;

                    string lkey = target.CharCount + "|" + target.RhymeGroup;
                    if (!loose.TryGetValue(lkey, out var pool)) continue;
                    if (CountValidDistractors(pool, poem.Id, target.Text) < minDistractors) continue;

                    string toneType = tone != null ? tone.ToneType(target.Text) : "";
                    string fkey = target.CharCount + "|" + target.RhymeGroup + "|" + toneType;
                    questions.Add(new Question
                    {
                        Id = "q-" + poem.Id + "-" + i,
                        PoemId = poem.Id,
                        BlankLineIndex = i,
                        ClusterId = idByKey.TryGetValue(fkey, out var cid) ? cid : -1,
                        Correct = QuestionOption.FromLine(target, poem.Id),
                        Difficulty = DifficultyFor(poem, target),
                        Explanation = "",
                        SourceMode = "corpus",
                    });
                }
            }
            return new QuestionFile { SchemaVersion = 2, Clusters = clusters, Questions = questions };
        }

        // Count lines usable as distractors for a target: different poem, distinct, not a near-dup.
        private static int CountValidDistractors(List<QuestionOption> pool, string poemId, string correctText)
        {
            int n = 0;
            foreach (var o in pool)
                if (o.SourcePoemId != poemId && o.Text != correctText && Levenshtein(o.Text, correctText) > 1) n++;
            return n;
        }

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

        /// <summary>
        /// Build one question with a distractor POOL: as many as the algorithm can find, capped at
        /// <paramref name="maxDistractors"/>. Returns null if the blank is unsuitable or fewer than
        /// <paramref name="minDistractors"/> distractors exist. The quiz samples 3 of the pool per attempt.
        /// </summary>
        public Question Generate(Poem poem, int blankLineIndex,
            int minDistractors = MinDistractors, int maxDistractors = MaxDistractorPool)
        {
            if (poem == null || poem.Lines == null) return null;
            if (blankLineIndex < 0 || blankLineIndex >= poem.Lines.Count) return null;

            var target = poem.Lines[blankLineIndex];
            if (string.IsNullOrEmpty(target.RhymeGroup)) return null; // rhyme uncomputable -> skip

            var distractors = SelectDistractors(poem.Id, target, maxDistractors, poem.Type, poem.Cipai);
            if (distractors.Count < minDistractors) return null;

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

        /// <summary>Generate questions (each with a 3..<paramref name="maxDistractors"/> distractor pool) for every viable blank line of a poem.</summary>
        public List<Question> GenerateForPoem(Poem poem,
            int minDistractors = MinDistractors, int maxDistractors = MaxDistractorPool, bool rhymeLinesOnly = false)
        {
            var list = new List<Question>();
            if (poem == null || poem.Lines == null) return list;
            for (int i = 0; i < poem.Lines.Count; i++)
            {
                if (rhymeLinesOnly && !poem.Lines[i].IsRhymeLine) continue;
                var q = Generate(poem, i, minDistractors, maxDistractors);
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

        /// <summary>A line is too close to use as a distractor if it equals or is within edit-distance 1.</summary>
        public static bool NearDuplicate(string a, string b) => a == b || Levenshtein(a, b) <= 1;

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
