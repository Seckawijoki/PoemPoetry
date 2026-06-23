using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Per-line difficulty derivation. A poem carries a tier (0/1/2/4); each line's difficulty is
    /// derived from that tier and whether the line is a 名句 (famous). Pure / UnityEngine-free.
    ///   tier 0 → every line 0
    ///   tier 1 → 名句 1, others 2
    ///   tier 2 → 名句 2, others 4
    ///   tier 4 → every line 4
    /// The whole poem's "average difficulty" is the mean of its line difficulties.
    /// </summary>
    public static class DifficultyRules
    {
        public static int LineDifficulty(int poemTier, bool famous)
        {
            switch (poemTier)
            {
                case 0: return 0;
                case 1: return famous ? 1 : 2;
                case 2: return famous ? 2 : 4;
                case 4: return 4;
                default: return poemTier;
            }
        }

        public static int LineDifficulty(Poem p, int lineIndex)
        {
            if (p == null || lineIndex < 0 || lineIndex >= p.Lines.Count) return 0;
            return LineDifficulty(p.Difficulty, p.Lines[lineIndex].Famous);
        }

        /// <summary>Mean of line difficulties, rounded; 0 for an empty poem.</summary>
        public static int AvgDifficulty(Poem p)
        {
            if (p == null || p.Lines.Count == 0) return 0;
            int sum = 0;
            for (int i = 0; i < p.Lines.Count; i++) sum += LineDifficulty(p, i);
            return (sum + p.Lines.Count / 2) / p.Lines.Count; // rounded
        }

        /// <summary>句号 group of a line: explicit Group if set, else couplet index (i/2).</summary>
        public static int EffectiveGroup(Poem p, int lineIndex)
        {
            if (p == null || lineIndex < 0 || lineIndex >= p.Lines.Count) return lineIndex / 2;
            int g = p.Lines[lineIndex].Group;
            return g >= 0 ? g : lineIndex / 2;
        }
    }
}
