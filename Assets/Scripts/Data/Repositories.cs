using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>
    /// The "local database" abstraction. Callers only see CRUD; the implementation is
    /// JSON-on-disk today and can become SQLite or a REST backend with zero churn here.
    /// All methods are async-shaped to keep the future backend swap painless.
    /// </summary>
    public interface IRecordRepository
    {
        Task<IReadOnlyList<ChallengeRecordSummary>> GetAllSummariesAsync();
        Task<ChallengeRecord> GetByIdAsync(string id);
        Task SaveAsync(ChallengeRecord record);
        Task DeleteAsync(string id);
    }

    public interface IFavoriteRepository
    {
        Task<IReadOnlyList<FavoriteEntry>> GetAllAsync();
        Task<bool> ExistsAsync(string poemId);
        Task AddAsync(FavoriteEntry entry);
        Task RemoveAsync(string poemId);
    }

    public interface IWrongBookRepository
    {
        Task<IReadOnlyList<WrongBookEntry>> GetAllAsync();
        Task<IReadOnlyList<WrongBookEntry>> GetDueAsync(DateTime nowUtc);
        Task<WrongBookEntry> GetAsync(string questionId);
        Task UpsertAsync(WrongBookEntry entry);
        Task RemoveAsync(string questionId);
    }

    public interface ISettingsStore
    {
        Task<UserSettings> LoadAsync();
        Task SaveAsync(UserSettings settings);
    }

    /// <summary>Per-poem difficulty overrides set in-app (poemId → tier).</summary>
    public interface IDifficultyOverrideStore
    {
        Task<IReadOnlyDictionary<string, int>> LoadAsync();
        Task SaveAsync(IReadOnlyDictionary<string, int> overrides);
    }
}
