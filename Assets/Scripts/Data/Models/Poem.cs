using System.Collections.Generic;
using Newtonsoft.Json;

namespace PoemPoetry.Data
{
    /// <summary>
    /// A single shipped poem (诗/词/曲). Read-only content loaded from StreamingAssets.
    /// Pure POCO — no UnityEngine dependency so it can be unit-tested off the engine.
    /// </summary>
    public class Poem
    {
        public string Id;
        public string Dynasty;       // 朝代, e.g. "唐"
        public string Author;        // 作者
        public string Title;         // 标题
        public string Type;          // 诗 / 词 / 曲
        public string Cipai;         // 词牌名 (optional, "" for 诗)
        public string Fame;          // famous | common | obscure
        public int Difficulty;       // 难度档: 0 = 当前题库(默认), higher = harder tier (set manually)
        public string Source;        // 出处, e.g. "全唐诗"
        public string Translation;   // 译文 (optional)
        public string Appreciation;  // 赏析 (optional)
        public List<string> Tags = new List<string>();
        public List<PoemLine> Lines = new List<PoemLine>();

        /// <summary>First line of body text, for list previews. Safe on empty poems.</summary>
        [JsonIgnore]
        public string FirstLineText
        {
            get { return (Lines != null && Lines.Count > 0) ? Lines[0].Text : ""; }
        }
    }

    /// <summary>One line of a poem, pre-annotated by the content pipeline.</summary>
    public class PoemLine
    {
        public string Text;
        public int CharCount;            // code-point length, punctuation stripped
        public string LastChar;          // 韵脚字
        public string RhymeFinal;        // pinyin final of LastChar, e.g. "iang"
        public string RhymeGroup;        // coarse 新韵 bucket id, e.g. "13"
        public string PingshuiRhyme;     // 平水韵 韵部 id of LastChar (finer than 新韵), "" if unknown
        public string Tone;              // 平仄 pattern, "" if unknown
        public bool IsRhymeLine;         // participates in the poem's rhyme
        public string PosPattern;        // dash-joined coarse POS tags, "" if unlabeled
        public int CoupletPartnerIndex = -1; // index of the 对仗 partner line, -1 if none

        // 句号组号: lines sharing a Group form one 句号 sentence (used to pick context neighbours
        // when blanking this line). -1 = unset → auto-grouped by couplet (index/2) at load.
        public int Group = -1;
        public bool Famous;              // 名句 marker → lower per-line difficulty within the poem

        // Explicit per-line difficulty override (set in the editor). -1 = derive from the poem's
        // tier + 名句 via DifficultyRules; >=0 = use this value directly (ignores tier/名句).
        public int Diff = -1;
    }
}
