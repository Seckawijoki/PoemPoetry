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
        private readonly Dictionary<string, Poem> _byId = new Dictionary<string, Poem>();

        public ContentService(IReadOnlyList<Poem> poems, IReadOnlyList<Question> questions)
        {
            _poems = new List<Poem>(poems ?? new List<Poem>());
            _questions = new List<Question>(questions ?? new List<Question>());
            foreach (var p in _poems)
                if (!string.IsNullOrEmpty(p.Id)) _byId[p.Id] = p;
        }

        public static async Task<ContentService> LoadAsync(IContentSource source)
        {
            var poems = await source.LoadPoemsAsync();
            var questions = await source.LoadQuestionsAsync();
            return new ContentService(poems, questions);
        }

        public IReadOnlyList<Poem> Poems => _poems;
        public IReadOnlyList<Question> Questions => _questions;
        public int QuestionCount => _questions.Count;

        public Poem GetPoem(string id) =>
            !string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var p) ? p : null;

        /// <summary>Distinct dynasties present, for the main-menu filter.</summary>
        public List<string> GetDynasties()
        {
            var set = new HashSet<string>();
            var list = new List<string>();
            foreach (var p in _poems)
                if (!string.IsNullOrEmpty(p.Dynasty) && set.Add(p.Dynasty)) list.Add(p.Dynasty);
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
            var pool = new List<Question>();
            foreach (var q in _questions)
                if (Matches(q, settings)) pool.Add(q);
            return pool;
        }

        /// <summary>Pool size without allocating the list (for live count in the config screen).</summary>
        public int CountPool(ChallengeSettings settings)
        {
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
        /// Generate questions at runtime. Difficulty is judged per-line ("以诗句为准"): a blanked
        /// line qualifies as a target when its line-difficulty is in the selected set; the distractor
        /// corpus is the selected dynasty whose poem-average difficulty is &lt;= the max selected tier.
        /// Returns up to ~2×limit candidate questions (one per poem) for the session builder.
        /// </summary>
        public List<Question> BuildRuntimeQuestions(ChallengeSettings settings, IRandomSource rng, int limit)
        {
            rng = rng ?? new SystemRandomSource();
            int maxTier = int.MaxValue;
            if (settings != null && settings.Difficulties != null && settings.Difficulties.Count > 0)
            {
                maxTier = int.MinValue;
                foreach (var t in settings.Difficulties) if (t > maxTier) maxTier = t;
            }

            // Distractor corpus: selected dynasty + poem-average difficulty <= max selected tier.
            var corpus = new List<Poem>();
            foreach (var p in _poems)
                if (DynastyOk(p, settings) && DifficultyRules.AvgDifficulty(p) <= maxTier) corpus.Add(p);
            var gen = new QuestionGenerator(corpus, rng);

            // Target poems: dynasty + type filter (per-line difficulty checked below).
            var targets = new List<Poem>();
            foreach (var p in _poems)
                if (DynastyOk(p, settings) && TypeOk(p, settings)) targets.Add(p);
            ShuffleUtil.ShuffleInPlace(targets, rng);

            var result = new List<Question>();
            int want = limit > 0 ? limit * 2 : int.MaxValue;
            foreach (var p in targets)
            {
                if (result.Count >= want) break;
                if (p.Lines == null) continue;
                var lineIdx = new List<int>();
                for (int i = 0; i < p.Lines.Count; i++)
                {
                    if (p.Type == "诗" && !p.Lines[i].IsRhymeLine) continue;
                    if (!LineDifficultyOk(p, i, settings)) continue;
                    lineIdx.Add(i);
                }
                ShuffleUtil.ShuffleInPlace(lineIdx, rng);
                foreach (var li in lineIdx)
                {
                    var q = gen.Generate(p, li, 3);
                    if (q != null) { result.Add(q); break; } // one per poem
                }
            }
            return result;
        }

        /// <summary>Apply per-poem difficulty overrides (from local user data) onto loaded poems.</summary>
        public void ApplyDifficultyOverrides(IReadOnlyDictionary<string, int> overrides)
        {
            if (overrides == null) return;
            foreach (var kv in overrides)
                if (_byId.TryGetValue(kv.Key, out var p)) p.Difficulty = kv.Value;
        }

        /// <summary>Set one poem's difficulty in memory (caller persists the override).</summary>
        public void SetDifficulty(string poemId, int tier)
        {
            if (!string.IsNullOrEmpty(poemId) && _byId.TryGetValue(poemId, out var p)) p.Difficulty = tier;
        }

        /// <summary>Look up specific questions by id (used by 错题本 review sessions).</summary>
        public List<Question> GetQuestionsByIds(IEnumerable<string> ids)
        {
            var byQId = new Dictionary<string, Question>();
            foreach (var q in _questions) byQId[q.Id] = q;
            var result = new List<Question>();
            foreach (var id in ids)
                if (byQId.TryGetValue(id, out var q)) result.Add(q);
            return result;
        }
    }
}
