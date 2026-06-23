using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>Favorites stored as a single {root}/favorites.json (newest first).</summary>
    public sealed class JsonFavoriteRepository : IFavoriteRepository
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public JsonFavoriteRepository(string rootDir)
        {
            _path = Path.Combine(rootDir, "favorites.json");
        }

        public Task<IReadOnlyList<FavoriteEntry>> GetAllAsync()
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<FavoritesFile>(_path);
                return Task.FromResult((IReadOnlyList<FavoriteEntry>)file.Favorites);
            }
        }

        public Task<bool> ExistsAsync(string poemId)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<FavoritesFile>(_path);
                return Task.FromResult(file.Favorites.Exists(e => e.PoemId == poemId));
            }
        }

        public Task AddAsync(FavoriteEntry entry)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<FavoritesFile>(_path);
                if (!file.Favorites.Exists(e => e.PoemId == entry.PoemId))
                {
                    file.Favorites.Insert(0, entry);
                    JsonFileIO.Write(_path, file);
                }
                return Task.CompletedTask;
            }
        }

        public Task RemoveAsync(string poemId)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<FavoritesFile>(_path);
                if (file.Favorites.RemoveAll(e => e.PoemId == poemId) > 0)
                    JsonFileIO.Write(_path, file);
                return Task.CompletedTask;
            }
        }

        internal class FavoritesFile
        {
            public List<FavoriteEntry> Favorites = new List<FavoriteEntry>();
        }
    }
}
