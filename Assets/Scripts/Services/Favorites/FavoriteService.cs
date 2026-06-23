using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>Poem-level favoriting. Raises <see cref="FavoritesChanged"/> so screens stay in sync.</summary>
    public sealed class FavoriteService
    {
        private readonly IFavoriteRepository _repo;
        private readonly IClock _clock;

        public event Action FavoritesChanged;

        public FavoriteService(IFavoriteRepository repo, IClock clock)
        {
            _repo = repo;
            _clock = clock ?? new SystemClock();
        }

        public Task<bool> IsFavoriteAsync(string poemId) => _repo.ExistsAsync(poemId);
        public Task<IReadOnlyList<FavoriteEntry>> GetAllAsync() => _repo.GetAllAsync();

        public async Task AddAsync(string poemId)
        {
            await _repo.AddAsync(new FavoriteEntry { PoemId = poemId, AddedAtUtc = _clock.UtcNow.ToString("o") });
            FavoritesChanged?.Invoke();
        }

        public async Task RemoveAsync(string poemId)
        {
            await _repo.RemoveAsync(poemId);
            FavoritesChanged?.Invoke();
        }

        /// <summary>Toggle favorite; returns the new state (true = now favorited).</summary>
        public async Task<bool> ToggleAsync(string poemId)
        {
            if (await _repo.ExistsAsync(poemId)) { await RemoveAsync(poemId); return false; }
            await AddAsync(poemId);
            return true;
        }
    }
}
