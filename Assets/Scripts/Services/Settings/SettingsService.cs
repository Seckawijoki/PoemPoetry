using System;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>Loads/holds/persists <see cref="UserSettings"/>. Call <see cref="InitAsync"/> at startup.</summary>
    public sealed class SettingsService
    {
        private readonly ISettingsStore _store;

        public UserSettings Current { get; private set; } = new UserSettings();
        public event Action SettingsChanged;

        public SettingsService(ISettingsStore store) { _store = store; }

        public async Task InitAsync()
        {
            Current = await _store.LoadAsync() ?? new UserSettings();
        }

        public async Task SaveAsync()
        {
            await _store.SaveAsync(Current);
            SettingsChanged?.Invoke();
        }

        public async Task SetLastChallengeLengthAsync(int length)
        {
            Current.LastChallengeLength = length;
            await SaveAsync();
        }
    }
}
