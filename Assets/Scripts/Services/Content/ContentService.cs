using System.Collections.Generic;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>Holds loaded poems + questions and provides lookups / filtered pools.</summary>
    public sealed class ContentService
    {
        private readonly List<Poem> _poems;
        private readonly List<Question> _questions;
        private readonly List<WordClozeQuestion> _wordCloze;
        private readonly Dictionary<string, Poem> _byId = new Dictionary<string, Poem>();
        private readonly Dictionary<string, Question> _byQId = new Dictionary<string, Question>();
        private readonly Dictionary<string, WordClozeQuestion> _byWcId = new Dictionary<string, WordClozeQuestion>();

        // Optional SQL query backend (content.db). When set, pool FILTERING (dynasty/体裁/难度/挖空数)
        // runs as indexed SQL; object materialization + distractor ranking still happen here in C#.
        // Null in unit tests / the in-memory path, which keeps the original full-scan behavior.
        private readonly SqliteContentDb _db;

        // v2 干扰项簇: id → fine cluster (同字数/同韵组/同平仄型); (字数|韵组) → union bucket for widening.
        private readonly Dictionary<int, LineCluster> _clusterById = new Dictionary<int, LineCluster>();
        private readonly Dictionary<string, List<QuestionOption>> _looseBucket = new Dictionary<string, List<QuestionOption>>();

        // Runtime distractor pool window: QuizService.Prepare samples 3 of these per attempt.
        private const int DistractorPoolCap = 20;

        public ContentService(IReadOnlyList<Poem> poems, IReadOnlyList<Question> questions,
            IReadOnlyList<WordClozeQuestion> wordCloze = null, IReadOnlyList<LineCluster> clusters = null,
            SqliteContentDb db = null)
        {
            _db = db;
            _poems = new List<Poem>(poems ?? new List<Poem>());
            _questions = new List<Question>(questions ?? new List<Question>());
            _wordCloze = new List<WordClozeQuestion>(wordCloze ?? new List<WordClozeQuestion>());
            foreach (var p in _poems)
                if (!string.IsNullOrEmpty(p.Id)) _byId[p.Id] = p;
            foreach (var q in _questions) _byQId[q.Id] = q;
            foreach (var w in _wordCloze) _byWcId[w.Id] = w;

            if (clusters != null)
                foreach (var c in clusters)
                {
                    if (c == null) continue;
                    _clusterById[c.Id] = c;
                    string lkey = c.CharCount + "|" + c.RhymeGroup;
                    if (!_looseBucket.TryGetValue(lkey, out var bucket)) { bucket = new List<QuestionOption>(); _looseBucket[lkey] = bucket; }
                    if (c.Lines != null) bucket.AddRange(c.Lines);
                }
        }

        public static async Task<ContentService> LoadAsync(IContentSource source, SqliteContentDb db = null)
        {
            var poems = await source.LoadPoemsAsync();
            var bank = await source.LoadQuestionBankAsync();
            var wordCloze = await source.LoadWordClozeQuestionsAsync();
            return new ContentService(poems, bank?.Questions, wordCloze, bank?.Clusters, db);
        }

        public IReadOnlyList<Poem> Poems => _poems;
        public IReadOnlyList<Question> Questions => _questions;
        public int QuestionCount => _questions.Count;
        public IReadOnlyList<WordClozeQuestion> WordClozeQuestions => _wordCloze;
        public int WordClozeCount => _wordCloze.Count;

        public Poem GetPoem(string id) =>
            !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var p) ? p : null;

        // Canonical chronological order of dynasties, earliest first. Anything not listed
        // sorts after these, keeping its first-seen position.
        private static readonly string[] DynastyOrder =
        {
            "先秦", "秦", "汉", "魏", "晋", "魏晋", "南北朝", "隋", "唐",
            "五代", "宋", "辽", "金", "元", "明", "清", "近现代", "现代", "当代",
        };

        private static int DynastyRank(string d)
        {
            int i = System.Array.IndexOf(DynastyOrder, d);
            return i < 0 ? int.MaxValue : i;
        }

        /// <summary>Distinct dynasties present, chronologically ordered, for the main-menu filter.</summary>
        public List<string> GetDynasties()
        {
            var set = new HashSet<string>();
            var firstSeen = new List<string>();
            foreach (var p in _poems)
                if (!string.IsNullOrEmpty(p.Dynasty) && set.Add(p.Dynasty)) firstSeen.Add(p.Dynasty);
            var list = new List<string>(firstSeen);
            list.Sort((a, b) =>
            {
                int ra = DynastyRank(a), rb = DynastyRank(b);
                // Tiebreak on original first-seen order so unranked dynasties stay stable.
                return ra != rb ? ra.CompareTo(rb) : firstSeen.IndexOf(a).CompareTo(firstSeen.IndexOf(b));
            });
            return list;
        }

        /// <summary>Distinct 体裁 (诗/词/曲) present, for the type filter.</summary>
        public List<string> GetTypes()
        {
            var set = new HashSet<string>();
            var list = new List<string>();
            foreach (var p in _poems)
                if (!string.IsNullOrEmpty(p.Type) && set.Add(p.Type)) list.Add(p.Type);
            return list;
        }

        /// <summary>
        /// Per-line difficulty tiers present among the question bank, ascending. A question's
        /// difficulty is that of its blanked line (poem tier + 名句), per <see cref="DifficultyRules"/>.
        /// </summary>
        public List<int> GetDifficultyTiers()
        {
            if (_db != null) return _db.QuestionDifficultyTiers();
            var set = new HashSet<int>();
            foreach (var q in _questions)
            {
                var p = GetPoem(q.PoemId);
                if (p != null) set.Add(DifficultyRules.LineDifficulty(p, q.BlankLineIndex));
            }
            var list = new List<int>(set);
            list.Sort();
            return list;
        }

        /// <summary>Questions matching the settings' difficulty + dynasty filters.</summary>
        public List<Question> GetPool(ChallengeSettings settings)
        {
            if (_db != null)
            {
                var ids = _db.QuestionPoolIds(settings);
                var dbPool = new List<Question>(ids.Count);
                foreach (var id in ids) if (_byQId.TryGetValue(id, out var q)) dbPool.Add(q);
                return dbPool;
            }
            var pool = new List<Question>();
            foreach (var q in _questions)
                if (Matches(q, settings)) pool.Add(q);
            return pool;
        }

        /// <summary>Pool size without allocating the list (for live count in the config screen).</summary>
        public int CountPool(ChallengeSettings settings)
        {
            if (_db != null) return _db.CountQuestionPool(settings);
            int n = 0;
            foreach (var q in _questions)
                if (Matches(q, settings)) n++;
            return n;
        }

        private bool Matches(Question q, ChallengeSettings settings)
        {
            var poem = GetPoem(q.PoemId);
            if (poem == null) return false;
            return DynastyOk(poem, settings) && TypeOk(poem, settings)
                   && LineDifficultyOk(poem, q.BlankLineIndex, settings);
        }

        private static bool DynastyOk(Poem p, ChallengeSettings s) =>
            s == null || s.Dynasties == null || s.Dynasties.Count == 0 || s.Dynasties.Contains(p.Dynasty);
        private static bool TypeOk(Poem p, ChallengeSettings s) =>
            s == null || s.Types == null || s.Types.Count == 0 || s.Types.Contains(p.Type);
        // Difficulty now keys off the blanked line, not the poem tier ("以诗句为准").
        private static bool LineDifficultyOk(Poem p, int lineIndex, ChallengeSettings s) =>
            s == null || s.Difficulties == null || s.Difficulties.Count == 0
            || s.Difficulties.Contains(DifficultyRules.LineDifficulty(p, lineIndex));

        /// <summary>
        /// Select runtime quiz candidates from the loaded v2 bank. Difficulty is judged per-line
        /// ("以诗句为准"): a blanked line qualifies when its line-difficulty is in the selected set.
        /// Each chosen question gets its transient distractor pool filled from its 簇 (preferring the
        /// same 平仄型, widening to the 韵组 bucket when short), restricted to distractor poems whose
        /// average difficulty is &lt;= the max selected tier. Returns up to ~2×limit candidates
        /// (one per poem) for the session builder.
        /// </summary>
        public List<Question> BuildRuntimeQuestions(ChallengeSettings settings, IRandomSource rng, int limit)
        {
            rng = rng ?? new SystemRandomSource();
            int maxTier = MaxTier(settings);

            // Candidate questions = pool passing dynasty/type/per-line difficulty filters (indexed SQL
            // when a DB backend is present), grouped per poem so we keep at most one per poem.
            var byPoem = new Dictionary<string, List<Question>>();
            foreach (var q in GetPool(settings))
            {
                if (!byPoem.TryGetValue(q.PoemId, out var list)) { list = new List<Question>(); byPoem[q.PoemId] = list; }
                list.Add(q);
            }

            var poemIds = new List<string>(byPoem.Keys);
            ShuffleUtil.ShuffleInPlace(poemIds, rng);

            var result = new List<Question>();
            int want = limit > 0 ? limit * 2 : int.MaxValue;
            foreach (var pid in poemIds)
            {
                if (result.Count >= want) break;
                var list = byPoem[pid];
                ShuffleUtil.ShuffleInPlace(list, rng);
                foreach (var q in list)
                {
                    PopulateDistractors(q, maxTier, rng);
                    if (q.Distractors.Count >= QuestionGenerator.MinDistractors) { result.Add(q); break; } // one per poem
                }
            }
            return result;
        }

        private static int MaxTier(ChallengeSettings settings)
        {
            if (settings == null || settings.Difficulties == null || settings.Difficulties.Count == 0) return int.MaxValue;
            int max = int.MinValue;
            foreach (var t in settings.Difficulties) if (t > max) max = t;
            return max;
        }

        /// <summary>
        /// Fill <paramref name="q"/>.Distractors from its 簇: candidates from the same 平仄型 cluster
        /// first (其字面与正确句不同、来自他诗、非近似、所属诗均难度 &lt;= <paramref name="maxTier"/>);
        /// only widen to the whole 韵组 bucket if the cluster can't supply 3. Same-平水韵部 ranked first,
        /// then capped to a window that <see cref="QuizService.Prepare"/> samples 3 from.
        /// </summary>
        public void PopulateDistractors(Question q, int maxTier, IRandomSource rng)
        {
            rng = rng ?? new SystemRandomSource();
            q.Distractors = new List<QuestionOption>();
            if (q?.Correct == null) return;
            var correct = q.Correct;

            var same = new List<QuestionOption>();   // same 平水韵部 as correct
            var other = new List<QuestionOption>();
            var seen = new HashSet<string> { correct.Text };

            void Collect(IEnumerable<QuestionOption> src)
            {
                if (src == null) return;
                foreach (var o in src)
                {
                    if (o == null || o.SourcePoemId == q.PoemId) continue;
                    if (!seen.Add(o.Text)) continue;
                    if (QuestionGenerator.NearDuplicate(o.Text, correct.Text)) continue;
                    if (!DistractorDifficultyOk(o, maxTier)) continue;
                    if (!string.IsNullOrEmpty(correct.Pingshui) && o.Pingshui == correct.Pingshui) same.Add(o);
                    else other.Add(o);
                }
            }

            if (_clusterById.TryGetValue(q.ClusterId, out var cluster)) Collect(cluster.Lines);
            if (same.Count + other.Count < QuestionGenerator.MinDistractors
                && _looseBucket.TryGetValue(correct.CharCount + "|" + correct.RhymeGroup, out var widen))
                Collect(widen);

            ShuffleUtil.ShuffleInPlace(same, rng);
            ShuffleUtil.ShuffleInPlace(other, rng);
            var pool = q.Distractors;
            foreach (var o in same) { if (pool.Count >= DistractorPoolCap) break; pool.Add(o); }
            foreach (var o in other) { if (pool.Count >= DistractorPoolCap) break; pool.Add(o); }
        }

        // A distractor's source poem must be no harder than the selected max tier (matches v1 corpus filter).
        private bool DistractorDifficultyOk(QuestionOption o, int maxTier)
        {
            if (maxTier == int.MaxValue) return true;
            var p = GetPoem(o.SourcePoemId);
            return p == null || DifficultyRules.AvgDifficulty(p) <= maxTier;
        }

        /// <summary>Apply per-poem difficulty overrides (from local user data) onto loaded poems.</summary>
        public void ApplyDifficultyOverrides(IReadOnlyDictionary<string, int> overrides)
        {
            if (overrides == null) return;
            foreach (var kv in overrides)
                if (_byId.TryGetValue(kv.Key, out var p)) p.Difficulty = kv.Value;
            _db?.SyncOverrides(overrides); // keep SQL line-difficulty consistent with in-memory tiers
        }

        /// <summary>Set one poem's difficulty in memory (caller persists the override).</summary>
        public void SetDifficulty(string poemId, int tier)
        {
            if (!string.IsNullOrEmpty(poemId) && _byId.TryGetValue(poemId, out var p)) p.Difficulty = tier;
            if (!string.IsNullOrEmpty(poemId)) _db?.SetOverride(poemId, tier);
        }

        /// <summary>Look up specific questions by id (used by 错题本 review sessions), with distractor
        /// pools filled from each question's 簇 (no difficulty restriction for review).</summary>
        public List<Question> GetQuestionsByIds(IEnumerable<string> ids)
        {
            var byQId = new Dictionary<string, Question>();
            foreach (var q in _questions) byQId[q.Id] = q;
            var rng = new SystemRandomSource();
            var result = new List<Question>();
            foreach (var id in ids)
                if (byQId.TryGetValue(id, out var q))
                {
                    PopulateDistractors(q, int.MaxValue, rng);
                    result.Add(q);
                }
            return result;
        }

        // ---- 逐词填空 (wordcloze) pools — same dynasty/type/difficulty filters as the line questions ----

        /// <summary>Per-line difficulty tiers present among the wordcloze bank, ascending.</summary>
        public List<int> GetWordClozeDifficultyTiers()
        {
            if (_db != null) return _db.WordClozeDifficultyTiers();
            var set = new HashSet<int>();
            foreach (var q in _wordCloze) set.Add(q.Difficulty);
            var list = new List<int>(set);
            list.Sort();
            return list;
        }

        /// <summary>Distinct 挖空数 (total blanks per question) present in the wordcloze bank, ascending.</summary>
        public List<int> GetWordClozeBlankCounts()
        {
            if (_db != null) return _db.WordClozeBlankCounts();
            var set = new HashSet<int>();
            foreach (var q in _wordCloze) set.Add(q.Blanks != null ? q.Blanks.Count : 0);
            var list = new List<int>(set);
            list.Sort();
            return list;
        }

        /// <summary>Wordcloze questions matching the settings' filters and (optionally) a 挖空数 set.</summary>
        public List<WordClozeQuestion> GetWordClozePool(ChallengeSettings settings, ICollection<int> blankCounts = null)
        {
            if (_db != null)
            {
                var ids = _db.WordClozePoolIds(settings, blankCounts);
                var dbPool = new List<WordClozeQuestion>(ids.Count);
                foreach (var id in ids) if (_byWcId.TryGetValue(id, out var q)) dbPool.Add(q);
                return dbPool;
            }
            var pool = new List<WordClozeQuestion>();
            foreach (var q in _wordCloze)
                if (WordClozeMatches(q, settings, blankCounts)) pool.Add(q);
            return pool;
        }

        public int CountWordClozePool(ChallengeSettings settings, ICollection<int> blankCounts = null)
        {
            if (_db != null) return _db.CountWordClozePool(settings, blankCounts);
            int n = 0;
            foreach (var q in _wordCloze)
                if (WordClozeMatches(q, settings, blankCounts)) n++;
            return n;
        }

        private bool WordClozeMatches(WordClozeQuestion q, ChallengeSettings settings, ICollection<int> blankCounts)
        {
            var poem = GetPoem(q.PoemId);
            if (poem == null) return false;
            if (!DynastyOk(poem, settings) || !TypeOk(poem, settings)) return false;
            if (settings != null && settings.Difficulties != null && settings.Difficulties.Count > 0
                && !settings.Difficulties.Contains(q.Difficulty)) return false;
            if (blankCounts != null && blankCounts.Count > 0
                && !blankCounts.Contains(q.Blanks != null ? q.Blanks.Count : 0)) return false;
            return true;
        }

        /// <summary>Look up specific wordcloze questions by id (used by 错题本 review sessions).</summary>
        public List<WordClozeQuestion> GetWordClozeByIds(IEnumerable<string> ids)
        {
            var byId = new Dictionary<string, WordClozeQuestion>();
            foreach (var q in _wordCloze) byId[q.Id] = q;
            var result = new List<WordClozeQuestion>();
            foreach (var id in ids)
                if (byId.TryGetValue(id, out var q)) result.Add(q);
            return result;
        }
    }
}
