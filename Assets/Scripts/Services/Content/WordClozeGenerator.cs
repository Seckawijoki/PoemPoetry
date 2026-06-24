using System.Collections.Generic;
using System.Globalization;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Generates 逐词填空 (残句调控) questions: blanks one or more 名词/动词 keywords in a line and
    /// builds a single-character tile pool of the answer chars plus distractor chars chosen to be
    /// 同语义类型 + 平仄相当 (falling back layer-by-layer when a category/tone runs dry).
    ///
    /// A word is "blankable" iff its surface form is in the word bank (so 名词/动词 detection is the
    /// bank's responsibility — populated offline by jieba / hand-review). Distractor characters come
    /// from semantic_categories.json (per-char 类型) cross 平仄 (via <see cref="ToneService"/>).
    /// Pure, UnityEngine-free, deterministic given a seeded <see cref="IRandomSource"/>.
    /// </summary>
    public sealed class WordClozeGenerator
    {
        // Category resolution priority when a char belongs to several lists.
        private static readonly string[] CategoryPriority = { "颜色", "动物", "植物", "方位", "数字", "时间", "天文", "地理" };
        private const string General = "通用";

        private readonly IRandomSource _rng;
        private readonly ToneService _tone;

        private readonly Dictionary<string, WordEntry> _wordByText = new Dictionary<string, WordEntry>();
        private readonly List<string> _wordsByLenDesc = new List<string>();
        private int _maxWordLen = 1;

        private readonly Dictionary<string, string> _charCategory = new Dictionary<string, string>();
        private readonly Dictionary<string, List<string>> _catToneChars = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _posToneChars = new Dictionary<string, List<string>>();
        private readonly Dictionary<string, List<string>> _corpusToneChars = new Dictionary<string, List<string>>();
        private readonly List<string> _corpusChars = new List<string>();

        public WordClozeGenerator(
            IEnumerable<Poem> poems,
            IEnumerable<WordEntry> wordBank,
            IReadOnlyDictionary<string, List<string>> semanticCategories,
            ToneService tone,
            IRandomSource rng)
        {
            _tone = tone ?? new ToneService(null);
            _rng = rng ?? new SystemRandomSource();

            // Word bank: lookup by surface form + a longest-first scan order.
            if (wordBank != null)
                foreach (var w in wordBank)
                {
                    if (w == null || string.IsNullOrEmpty(w.Text) || _wordByText.ContainsKey(w.Text)) continue;
                    _wordByText[w.Text] = w;
                    _wordsByLenDesc.Add(w.Text);
                    _maxWordLen = System.Math.Max(_maxWordLen, ElementCount(w.Text));
                    // POS-tagged char pools (decompose each word's chars), keyed by pos|tone.
                    foreach (var ch in Chars(w.Text))
                        Add(_posToneChars, w.Pos + "|" + _tone.ToneOf(ch), ch);
                }
            _wordsByLenDesc.Sort((a, b) => ElementCount(b).CompareTo(ElementCount(a)));

            // Semantic categories: char → category (by priority) and (category|tone) → chars.
            if (semanticCategories != null)
                foreach (var cat in CategoryPriority)
                {
                    if (!semanticCategories.TryGetValue(cat, out var chars) || chars == null) continue;
                    foreach (var ch in chars)
                    {
                        if (string.IsNullOrEmpty(ch)) continue;
                        if (!_charCategory.ContainsKey(ch)) _charCategory[ch] = cat; // first (highest-priority) wins
                        Add(_catToneChars, cat + "|" + _tone.ToneOf(ch), ch);
                    }
                }

            // Corpus char pool (fallback distractor source), deduped, keyed by tone.
            var seen = new HashSet<string>();
            if (poems != null)
                foreach (var p in poems)
                {
                    if (p?.Lines == null) continue;
                    foreach (var line in p.Lines)
                        foreach (var ch in Chars(RhymeService.StripPunct(line.Text)))
                            if (seen.Add(ch))
                            {
                                _corpusChars.Add(ch);
                                Add(_corpusToneChars, _tone.ToneOf(ch), ch);
                            }
                }
        }

        /// <summary>
        /// Build one question for a line; null if no blankable 2~3-char word is present.
        /// Each answer character is one "unit" and gets its own <paramref name="perCharDistractors"/>
        /// same-类型 + 平仄相当 distractors (so a 2-字 word's 1+1 split, or a 3-字 word's 1+1+1 split,
        /// each part is independently confusable). The tile pool is padded to an even size so it tiles
        /// cleanly into a 2×N grid.
        /// </summary>
        public WordClozeQuestion Generate(Poem poem, int lineIndex, int maxBlanks, int perCharDistractors)
        {
            if (poem?.Lines == null || lineIndex < 0 || lineIndex >= poem.Lines.Count) return null;
            var text = poem.Lines[lineIndex].Text;
            var elems = Chars(text);
            if (elems.Count == 0) return null;

            // Greedy left-to-right longest-match scan for non-overlapping word occurrences.
            var found = new List<WordClozeBlank>();
            for (int i = 0; i < elems.Count;)
            {
                string matched = null; int matchedLen = 0;
                for (int len = System.Math.Min(_maxWordLen, elems.Count - i); len >= 1; len--)
                {
                    var sub = Join(elems, i, len);
                    if (_wordByText.ContainsKey(sub)) { matched = sub; matchedLen = len; break; }
                }
                if (matched != null)
                {
                    var entry = _wordByText[matched];
                    found.Add(new WordClozeBlank
                    {
                        Start = i,
                        Count = matchedLen,
                        AnswerChars = Chars(matched),
                        Pos = entry.Pos,
                        Semantic = string.IsNullOrEmpty(entry.Semantic) ? General : entry.Semantic,
                    });
                    i += matchedLen;
                }
                else i++;
            }
            // Only blank 2~3-character words (single chars are too easy / ambiguous).
            found.RemoveAll(b => b.Count < 2 || b.Count > 3);
            if (found.Count == 0) return null;

            // Rank candidates: 非通用语义 > 名词 > 更长的词; keep top maxBlanks, then order by Start.
            found.Sort((a, b) =>
            {
                int sa = a.Semantic != General ? 0 : 1, sb = b.Semantic != General ? 0 : 1;
                if (sa != sb) return sa - sb;
                int pa = a.Pos == "n" ? 0 : 1, pb = b.Pos == "n" ? 0 : 1;
                if (pa != pb) return pa - pb;
                return b.Count - a.Count;
            });
            int take = System.Math.Min(System.Math.Max(1, maxBlanks), found.Count);
            var blanks = found.GetRange(0, take);
            blanks.Sort((a, b) => a.Start - b.Start);

            // Tile pool = answer chars + distractors (one share per answer char). Pad to even so the
            // pool fills a 2×N grid with no ragged hole.
            var answerChars = new List<string>();
            foreach (var b in blanks) answerChars.AddRange(b.AnswerChars);
            int distractorCount = answerChars.Count * System.Math.Max(1, perCharDistractors);
            if ((answerChars.Count + distractorCount) % 2 != 0) distractorCount++;
            var distractors = BuildDistractors(blanks, answerChars, distractorCount);

            var pool = new List<string>(answerChars);
            pool.AddRange(distractors);
            ShuffleUtil.ShuffleInPlace(pool, _rng);

            return new WordClozeQuestion
            {
                Id = "wc-" + poem.Id + "-" + lineIndex,
                PoemId = poem.Id,
                BlankLineIndex = lineIndex,
                Blanks = blanks,
                TilePool = pool,
                Difficulty = DifficultyRules.LineDifficulty(poem, lineIndex),
            };
        }

        /// <summary>Generate one question per viable line of a poem; difficulty drives blank/tile counts.</summary>
        public List<WordClozeQuestion> GenerateForPoem(Poem poem)
        {
            var list = new List<WordClozeQuestion>();
            if (poem?.Lines == null) return list;
            for (int i = 0; i < poem.Lines.Count; i++)
            {
                int diff = DifficultyRules.LineDifficulty(poem, i);
                // Blanks are 2~3 chars each, so 1 blank is usually enough; harder lines may get 2.
                int maxBlanks = diff <= 1 ? 1 : 2;
                int perChar = 4 + (diff >= 2 ? 1 : 0); // 稍多: ~4-5 distractors per answer char
                var q = Generate(poem, i, maxBlanks, perChar);
                if (q != null) list.Add(q);
            }
            return list;
        }

        // Round-robin one distractor per answer char through its layered candidate queue.
        private List<string> BuildDistractors(List<WordClozeBlank> blanks, List<string> answerChars, int count)
        {
            var used = new HashSet<string>(answerChars);
            var distractors = new List<string>();
            if (count <= 0) return distractors;

            // One candidate queue per answer char (paired with its blank's pos), in slot order.
            var queues = new List<List<string>>();
            foreach (var b in blanks)
                foreach (var ch in b.AnswerChars)
                    queues.Add(CandidateQueue(ch, b.Pos));

            bool progressed = true;
            while (distractors.Count < count && progressed)
            {
                progressed = false;
                foreach (var q in queues)
                {
                    while (q.Count > 0)
                    {
                        var c = q[q.Count - 1];
                        q.RemoveAt(q.Count - 1);
                        if (used.Add(c)) { distractors.Add(c); progressed = true; break; }
                    }
                    if (distractors.Count >= count) break;
                }
            }
            return distractors;
        }

        // Layered distractor candidates for one answer char, deduped & excluding the char itself.
        // Layers: 同类同平仄 → 同类(任平仄) → 同词性同平仄 → 同词性 → 同平仄(全语料) → 任意.
        private List<string> CandidateQueue(string answerChar, string pos)
        {
            string tone = _tone.ToneOf(answerChar);
            string cat = _charCategory.TryGetValue(answerChar, out var c) ? c : General;
            string otherTone = tone == ToneService.Ping ? ToneService.Ze : ToneService.Ping;

            var layers = new List<List<string>>();
            if (cat != General)
            {
                layers.Add(Get(_catToneChars, cat + "|" + tone));
                layers.Add(Get(_catToneChars, cat + "|" + otherTone));
            }
            layers.Add(Get(_posToneChars, pos + "|" + tone));
            layers.Add(Get(_posToneChars, pos + "|" + otherTone));
            layers.Add(Get(_corpusToneChars, tone));
            layers.Add(_corpusChars);

            // Flatten in layer order, shuffling within each layer; dedup; drop the answer char.
            // Stored reversed so callers can pop cheaply from the tail (preserves layer order).
            var ordered = new List<string>();
            var seen = new HashSet<string> { answerChar };
            foreach (var layer in layers)
            {
                var shuffled = new List<string>(layer);
                ShuffleUtil.ShuffleInPlace(shuffled, _rng);
                foreach (var ch in shuffled)
                    if (seen.Add(ch)) ordered.Add(ch);
            }
            ordered.Reverse();
            return ordered;
        }

        private static void Add(Dictionary<string, List<string>> map, string key, string val)
        {
            if (!map.TryGetValue(key, out var list)) { list = new List<string>(); map[key] = list; }
            list.Add(val);
        }

        private static List<string> Get(Dictionary<string, List<string>> map, string key) =>
            map.TryGetValue(key, out var list) ? list : EmptyList;

        private static readonly List<string> EmptyList = new List<string>();

        private static int ElementCount(string s) => new StringInfo(s).LengthInTextElements;

        private static List<string> Chars(string s)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(s)) return r;
            var si = new StringInfo(s);
            int n = si.LengthInTextElements;
            for (int i = 0; i < n; i++) r.Add(si.SubstringByTextElements(i, 1));
            return r;
        }

        private static string Join(List<string> elems, int start, int len)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < len; i++) sb.Append(elems[start + i]);
            return sb.ToString();
        }
    }
}
