using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>Difficulty overrides stored as {root}/difficulty_overrides.json.</summary>
    public sealed class JsonDifficultyOverrideStore : IDifficultyOverrideStore
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public JsonDifficultyOverrideStore(string rootDir)
        {
            _path = Path.Combine(rootDir, "difficulty_overrides.json");
        }

        public Task<IReadOnlyDictionary<string, int>> LoadAsync()
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<OverrideFile>(_path);
                return Task.FromResult((IReadOnlyDictionary<string, int>)file.Overrides);
            }
        }

        public Task SaveAsync(IReadOnlyDictionary<string, int> overrides)
        {
            lock (_lock)
            {
                var file = new OverrideFile();
                if (overrides != null)
                    foreach (var kv in overrides) file.Overrides[kv.Key] = kv.Value;
                JsonFileIO.Write(_path, file);
                return Task.CompletedTask;
            }
        }

        internal class OverrideFile
        {
            public Dictionary<string, int> Overrides = new Dictionary<string, int>();
        }
    }
}
