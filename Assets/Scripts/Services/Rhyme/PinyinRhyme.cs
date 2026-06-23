using System.Collections.Generic;
using System.Text;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Computes a coarse 中华新韵 (14-group) rhyme key from a pinyin syllable. Fully offline
    /// and computable — good enough for the gameplay constraint of "same / close rhyme".
    /// Returns a canonical final token; <see cref="GroupOf"/> maps that token to a 韵部 id.
    /// Pure, deterministic, UnityEngine-free.
    /// </summary>
    public static class PinyinRhyme
    {
        private static readonly string[] DoubleInitials = { "zh", "ch", "sh" };
        private const string SingleInitials = "bpmfdtnlgkhjqxrzcs";
        private const string BuzzInitials = "zh,ch,sh,r,z,c,s";

        // Canonical final token -> 新韵 group id (1..14). Used as the built-in default; a
        // shipped rhyme_groups.json can override/extend this via RhymeService.
        private static readonly Dictionary<string, string> DefaultGroups = BuildDefaultGroups();

        /// <summary>Canonical rhyme final token for a pinyin syllable, e.g. "xiang1" -> "iang".</summary>
        public static string Final(string pinyin)
        {
            if (string.IsNullOrEmpty(pinyin)) return "";
            var s = StripTone(pinyin);
            if (s.Length == 0) return "";

            // Normalize y/w spellings into medial i/u/ü.
            if (s.StartsWith("yu")) s = "ü" + s.Substring(2);          // yu->ü, yuan->üan, yue->üe, yun->ün
            else if (s.StartsWith("yi")) s = s.Substring(1);          // yi->i, yin->in, ying->ing
            else if (s.StartsWith("y")) s = "i" + s.Substring(1);     // ya->ia, ye->ie, yao->iao, yan->ian...
            else if (s.StartsWith("wu")) s = s.Substring(1);          // wu->u
            else if (s.StartsWith("w")) s = "u" + s.Substring(1);     // wa->ua, wei->uei, wang->uang...

            // Strip leading consonant initial.
            string initial = "";
            foreach (var di in DoubleInitials)
            {
                if (s.StartsWith(di)) { initial = di; break; }
            }
            if (initial.Length == 0 && s.Length > 1 && SingleInitials.IndexOf(s[0]) >= 0)
                initial = s[0].ToString();

            var rest = s.Substring(initial.Length);

            // After j/q/x, a written "u" is actually ü (ju=jü, xue=xüe, jun=jün).
            if ((initial == "j" || initial == "q" || initial == "x") && rest.StartsWith("u"))
                rest = "ü" + rest.Substring(1);
            // "v" is an ASCII spelling of ü (lv = lü).
            if (rest.StartsWith("v")) rest = "ü" + rest.Substring(1);

            // The buzzing -i of zhi/chi/shi/ri/zi/ci/si is a separate 韵部 from the -i of ji/qi/yi.
            if (rest == "i" && IsBuzz(initial)) rest = "i_buzz";

            return rest;
        }

        /// <summary>新韵 group id for a final token, or "" if unknown.</summary>
        public static string GroupOf(string finalToken)
        {
            if (string.IsNullOrEmpty(finalToken)) return "";
            return DefaultGroups.TryGetValue(finalToken, out var g) ? g : "";
        }

        public static IReadOnlyDictionary<string, string> DefaultGroupMap => DefaultGroups;

        private static bool IsBuzz(string initial)
        {
            foreach (var b in BuzzInitials.Split(','))
                if (b == initial) return true;
            return false;
        }

        private static string StripTone(string pinyin)
        {
            var sb = new StringBuilder(pinyin.Length);
            foreach (var c in pinyin.Trim().ToLowerInvariant())
            {
                if (c >= '0' && c <= '9') continue; // numeric tone marks
                sb.Append(FoldDiacritic(c));
            }
            return sb.ToString();
        }

        // Folds toned vowels to their base letter (ā->a ...), keeping ü distinct.
        private static char FoldDiacritic(char c)
        {
            switch (c)
            {
                case 'ā': case 'á': case 'ǎ': case 'à': return 'a';
                case 'ō': case 'ó': case 'ǒ': case 'ò': return 'o';
                case 'ē': case 'é': case 'ě': case 'è': case 'ê': return 'e';
                case 'ī': case 'í': case 'ǐ': case 'ì': return 'i';
                case 'ū': case 'ú': case 'ǔ': case 'ù': return 'u';
                case 'ǖ': case 'ǘ': case 'ǚ': case 'ǜ': case 'ü': return 'ü';
                default: return c;
            }
        }

        private static Dictionary<string, string> BuildDefaultGroups()
        {
            var m = new Dictionary<string, string>();
            void Add(string group, params string[] tokens)
            {
                foreach (var t in tokens) m[t] = group;
            }
            Add("1", "a", "ia", "ua");                    // 麻
            Add("2", "o", "e", "uo");                     // 波
            Add("3", "ie", "üe");                         // 皆
            Add("4", "ai", "uai");                        // 开
            Add("5", "ei", "ui", "uei");                  // 微
            Add("6", "ao", "iao");                        // 豪
            Add("7", "ou", "iu", "iou");                  // 尤
            Add("8", "an", "ian", "uan", "üan");          // 寒
            Add("9", "en", "in", "un", "uen", "ün");      // 文
            Add("10", "ang", "iang", "uang");             // 唐
            Add("11", "eng", "ing", "ong", "iong", "ueng"); // 庚
            Add("12", "i", "ü", "er");                    // 齐
            Add("13", "i_buzz");                          // 支
            Add("14", "u");                               // 姑
            return m;
        }
    }
}
