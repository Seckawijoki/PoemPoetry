using System.Collections.Generic;

namespace PoemPoetry.Data
{
    /// <summary>Root object of poems.json (JSON has no top-level arrays we rely on across tools).</summary>
    public class PoemFile
    {
        public int SchemaVersion = 1;
        public List<Poem> Poems = new List<Poem>();
    }

    /// <summary>Root object of questions.json.</summary>
    public class QuestionFile
    {
        public int SchemaVersion = 1;
        public List<Question> Questions = new List<Question>();
    }

    /// <summary>Root object of char_pinyin.json: 字 → 拼音读音[] (多音字 has several).</summary>
    public class CharPinyinFile
    {
        public int SchemaVersion = 1;
        public Dictionary<string, List<string>> Entries = new Dictionary<string, List<string>>();
    }

    /// <summary>Root object of rhyme_groups.json: 拼音韵母 → 新韵组 id.</summary>
    public class RhymeGroupFile
    {
        public int SchemaVersion = 1;
        public Dictionary<string, string> Groups = new Dictionary<string, string>();
    }

    /// <summary>Root object of pingshui_rhyme.json: 字 → 平水韵 韵部 id[] (多音字 may span several 韵部).</summary>
    public class PingshuiRhymeFile
    {
        public int SchemaVersion = 1;
        public Dictionary<string, List<string>> Entries = new Dictionary<string, List<string>>();
    }
}
