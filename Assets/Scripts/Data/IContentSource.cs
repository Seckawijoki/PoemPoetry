using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Loads shipped, read-only content. Async-shaped now so a remote/Addressables source
    /// can replace the local one later with no churn above the data layer.
    /// </summary>
    public interface IContentSource
    {
        Task<IReadOnlyList<Poem>> LoadPoemsAsync();

        /// <summary>The full question bank (shared 干扰项簇 + lightweight questions). v2 schema.</summary>
        Task<QuestionFile> LoadQuestionBankAsync();

        /// <summary>逐词填空 (残句调控) question bank; empty if the file isn't shipped.</summary>
        Task<IReadOnlyList<WordClozeQuestion>> LoadWordClozeQuestionsAsync();

        /// <summary>char → pinyin readings (多音字 has multiple). Used by RhymeService / editor tools.</summary>
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCharPinyinAsync();

        /// <summary>pinyin final → coarse 新韵 group id.</summary>
        Task<IReadOnlyDictionary<string, string>> LoadRhymeGroupsAsync();

        /// <summary>char → 平水韵 韵部 id[] (多音字 may span several); empty if the file isn't shipped.</summary>
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadPingshuiRhymeAsync();
    }
}
