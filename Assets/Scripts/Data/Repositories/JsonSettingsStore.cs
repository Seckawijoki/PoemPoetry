using System.IO;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>User settings stored as {root}/settings.json.</summary>
    public sealed class JsonSettingsStore : ISettingsStore
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public JsonSettingsStore(string rootDir)
        {
            _path = Path.Combine(rootDir, "settings.json");
        }

        public Task<UserSettings> LoadAsync()
        {
            lock (_lock)
            {
                return Task.FromResult(JsonFileIO.ReadOrDefault<UserSettings>(_path));
            }
        }

        public Task SaveAsync(UserSettings settings)
        {
            lock (_lock)
            {
                JsonFileIO.Write(_path, settings);
                return Task.CompletedTask;
            }
        }
    }
}
