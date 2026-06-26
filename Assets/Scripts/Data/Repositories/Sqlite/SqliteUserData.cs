using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite4Unity3d;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Writable per-user database (<c>user.db</c> in persistentDataPath) backing records,
    /// favorites, 错题本, settings and difficulty overrides. Replaces the per-file JSON repos with
    /// one transactional store; on first open it imports any existing JSON via the old repos and
    /// renames them aside, so upgrading players keep their data.
    ///
    /// One shared connection guarded by a lock (the app touches it from the main thread, but the
    /// lock keeps it correct if a write ever races a read). Pure C#: callers pass the dir path.
    /// </summary>
    public sealed class SqliteUserDatabase
    {
        internal readonly SQLiteConnection Conn;
        internal readonly object Gate = new object();

        private SqliteUserDatabase(string dbPath)
        {
            Conn = new SQLiteConnection(dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            CreateSchema();
        }

        public static async Task<SqliteUserDatabase> OpenAsync(string rootDir)
        {
            var dbPath = System.IO.Path.Combine(rootDir, "user.db");
            var db = new SqliteUserDatabase(dbPath);
            await db.MigrateFromJsonIfNeededAsync(rootDir);
            return db;
        }

        private void CreateSchema()
        {
            Conn.Execute("CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT)");
            Conn.Execute(
                "CREATE TABLE IF NOT EXISTS records(" +
                "id TEXT PRIMARY KEY, seq INTEGER, completed_at_utc TEXT, mode TEXT, " +
                "accuracy_percent INTEGER, correct_count INTEGER, total INTEGER, " +
                "duration_seconds INTEGER, best_streak INTEGER, json TEXT)");
            Conn.Execute("CREATE TABLE IF NOT EXISTS favorites(poem_id TEXT PRIMARY KEY, seq INTEGER, added_at_utc TEXT)");
            Conn.Execute(
                "CREATE TABLE IF NOT EXISTS wrongbook(" +
                "question_id TEXT PRIMARY KEY, poem_id TEXT, box INTEGER, next_review_utc TEXT, " +
                "last_result TEXT, added_at_utc TEXT, review_count INTEGER)");
            Conn.Execute("CREATE INDEX IF NOT EXISTS ix_wrongbook_due ON wrongbook(next_review_utc)");
            Conn.Execute("CREATE TABLE IF NOT EXISTS settings(id INTEGER PRIMARY KEY, json TEXT)");
            Conn.Execute("CREATE TABLE IF NOT EXISTS difficulty_overrides(poem_id TEXT PRIMARY KEY, tier INTEGER)");
        }

        internal long NextSeq(string table)
        {
            return Conn.ExecuteScalar<long>($"SELECT COALESCE(MAX(seq),0)+1 FROM {table}");
        }

        // ---- one-time JSON → SQLite migration -------------------------------------------------
        private async Task MigrateFromJsonIfNeededAsync(string rootDir)
        {
            string done = Conn.ExecuteScalar<string>("SELECT value FROM meta WHERE key='migrated'");
            if (done == "1") return;

            // Read existing data through the old JSON repos (identical parsing), write into SQLite.
            var recRepo = new JsonRecordRepository(rootDir);
            var favRepo = new JsonFavoriteRepository(rootDir);
            var wrongRepo = new JsonWrongBookRepository(rootDir);

            foreach (var s in await recRepo.GetAllSummariesAsync())
            {
                var full = await recRepo.GetByIdAsync(s.Id);
                if (full != null) new SqliteRecordRepository(this).SaveSync(full);
            }
            foreach (var f in await favRepo.GetAllAsync()) new SqliteFavoriteRepository(this).AddSync(f);
            foreach (var w in await wrongRepo.GetAllAsync()) new SqliteWrongBookRepository(this).UpsertSync(w);

            var settings = await new JsonSettingsStore(rootDir).LoadAsync();
            new SqliteSettingsStore(this).SaveSync(settings);
            var ov = await new JsonDifficultyOverrideStore(rootDir).LoadAsync();
            new SqliteDifficultyOverrideStore(this).SaveSync(ov);

            Conn.Execute("INSERT OR REPLACE INTO meta(key,value) VALUES('migrated','1')");
            RenameAside(rootDir);
        }

        // Move imported JSON aside (reversible) so the old path no longer shadows the DB.
        private static void RenameAside(string rootDir)
        {
            foreach (var name in new[] { "favorites.json", "wrongbook.json", "settings.json", "difficulty_overrides.json" })
            {
                var p = System.IO.Path.Combine(rootDir, name);
                try { if (System.IO.File.Exists(p)) System.IO.File.Move(p, p + ".migrated"); } catch { /* best effort */ }
            }
            var recDir = System.IO.Path.Combine(rootDir, "records");
            try { if (System.IO.Directory.Exists(recDir)) System.IO.Directory.Move(recDir, recDir + ".migrated"); } catch { /* best effort */ }
        }
    }

    // ----- row DTOs (sqlite-net maps result columns to public PROPERTIES by name) -----
    internal class RecordSummaryRow
    {
        public string Id { get; set; }
        public string CompletedAtUtc { get; set; }
        public string Mode { get; set; }
        public int AccuracyPercent { get; set; }
        public int CorrectCount { get; set; }
        public int Total { get; set; }
        public int DurationSeconds { get; set; }
        public int BestStreak { get; set; }
    }
    internal class JsonRow { public string Json { get; set; } }
    internal class FavoriteRow { public string PoemId { get; set; } public string AddedAtUtc { get; set; } }
    internal class WrongRow
    {
        public string QuestionId { get; set; }
        public string PoemId { get; set; }
        public int Box { get; set; }
        public string NextReviewUtc { get; set; }
        public string LastResult { get; set; }
        public string AddedAtUtc { get; set; }
        public int ReviewCount { get; set; }
    }
    internal class OverrideRow { public string PoemId { get; set; } public int Tier { get; set; } }

    /// <summary>Records: summary index + full JSON blob, newest-first by save order (seq DESC).</summary>
    public sealed class SqliteRecordRepository : IRecordRepository
    {
        private readonly SqliteUserDatabase _db;
        public SqliteRecordRepository(SqliteUserDatabase db) { _db = db; }

        public Task<IReadOnlyList<ChallengeRecordSummary>> GetAllSummariesAsync()
        {
            lock (_db.Gate)
            {
                var rows = _db.Conn.Query<RecordSummaryRow>(
                    "SELECT id AS Id, completed_at_utc AS CompletedAtUtc, mode AS Mode, " +
                    "accuracy_percent AS AccuracyPercent, correct_count AS CorrectCount, total AS Total, " +
                    "duration_seconds AS DurationSeconds, best_streak AS BestStreak FROM records ORDER BY seq DESC");
                var list = new List<ChallengeRecordSummary>(rows.Count);
                foreach (var r in rows)
                    list.Add(new ChallengeRecordSummary
                    {
                        Id = r.Id, CompletedAtUtc = r.CompletedAtUtc, Mode = r.Mode,
                        AccuracyPercent = r.AccuracyPercent, CorrectCount = r.CorrectCount, Total = r.Total,
                        DurationSeconds = r.DurationSeconds, BestStreak = r.BestStreak,
                    });
                return Task.FromResult((IReadOnlyList<ChallengeRecordSummary>)list);
            }
        }

        public Task<ChallengeRecord> GetByIdAsync(string id)
        {
            lock (_db.Gate)
            {
                var rows = _db.Conn.Query<JsonRow>("SELECT json AS Json FROM records WHERE id=?", id);
                var rec = rows.Count > 0 ? PoemJson.Deserialize<ChallengeRecord>(rows[0].Json) : null;
                return Task.FromResult(rec);
            }
        }

        public Task SaveAsync(ChallengeRecord record) { SaveSync(record); return Task.CompletedTask; }

        internal void SaveSync(ChallengeRecord r)
        {
            lock (_db.Gate)
            {
                _db.Conn.Execute(
                    "INSERT OR REPLACE INTO records(id,seq,completed_at_utc,mode,accuracy_percent," +
                    "correct_count,total,duration_seconds,best_streak,json) VALUES(?,?,?,?,?,?,?,?,?,?)",
                    r.Id, _db.NextSeq("records"), r.CompletedAtUtc, r.Mode, r.AccuracyPercent,
                    r.CorrectCount, r.Total, r.DurationSeconds, r.BestStreak, PoemJson.Serialize(r));
            }
        }

        public Task DeleteAsync(string id)
        {
            lock (_db.Gate) { _db.Conn.Execute("DELETE FROM records WHERE id=?", id); }
            return Task.CompletedTask;
        }
    }

    /// <summary>Favorites: newest-first by add order (seq DESC), no duplicates.</summary>
    public sealed class SqliteFavoriteRepository : IFavoriteRepository
    {
        private readonly SqliteUserDatabase _db;
        public SqliteFavoriteRepository(SqliteUserDatabase db) { _db = db; }

        public Task<IReadOnlyList<FavoriteEntry>> GetAllAsync()
        {
            lock (_db.Gate)
            {
                var rows = _db.Conn.Query<FavoriteRow>(
                    "SELECT poem_id AS PoemId, added_at_utc AS AddedAtUtc FROM favorites ORDER BY seq DESC");
                var list = new List<FavoriteEntry>(rows.Count);
                foreach (var r in rows) list.Add(new FavoriteEntry { PoemId = r.PoemId, AddedAtUtc = r.AddedAtUtc });
                return Task.FromResult((IReadOnlyList<FavoriteEntry>)list);
            }
        }

        public Task<bool> ExistsAsync(string poemId)
        {
            lock (_db.Gate)
            {
                int n = _db.Conn.ExecuteScalar<int>("SELECT COUNT(*) FROM favorites WHERE poem_id=?", poemId);
                return Task.FromResult(n > 0);
            }
        }

        public Task AddAsync(FavoriteEntry entry) { AddSync(entry); return Task.CompletedTask; }

        internal void AddSync(FavoriteEntry entry)
        {
            lock (_db.Gate)
            {
                int n = _db.Conn.ExecuteScalar<int>("SELECT COUNT(*) FROM favorites WHERE poem_id=?", entry.PoemId);
                if (n == 0)
                    _db.Conn.Execute("INSERT INTO favorites(poem_id,seq,added_at_utc) VALUES(?,?,?)",
                        entry.PoemId, _db.NextSeq("favorites"), entry.AddedAtUtc);
            }
        }

        public Task RemoveAsync(string poemId)
        {
            lock (_db.Gate) { _db.Conn.Execute("DELETE FROM favorites WHERE poem_id=?", poemId); }
            return Task.CompletedTask;
        }
    }

    /// <summary>错题本: Leitner entries; "due" is an indexed range scan on next_review_utc.</summary>
    public sealed class SqliteWrongBookRepository : IWrongBookRepository
    {
        private readonly SqliteUserDatabase _db;
        public SqliteWrongBookRepository(SqliteUserDatabase db) { _db = db; }

        private const string Cols =
            "question_id AS QuestionId, poem_id AS PoemId, box AS Box, next_review_utc AS NextReviewUtc, " +
            "last_result AS LastResult, added_at_utc AS AddedAtUtc, review_count AS ReviewCount";

        public Task<IReadOnlyList<WrongBookEntry>> GetAllAsync()
        {
            lock (_db.Gate) { return Task.FromResult(ToEntries(_db.Conn.Query<WrongRow>("SELECT " + Cols + " FROM wrongbook"))); }
        }

        public Task<IReadOnlyList<WrongBookEntry>> GetDueAsync(DateTime nowUtc)
        {
            // ISO-8601 round-trip strings sort lexicographically by instant (UTC, fixed width).
            string now = nowUtc.ToUniversalTime().ToString("o");
            lock (_db.Gate)
            {
                var rows = _db.Conn.Query<WrongRow>(
                    "SELECT " + Cols + " FROM wrongbook WHERE next_review_utc <= ? ORDER BY next_review_utc", now);
                return Task.FromResult(ToEntries(rows));
            }
        }

        public Task<WrongBookEntry> GetAsync(string questionId)
        {
            lock (_db.Gate)
            {
                var rows = _db.Conn.Query<WrongRow>("SELECT " + Cols + " FROM wrongbook WHERE question_id=?", questionId);
                return Task.FromResult(rows.Count > 0 ? ToEntry(rows[0]) : null);
            }
        }

        public Task UpsertAsync(WrongBookEntry entry) { UpsertSync(entry); return Task.CompletedTask; }

        internal void UpsertSync(WrongBookEntry e)
        {
            lock (_db.Gate)
            {
                _db.Conn.Execute(
                    "INSERT OR REPLACE INTO wrongbook(question_id,poem_id,box,next_review_utc,last_result,added_at_utc,review_count) " +
                    "VALUES(?,?,?,?,?,?,?)",
                    e.QuestionId, e.PoemId, e.Box, e.NextReviewUtc, e.LastResult, e.AddedAtUtc, e.ReviewCount);
            }
        }

        public Task RemoveAsync(string questionId)
        {
            lock (_db.Gate) { _db.Conn.Execute("DELETE FROM wrongbook WHERE question_id=?", questionId); }
            return Task.CompletedTask;
        }

        private static IReadOnlyList<WrongBookEntry> ToEntries(List<WrongRow> rows)
        {
            var list = new List<WrongBookEntry>(rows.Count);
            foreach (var r in rows) list.Add(ToEntry(r));
            return list;
        }
        private static WrongBookEntry ToEntry(WrongRow r) => new WrongBookEntry
        {
            QuestionId = r.QuestionId, PoemId = r.PoemId, Box = r.Box, NextReviewUtc = r.NextReviewUtc,
            LastResult = r.LastResult, AddedAtUtc = r.AddedAtUtc, ReviewCount = r.ReviewCount,
        };
    }

    /// <summary>Settings: single-row JSON blob (the model has many list fields; a blob is faithful).</summary>
    public sealed class SqliteSettingsStore : ISettingsStore
    {
        private readonly SqliteUserDatabase _db;
        public SqliteSettingsStore(SqliteUserDatabase db) { _db = db; }

        public Task<UserSettings> LoadAsync()
        {
            lock (_db.Gate)
            {
                var rows = _db.Conn.Query<JsonRow>("SELECT json AS Json FROM settings WHERE id=1");
                var s = rows.Count > 0 ? PoemJson.Deserialize<UserSettings>(rows[0].Json) : null;
                return Task.FromResult(s ?? new UserSettings());
            }
        }

        public Task SaveAsync(UserSettings settings) { SaveSync(settings); return Task.CompletedTask; }

        internal void SaveSync(UserSettings settings)
        {
            lock (_db.Gate)
            {
                _db.Conn.Execute("INSERT OR REPLACE INTO settings(id,json) VALUES(1,?)",
                    PoemJson.Serialize(settings ?? new UserSettings()));
            }
        }
    }

    /// <summary>Per-poem difficulty overrides (poemId → tier).</summary>
    public sealed class SqliteDifficultyOverrideStore : IDifficultyOverrideStore
    {
        private readonly SqliteUserDatabase _db;
        public SqliteDifficultyOverrideStore(SqliteUserDatabase db) { _db = db; }

        public Task<IReadOnlyDictionary<string, int>> LoadAsync()
        {
            lock (_db.Gate)
            {
                var dict = new Dictionary<string, int>();
                foreach (var r in _db.Conn.Query<OverrideRow>("SELECT poem_id AS PoemId, tier AS Tier FROM difficulty_overrides"))
                    dict[r.PoemId] = r.Tier;
                return Task.FromResult((IReadOnlyDictionary<string, int>)dict);
            }
        }

        public Task SaveAsync(IReadOnlyDictionary<string, int> overrides) { SaveSync(overrides); return Task.CompletedTask; }

        internal void SaveSync(IReadOnlyDictionary<string, int> overrides)
        {
            lock (_db.Gate)
            {
                _db.Conn.Execute("DELETE FROM difficulty_overrides");
                if (overrides != null)
                    foreach (var kv in overrides)
                        _db.Conn.Execute("INSERT OR REPLACE INTO difficulty_overrides(poem_id,tier) VALUES(?,?)", kv.Key, kv.Value);
            }
        }
    }
}
