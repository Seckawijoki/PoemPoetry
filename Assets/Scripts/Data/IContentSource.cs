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
        Task<IReadOnlyList<Question>> LoadQuestionsAsync();

        /// <summary>char → pinyin readings (多音字 has multiple). Used by RhymeService / editor tools.</summary>
        Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCharPinyinAsync();

        /// <summary>pinyin final → coarse 新韵 group id.</summary>
        Task<IReadOnlyDictionary<string, string>> LoadRhymeGroupsAsync();
    }
}
