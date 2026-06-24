using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Computes 平/仄 from the shipped char→pinyin dictionary. In 中华新韵 (普通话) terms the
    /// 阴平/阳平 (tone 1/2) are 平 and 上声/去声 (tone 3/4) are 仄. The dictionary stores tones as a
    /// trailing digit (e.g. "guang1"); a missing tone or 轻声 (0) falls back to 平.
    ///
    /// Pure, UnityEngine-free, deterministic — shares the same char→pinyin map as
    /// <see cref="RhymeService"/>. For 多音字 it uses the first reading (the dictionary's primary),
    /// which is good enough for the wordcloze distractor constraint and can be hand-corrected
    /// in the word bank when wrong.
    /// </summary>
    public sealed class ToneService
    {
        public const string Ping = "平";
        public const string Ze = "仄";

        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _charPinyin;

        public ToneService(IReadOnlyDictionary<string, IReadOnlyList<string>> charPinyin)
        {
            _charPinyin = charPinyin ?? new Dictionary<string, IReadOnlyList<string>>();
        }

        /// <summary>平/仄 of a single character (uses its primary reading). Unknown → 平.</summary>
        public string ToneOf(string ch)
        {
            if (string.IsNullOrEmpty(ch)) return Ping;
            if (!_charPinyin.TryGetValue(ch, out var readings) || readings == null || readings.Count == 0)
                return Ping;
            return ClassToneDigit(LastDigit(readings[0]));
        }

        /// <summary>Per-character 平仄 string for a word, e.g. "光阴" → "平平". Punctuation chars are skipped.</summary>
        public string ToneString(string word)
        {
            if (string.IsNullOrEmpty(word)) return "";
            var sb = new StringBuilder();
            var si = new StringInfo(RhymeService.StripPunct(word));
            int n = si.LengthInTextElements;
            for (int i = 0; i < n; i++) sb.Append(ToneOf(si.SubstringByTextElements(i, 1)));
            return sb.ToString();
        }

        // 1/2 -> 平; 3/4 -> 仄; 0/none -> 平 (轻声 treated as 平 for matching purposes).
        private static string ClassToneDigit(int digit) => (digit == 3 || digit == 4) ? Ze : Ping;

        // Trailing tone digit of a pinyin syllable, or 0 if none ("guang1" -> 1, "guang" -> 0).
        private static int LastDigit(string pinyin)
        {
            if (string.IsNullOrEmpty(pinyin)) return 0;
            char c = pinyin[pinyin.Length - 1];
            return (c >= '0' && c <= '9') ? c - '0' : 0;
        }
    }
}
