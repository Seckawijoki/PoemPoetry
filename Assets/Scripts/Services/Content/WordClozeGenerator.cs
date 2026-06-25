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

        // Leading characters that make a 2-char noun read as 形容字+名词 (名词词组). 颜色 chars are
        // added from semantic_categories at construction; this is the non-color adjective seed.
        private static readonly string[] AdjSeed =
        {
            "明", "孤", "寒", "清", "远", "高", "深", "新", "空", "幽", "残", "斜", "轻", "细",
            "暗", "曲", "长", "古", "野", "晴", "暖", "冷", "落", "归", "故", "秋", "春",
        };

        private readonly IRandomSource _rng;
        private readonly ToneService _tone;

        private readonly Dictionary<string, WordEntry> _wordByText = new Dictionary<string, WordEntry>();
        private readonly List<string> _wordsByLenDesc = new List<string>();
        private int _maxWordLen = 1;

        private readonly Dictionary<string, string> _charCategory = new Dictionary<string, string>();
        private readonly HashSet<string> _adjFirst = new HashSet<string>();
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

            // Adjective-ish leading chars (颜色 + curated seed) → 形容字+名词 detection.
            foreach (var a in AdjSeed) _adjFirst.Add(a);
            if (semanticCategories != null && semanticCategories.TryGetValue("颜色", out var colorChars) && colorChars != null)
                foreach (var ch in colorChars) if (!string.IsNullOrEmpty(ch)) _adjFirst.Add(ch);

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
        /// Generate a varied bank for a poem. Shapes (each tagged by total 空 count so the config
        /// screen's 挖空数 selector can filter):
        ///   • 1 空 — one keyword in a 句 (2~3 字 word, or a single 动词/介词 char; never a 单字名词).
        ///   • 2 空 同句 — two keywords in one 句.
        ///   • 2 空 一联 — two 等长同 group 句 (对仗/对偶 ≈ 句号组 + 字数); same 字位 preferred (炼字对照).
        ///   • 3 空 — 词/曲 only (默认跳过诗): two 邻 group forming 1+2 / 2+1 (单句 + 一联), one keyword each.
        ///   • 4 空 — four consecutive 句 (两邻 group 合并), one keyword each.
        /// Distractors are 4 per answer char; the tile pool is padded with random chars to a full grid.
        /// </summary>
        public List<WordClozeQuestion> GenerateForPoem(Poem poem)
        {
            var list = new List<WordClozeQuestion>();
            if (poem?.Lines == null) return list;
            int n = poem.Lines.Count;
            var ids = new HashSet<string>();

            var lineBlanks = new List<List<WordClozeBlank>>();
            for (int i = 0; i < n; i++)
            {
                var f = FindBlanks(Chars(poem.Lines[i].Text));
                foreach (var b in f) b.LineIndex = i;
                lineBlanks.Add(f);
            }

            void TryAdd(WordClozeQuestion q) { if (q != null && ids.Add(q.Id)) list.Add(q); }
            int PerChar() => 4;   // 固定每字 4 个干扰

            // 1 空: best keyword per 句, plus a distinct single 动词/介词 char when present.
            for (int i = 0; i < n; i++)
            {
                var ranked = RankAll(lineBlanks[i]);
                if (ranked.Count == 0) continue;
                var top = ranked[0];
                TryAdd(Build(poem, new List<int> { i }, new List<WordClozeBlank> { CloneBlank(top) },
                    PerChar(), $"wc-{poem.Id}-s1-{i}-{top.Start}"));
                foreach (var b in ranked)
                    if (b.Count == 1 && (b.Pos == "v" || b.Pos == "p") && b.Start != top.Start)
                    {
                        TryAdd(Build(poem, new List<int> { i }, new List<WordClozeBlank> { CloneBlank(b) },
                            PerChar(), $"wc-{poem.Id}-s1-{i}-{b.Start}"));
                        break;
                    }
            }

            // 2 空 同句: top-2 POS-coherent keywords in one 句.
            for (int i = 0; i < n; i++)
            {
                var two = PickCoherent(lineBlanks[i], 2);
                if (two.Count < 2) continue;
                TryAdd(Build(poem, new List<int> { i }, CloneAll(two), PerChar(), $"wc-{poem.Id}-l2-{i}"));
            }

            // 2 空 一联: 等长同 group adjacent 句, same 字位 preferred.
            for (int i = 0; i + 1 < n; i++)
                if (SameCouplet(poem, i, i + 1))
                    TryAdd(BuildCouplet(poem, i, i + 1, lineBlanks, PerChar()));

            // 3 空 & 4 空: 句号组对齐的窗口 (连续若干 group 的句数恰好 = 3 / 4)。这样能覆盖词的多种结构
            // (单句+一联 1+2/2+1、三句一组 3、2+2、1+3 …)。3 空默认跳过律诗/绝句 (诗 且 4 或 8 句的近体)，
            // 长诗 / 词 / 曲 只要句号组能凑出 3 句窗口即可出 3 空。BuildMulti 要求窗口内每句都可挖。
            var runs = GroupRuns(poem);
            bool jueLu = poem.Type == "诗" && (n == 4 || n == 8);
            if (!jueLu)
                foreach (var win in GroupWindows(runs, 3))
                    TryAdd(BuildMulti(poem, win, lineBlanks, PerChar()));
            foreach (var win in GroupWindows(runs, 4))
                TryAdd(BuildMulti(poem, win, lineBlanks, PerChar()));
            return list;
        }

        // Windows of consecutive 句号组 whose total 句数 == target (lines are consecutive, ascending).
        // One window per starting group (the shortest span from that group reaching exactly target).
        private static List<List<int>> GroupWindows(List<List<int>> runs, int target)
        {
            var wins = new List<List<int>>();
            for (int s = 0; s < runs.Count; s++)
            {
                int sum = 0;
                var lines = new List<int>();
                for (int e = s; e < runs.Count; e++)
                {
                    sum += runs[e].Count;
                    lines.AddRange(runs[e]);
                    if (sum == target) { wins.Add(lines); break; }
                    if (sum > target) break;
                }
            }
            return wins;
        }

        // Consecutive runs of lines sharing the same 句号 group (each run = one 句号组).
        private static List<List<int>> GroupRuns(Poem poem)
        {
            var runs = new List<List<int>>();
            for (int i = 0; i < poem.Lines.Count; i++)
            {
                int g = DifficultyRules.EffectiveGroup(poem, i);
                if (runs.Count == 0 || DifficultyRules.EffectiveGroup(poem, runs[runs.Count - 1][0]) != g)
                    runs.Add(new List<int> { i });
                else
                    runs[runs.Count - 1].Add(i);
            }
            return runs;
        }

        // Two 等长 (same 字数) 句 in the same 句号 group → 对仗/对偶 candidate (粗判: group + 字数).
        private static bool SameCouplet(Poem poem, int a, int b) =>
            poem.Lines[a].CharCount > 0 &&
            poem.Lines[a].CharCount == poem.Lines[b].CharCount &&
            DifficultyRules.EffectiveGroup(poem, a) == DifficultyRules.EffectiveGroup(poem, b);

        // Couplet question: prefer blanking the SAME 字位 (start+len) in both 句; else best per 句.
        private WordClozeQuestion BuildCouplet(Poem poem, int a, int b, List<List<WordClozeBlank>> lineBlanks, int perChar)
        {
            var ra = RankAll(lineBlanks[a]); var rb = RankAll(lineBlanks[b]);
            if (ra.Count == 0 || rb.Count == 0) return null;

            WordClozeBlank pa = null, pb = null;
            foreach (var x in ra)
            {
                foreach (var y in rb)
                    if (y.Start == x.Start && y.Count == x.Count) { pa = x; pb = y; break; }
                if (pa != null) break;
            }
            if (pa == null) { pa = ra[0]; pb = rb[0]; }
            return Build(poem, new List<int> { a, b }, new List<WordClozeBlank> { CloneBlank(pa), CloneBlank(pb) },
                perChar, $"wc-{poem.Id}-c2-{a}-{b}");
        }

        // Multi-line question: one keyword per 句, biased so 词性 pairs up (每2空尽量同词性). We pick a
        // target 词性 that ≥2 句 can supply (preferring 名词 > 形名 > 动词 > 介词) and use it wherever
        // possible; remaining 句 take their best word. For 4 句 this yields 2+2 or 2+1+1; for 3 句, 2+1.
        private WordClozeQuestion BuildMulti(Poem poem, List<int> lines, List<List<WordClozeBlank>> lineBlanks, int perChar)
        {
            foreach (var li in lines) if (lineBlanks[li].Count == 0) return null;

            var ranked = new List<List<WordClozeBlank>>();
            foreach (var li in lines) ranked.Add(RankAll(lineBlanks[li]));

            // Target 词性: the kind the most 句 can supply (≥2); ties prefer the lower (more wanted) kind.
            int targetKind = -1, targetCount = 1;
            for (int kind = 0; kind <= 3; kind++)
            {
                int c = 0;
                foreach (var r in ranked) if (FirstOfKind(r, kind) != null) c++;
                if (c >= 2 && c > targetCount) { targetCount = c; targetKind = kind; }
            }

            var blanks = new List<WordClozeBlank>();
            foreach (var r in ranked)
            {
                var pick = targetKind >= 0 ? FirstOfKind(r, targetKind) : null;
                blanks.Add(CloneBlank(pick ?? r[0]));
            }
            return Build(poem, lines, blanks, perChar, $"wc-{poem.Id}-m{lines.Count}-{lines[0]}");
        }

        private WordClozeBlank FirstOfKind(List<WordClozeBlank> ranked, int kind)
        {
            foreach (var b in ranked) if (KindRank(b) == kind) return b;
            return null;
        }

        // Assemble a question from chosen blanks: distractors (4~5/char) + random grid padding.
        private WordClozeQuestion Build(Poem poem, List<int> shownLines, List<WordClozeBlank> blanks, int perChar, string id)
        {
            if (blanks == null || blanks.Count == 0) return null;
            blanks.Sort((x, y) => x.LineIndex != y.LineIndex ? x.LineIndex - y.LineIndex : x.Start - y.Start);

            var answerChars = new List<string>();
            foreach (var b in blanks) answerChars.AddRange(b.AnswerChars);
            int distractorCount = answerChars.Count * System.Math.Max(1, perChar);
            var distractors = BuildDistractors(blanks, answerChars, distractorCount);

            var pool = new List<string>(answerChars);
            pool.AddRange(distractors);
            PadToGrid(pool);
            ShuffleUtil.ShuffleInPlace(pool, _rng);

            int maxDiff = 0;
            foreach (var li in shownLines) maxDiff = System.Math.Max(maxDiff, DifficultyRules.LineDifficulty(poem, li));

            return new WordClozeQuestion
            {
                Id = id,
                PoemId = poem.Id,
                BlankLineIndex = shownLines[0],
                LineIndices = shownLines.Count > 1 ? new List<int>(shownLines) : new List<int>(),
                Blanks = blanks,
                TilePool = pool,
                Difficulty = maxDiff,
            };
        }

        // Tile-grid shape: at least 2 rows, at most 8 columns (phone-friendly). Shared by the generator
        // (padding) and WordClozeScreen (layout) so the grid is always a full rectangle — no ragged row.
        public const int MaxGridCols = 8;
        public static int GridRows(int count) => System.Math.Max(2, (count + MaxGridCols - 1) / MaxGridCols);

        // Pad the pool with random corpus chars (duplicates allowed) up to a full rows×cols rectangle.
        private void PadToGrid(List<string> pool)
        {
            int m = pool.Count;
            if (m == 0 || _corpusChars.Count == 0) return;
            int rows = GridRows(m);
            int cols = (m + rows - 1) / rows;
            int target = rows * cols;
            while (pool.Count < target) pool.Add(_corpusChars[_rng.Next(_corpusChars.Count)]);
        }

        // Rank candidates: 形容字+名词 > 名词 > 动词 > 介词; then 非通用语义; then 更长的词.
        private List<WordClozeBlank> RankAll(List<WordClozeBlank> found)
        {
            var copy = new List<WordClozeBlank>(found);
            copy.Sort((a, b) =>
            {
                int ka = KindRank(a), kb = KindRank(b);
                if (ka != kb) return ka - kb;
                int sa = a.Semantic != General ? 0 : 1, sb = b.Semantic != General ? 0 : 1;
                if (sa != sb) return sa - sb;
                if (a.Count != b.Count) return b.Count - a.Count;
                return a.Start - b.Start;
            });
            return copy;
        }

        // Pick up to n blanks favouring POS coherence with the top candidate, non-overlapping by 字位.
        private List<WordClozeBlank> PickCoherent(List<WordClozeBlank> found, int n)
        {
            var ranked = RankAll(found);
            var picked = new List<WordClozeBlank>();
            if (ranked.Count == 0) return picked;
            picked.Add(ranked[0]);
            int wantKind = KindRank(ranked[0]);
            foreach (var b in ranked)
            {
                if (picked.Count >= n) break;
                if (picked.Contains(b) || Overlaps(picked, b)) continue;
                if (KindRank(b) == wantKind) picked.Add(b);
            }
            foreach (var b in ranked)
            {
                if (picked.Count >= n) break;
                if (picked.Contains(b) || Overlaps(picked, b)) continue;
                picked.Add(b);
            }
            return picked;
        }

        private static bool Overlaps(List<WordClozeBlank> picked, WordClozeBlank b)
        {
            foreach (var p in picked)
                if (p.Start < b.Start + b.Count && b.Start < p.Start + p.Count) return true;
            return false;
        }

        // 形容字+名词(0) < 名词(1) < 动词(2) < 介词(3) — lower sorts first (preferred).
        private int KindRank(WordClozeBlank b)
        {
            if (b.Pos == "v") return 2;
            if (b.Pos == "p") return 3;
            if (b.Count >= 2 && b.AnswerChars.Count > 0 && _adjFirst.Contains(b.AnswerChars[0])) return 0;
            return 1;
        }

        private static WordClozeBlank CloneBlank(WordClozeBlank b) => new WordClozeBlank
        {
            LineIndex = b.LineIndex, Start = b.Start, Count = b.Count,
            AnswerChars = new List<string>(b.AnswerChars), Pos = b.Pos, Semantic = b.Semantic,
        };

        private static List<WordClozeBlank> CloneAll(List<WordClozeBlank> src)
        {
            var r = new List<WordClozeBlank>();
            foreach (var b in src) r.Add(CloneBlank(b));
            return r;
        }

        // Greedy left-to-right longest-match scan for non-overlapping 2~3-char blankable words.
        private List<WordClozeBlank> FindBlanks(List<string> elems)
        {
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
            // Blank 2~3-char words (any 词性), or a single 动词/介词 char — never a 单字名词.
            found.RemoveAll(b => !((b.Count >= 2 && b.Count <= 3) || (b.Count == 1 && (b.Pos == "v" || b.Pos == "p"))));
            return found;
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
