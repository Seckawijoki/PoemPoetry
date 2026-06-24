using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Wraps the shipped char→pinyin dictionary and final→group table to annotate poem
    /// lines with rhyme metadata and disambiguate 多音字. Pure, UnityEngine-free.
    /// </summary>
    public sealed class RhymeService
    {
        private static readonly IReadOnlyList<string> Empty = new List<string>();

        // Punctuation stripped before counting/选韵脚.
        private const string Punct = "，。！？、；：“”‘’（）《》〈〉【】—…·.,!?;:\"'()[]{}「」『』　 \t\r\n";

        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _charPinyin;
        private readonly IReadOnlyDictionary<string, string> _rhymeGroups;
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _pingshui;

        public RhymeService(
            IReadOnlyDictionary<string, IReadOnlyList<string>> charPinyin,
            IReadOnlyDictionary<string, string> rhymeGroups = null,
            IReadOnlyDictionary<string, IReadOnlyList<string>> pingshui = null)
        {
            _charPinyin = charPinyin ?? new Dictionary<string, IReadOnlyList<string>>();
            _rhymeGroups = rhymeGroups;
            _pingshui = pingshui;
        }

        public static async Task<RhymeService> LoadAsync(IContentSource source)
        {
            var cp = await source.LoadCharPinyinAsync();
            var rg = await source.LoadRhymeGroupsAsync();
            var ps = await source.LoadPingshuiRhymeAsync();
            return new RhymeService(cp, rg, ps);
        }

        public bool HasChar(string ch) => _charPinyin.ContainsKey(ch);

        public IReadOnlyList<string> Readings(string ch) =>
            _charPinyin.TryGetValue(ch, out var r) ? r : Empty;

        /// <summary>新韵 group for a final token; shipped table wins, else the built-in default.</summary>
        public string GroupForFinal(string final)
        {
            if (string.IsNullOrEmpty(final)) return "";
            if (_rhymeGroups != null && _rhymeGroups.TryGetValue(final, out var g) && !string.IsNullOrEmpty(g))
                return g;
            return PinyinRhyme.GroupOf(final);
        }

        /// <summary>
        /// Rhyme final of a character. For 多音字, prefers the reading whose group is among
        /// <paramref name="contextGroups"/> (the rhyme groups of the poem's other 韵脚).
        /// </summary>
        public string FinalForChar(string ch, ISet<string> contextGroups = null)
        {
            var readings = Readings(ch);
            if (readings.Count == 0) return "";
            if (readings.Count == 1 || contextGroups == null || contextGroups.Count == 0)
                return PinyinRhyme.Final(readings[0]);

            foreach (var r in readings)
            {
                var f = PinyinRhyme.Final(r);
                if (contextGroups.Contains(GroupForFinal(f))) return f;
            }
            return PinyinRhyme.Final(readings[0]);
        }

        public string GroupForChar(string ch, ISet<string> contextGroups = null) =>
            GroupForFinal(FinalForChar(ch, contextGroups));

        /// <summary>
        /// 平水韵 韵部 of a character (finer than 新韵). For 多音字 spanning several 韵部, prefers the one
        /// in <paramref name="contextPingshui"/> (the poem's other 韵脚 韵部). "" if no table or no entry.
        /// </summary>
        public string PingshuiForChar(string ch, ISet<string> contextPingshui = null)
        {
            if (_pingshui == null || string.IsNullOrEmpty(ch)) return "";
            if (!_pingshui.TryGetValue(ch, out var parts) || parts == null || parts.Count == 0) return "";
            if (parts.Count == 1 || contextPingshui == null || contextPingshui.Count == 0) return parts[0];
            foreach (var p in parts)
                if (contextPingshui.Contains(p)) return p;
            return parts[0];
        }

        /// <summary>Fills CharCount / LastChar / RhymeFinal / RhymeGroup / PingshuiRhyme on a line.</summary>
        public void Annotate(PoemLine line, ISet<string> contextGroups = null, ISet<string> contextPingshui = null)
        {
            var stripped = StripPunct(line.Text);
            var elements = new StringInfo(stripped);
            line.CharCount = elements.LengthInTextElements;
            if (line.CharCount > 0)
            {
                var last = elements.SubstringByTextElements(line.CharCount - 1, 1);
                line.LastChar = last;
                line.RhymeFinal = FinalForChar(last, contextGroups);
                line.RhymeGroup = GroupForFinal(line.RhymeFinal);
                line.PingshuiRhyme = PingshuiForChar(last, contextPingshui);
            }
        }

        public static string StripPunct(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
                if (Punct.IndexOf(ch) < 0) sb.Append(ch);
            return sb.ToString();
        }
    }
}
