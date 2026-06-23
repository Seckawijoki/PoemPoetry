using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Records persisted as one summary index plus one file per challenge, so saving a
    /// single record never rewrites the whole history.
    ///   {root}/records/index.json   -> RecordIndex (newest first)
    ///   {root}/records/{id}.json    -> full ChallengeRecord
    /// </summary>
    public sealed class JsonRecordRepository : IRecordRepository
    {
        private readonly string _dir;
        private readonly string _indexPath;
        private readonly object _lock = new object();

        public JsonRecordRepository(string rootDir)
        {
            _dir = Path.Combine(rootDir, "records");
            _indexPath = Path.Combine(_dir, "index.json");
        }

        public Task<IReadOnlyList<ChallengeRecordSummary>> GetAllSummariesAsync()
        {
            lock (_lock)
            {
                var index = JsonFileIO.ReadOrDefault<RecordIndex>(_indexPath);
                return Task.FromResult((IReadOnlyList<ChallengeRecordSummary>)index.Records);
            }
        }

        public Task<ChallengeRecord> GetByIdAsync(string id)
        {
            lock (_lock)
            {
                var path = Path.Combine(_dir, id + ".json");
                if (!File.Exists(path)) return Task.FromResult<ChallengeRecord>(null);
                var rec = PoemJson.Deserialize<ChallengeRecord>(File.ReadAllText(path, Encoding.UTF8));
                return Task.FromResult(rec);
            }
        }

        public Task SaveAsync(ChallengeRecord record)
        {
            lock (_lock)
            {
                JsonFileIO.Write(Path.Combine(_dir, record.Id + ".json"), record);
                var index = JsonFileIO.ReadOrDefault<RecordIndex>(_indexPath);
                index.Records.RemoveAll(r => r.Id == record.Id);
                index.Records.Insert(0, record.ToSummary()); // newest first
                JsonFileIO.Write(_indexPath, index);
                return Task.CompletedTask;
            }
        }

        public Task DeleteAsync(string id)
        {
            lock (_lock)
            {
                var path = Path.Combine(_dir, id + ".json");
                if (File.Exists(path)) File.Delete(path);
                var index = JsonFileIO.ReadOrDefault<RecordIndex>(_indexPath);
                index.Records.RemoveAll(r => r.Id == id);
                JsonFileIO.Write(_indexPath, index);
                return Task.CompletedTask;
            }
        }

        internal class RecordIndex
        {
            public List<ChallengeRecordSummary> Records = new List<ChallengeRecordSummary>();
        }
    }
}
