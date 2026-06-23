using System;
using System.Collections.Generic;

namespace PoemPoetry.Services
{
    /// <summary>Abstracts randomness so session building / shuffling is deterministic in tests.</summary>
    public interface IRandomSource
    {
        int Next(int maxExclusive);
    }

    public sealed class SystemRandomSource : IRandomSource
    {
        private readonly Random _r;
        public SystemRandomSource() { _r = new Random(); }
        public SystemRandomSource(int seed) { _r = new Random(seed); }
        public int Next(int maxExclusive) => maxExclusive <= 0 ? 0 : _r.Next(maxExclusive);
    }

    public static class ShuffleUtil
    {
        /// <summary>Fisher–Yates in place.</summary>
        public static void ShuffleInPlace<T>(IList<T> list, IRandomSource rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }
    }

    public static class ScoreMath
    {
        public static int AccuracyPercent(int correct, int total) =>
            total <= 0 ? 0 : (int)Math.Round(100.0 * correct / total, MidpointRounding.AwayFromZero);

        public static int BestStreak(IEnumerable<bool> outcomes)
        {
            int best = 0, cur = 0;
            foreach (var ok in outcomes)
            {
                if (ok) { cur++; if (cur > best) best = cur; }
                else cur = 0;
            }
            return best;
        }
    }
}
