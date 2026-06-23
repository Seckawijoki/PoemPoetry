using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>错题本 entries stored as a single {root}/wrongbook.json.</summary>
    public sealed class JsonWrongBookRepository : IWrongBookRepository
    {
        private readonly string _path;
        private readonly object _lock = new object();

        public JsonWrongBookRepository(string rootDir)
        {
            _path = Path.Combine(rootDir, "wrongbook.json");
        }

        public Task<IReadOnlyList<WrongBookEntry>> GetAllAsync()
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<WrongBookFile>(_path);
                return Task.FromResult((IReadOnlyList<WrongBookEntry>)file.Entries);
            }
        }

        public Task<IReadOnlyList<WrongBookEntry>> GetDueAsync(DateTime nowUtc)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<WrongBookFile>(_path);
                var due = file.Entries.FindAll(e => ParseUtc(e.NextReviewUtc) <= nowUtc);
                return Task.FromResult((IReadOnlyList<WrongBookEntry>)due);
            }
        }

        public Task<WrongBookEntry> GetAsync(string questionId)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<WrongBookFile>(_path);
                return Task.FromResult(file.Entries.Find(e => e.QuestionId == questionId));
            }
        }

        public Task UpsertAsync(WrongBookEntry entry)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<WrongBookFile>(_path);
                file.Entries.RemoveAll(e => e.QuestionId == entry.QuestionId);
                file.Entries.Add(entry);
                JsonFileIO.Write(_path, file);
                return Task.CompletedTask;
            }
        }

        public Task RemoveAsync(string questionId)
        {
            lock (_lock)
            {
                var file = JsonFileIO.ReadOrDefault<WrongBookFile>(_path);
                if (file.Entries.RemoveAll(e => e.QuestionId == questionId) > 0)
                    JsonFileIO.Write(_path, file);
                return Task.CompletedTask;
            }
        }

        private static DateTime ParseUtc(string s)
        {
            if (string.IsNullOrEmpty(s)) return DateTime.MinValue;
            return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        internal class WrongBookFile
        {
            public List<WrongBookEntry> Entries = new List<WrongBookEntry>();
        }
    }
}
