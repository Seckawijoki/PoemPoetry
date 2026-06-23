using System.Collections.Generic;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// Applies and persists in-app per-poem difficulty overrides. Overrides are loaded onto the
    /// in-memory poems at startup so pools/tiers reflect them immediately.
    /// </summary>
    public sealed class DifficultyService
    {
        private readonly IDifficultyOverrideStore _store;
        private readonly ContentService _content;
        private Dictionary<string, int> _overrides = new Dictionary<string, int>();

        public DifficultyService(IDifficultyOverrideStore store, ContentService content)
        {
            _store = store;
            _content = content;
        }

        public async Task InitAsync()
        {
            var loaded = await _store.LoadAsync();
            _overrides = new Dictionary<string, int>();
            if (loaded != null)
                foreach (var kv in loaded) _overrides[kv.Key] = kv.Value;
            _content.ApplyDifficultyOverrides(_overrides);
        }

        public async Task SetAsync(string poemId, int tier)
        {
            _overrides[poemId] = tier;
            _content.SetDifficulty(poemId, tier);
            await _store.SaveAsync(_overrides);
        }

        public int Get(string poemId)
        {
            var p = _content.GetPoem(poemId);
            return p != null ? p.Difficulty : 0;
        }
    }
}
