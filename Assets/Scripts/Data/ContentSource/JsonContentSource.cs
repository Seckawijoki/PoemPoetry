using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Loads shipped content from JSON via an <see cref="IRawTextLoader"/>. Platform-agnostic:
    /// the loader handles where bytes come from (File.IO on desktop/editor/iOS, UnityWebRequest
    /// on Android). Parsing is identical everywhere.
    /// </summary>
    public sealed class JsonContentSource : IContentSource
    {
        public const string PoemsPath = "PoemData/poems.json";
        public const string QuestionsPath = "PoemData/questions.json";
        public const string WordClozeQuestionsPath = "PoemData/word_questions.json";
        public const string CharPinyinPath = "PoemData/char_pinyin.json";
        public const string RhymeGroupsPath = "PoemData/rhyme_groups.json";
        public const string PingshuiRhymePath = "PoemData/pingshui_rhyme.json";

        private readonly IRawTextLoader _loader;

        public JsonContentSource(IRawTextLoader loader) { _loader = loader; }

        public async Task<IReadOnlyList<Poem>> LoadPoemsAsync()
        {
            var json = await _loader.ReadTextAsync(PoemsPath);
            var file = PoemJson.Deserialize<PoemFile>(json);
            return (IReadOnlyList<Poem>)(file?.Poems) ?? new List<Poem>();
        }

        public async Task<IReadOnlyList<Question>> LoadQuestionsAsync()
        {
            if (!_loader.Exists(QuestionsPath)) return new List<Question>();
            var json = await _loader.ReadTextAsync(QuestionsPath);
            var file = PoemJson.Deserialize<QuestionFile>(json);
            return (IReadOnlyList<Question>)(file?.Questions) ?? new List<Question>();
        }

        public async Task<IReadOnlyList<WordClozeQuestion>> LoadWordClozeQuestionsAsync()
        {
            if (!_loader.Exists(WordClozeQuestionsPath)) return new List<WordClozeQuestion>();
            var json = await _loader.ReadTextAsync(WordClozeQuestionsPath);
            var file = PoemJson.Deserialize<WordClozeQuestionFile>(json);
            return (IReadOnlyList<WordClozeQuestion>)(file?.Questions) ?? new List<WordClozeQuestion>();
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCharPinyinAsync()
        {
            var json = await _loader.ReadTextAsync(CharPinyinPath);
            var file = PoemJson.Deserialize<CharPinyinFile>(json);
            var result = new Dictionary<string, IReadOnlyList<string>>();
            if (file?.Entries != null)
                foreach (var kv in file.Entries)
                    result[kv.Key] = kv.Value;
            return result;
        }

        public async Task<IReadOnlyDictionary<string, string>> LoadRhymeGroupsAsync()
        {
            var json = await _loader.ReadTextAsync(RhymeGroupsPath);
            var file = PoemJson.Deserialize<RhymeGroupFile>(json);
            return (IReadOnlyDictionary<string, string>)(file?.Groups)
                   ?? new Dictionary<string, string>();
        }

        public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadPingshuiRhymeAsync()
        {
            var result = new Dictionary<string, IReadOnlyList<string>>();
            if (!_loader.Exists(PingshuiRhymePath)) return result; // optional asset; degrade gracefully
            var json = await _loader.ReadTextAsync(PingshuiRhymePath);
            var file = PoemJson.Deserialize<PingshuiRhymeFile>(json);
            if (file?.Entries != null)
                foreach (var kv in file.Entries)
                    result[kv.Key] = kv.Value;
            return result;
        }
    }
}
