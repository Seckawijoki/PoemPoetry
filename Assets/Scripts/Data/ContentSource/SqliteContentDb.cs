using System.Collections.Generic;
using SQLite4Unity3d;

namespace PoemPoetry.Data
{
    /// <summary>
    /// SQL query backend for the read-only content.db: pushes the pool FILTERING
    /// (dynasty / 体裁 / per-line difficulty / 挖空数) down to indexed SQL, so the work scales past
    /// what an in-memory scan comfortably handles. Object materialization and the procedural
    /// distractor ranking stay in <see cref="Services.ContentService"/>; this layer only answers
    /// "which ids / how many / which tiers" questions.
    ///
    /// Per-line difficulty depends on the poem's tier, which the user can OVERRIDE at runtime. Those
    /// overrides are mirrored into a per-connection TEMP table (<c>eff_override</c>) that the
    /// difficulty CASE reads, so SQL filtering stays consistent with the in-memory difficulty rules.
    /// </summary>
    public sealed class SqliteContentDb
    {
        private readonly SQLiteConnection _conn;

        public SqliteContentDb(string dbPath)
        {
            _conn = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite);
            _conn.Execute("CREATE TEMP TABLE IF NOT EXISTS eff_override(poem_id TEXT PRIMARY KEY, tier INTEGER)");
        }

        // ---- difficulty overrides (mirror ContentService's in-memory poem tiers) ----
        public void SyncOverrides(IReadOnlyDictionary<string, int> overrides)
        {
            _conn.Execute("DELETE FROM eff_override");
            if (overrides == null) return;
            foreach (var kv in overrides)
                _conn.Execute("INSERT OR REPLACE INTO eff_override(poem_id,tier) VALUES(?,?)", kv.Key, kv.Value);
        }

        public void SetOverride(string poemId, int tier) =>
            _conn.Execute("INSERT OR REPLACE INTO eff_override(poem_id,tier) VALUES(?,?)", poemId, tier);

        // Effective tier = override if present else the shipped poem tier.
        private const string Eff = "COALESCE(ov.tier, p.difficulty)";

        // Per-line difficulty, mirroring DifficultyRules.LineDifficulty (explicit per-line diff wins,
        // else derive from the effective poem tier + 名句 flag).
        private static readonly string LineDiff =
            "CASE WHEN pl.diff>=0 THEN pl.diff " +
            "WHEN " + Eff + "=0 THEN 0 " +
            "WHEN " + Eff + "=1 THEN (CASE WHEN pl.famous=1 THEN 1 ELSE 2 END) " +
            "WHEN " + Eff + "=2 THEN (CASE WHEN pl.famous=1 THEN 2 ELSE 3 END) " +
            "WHEN " + Eff + "=3 THEN 3 ELSE " + Eff + " END";

        private const string QFrom =
            "FROM questions q JOIN poems p ON p.id=q.poem_id " +
            "JOIN poem_lines pl ON pl.poem_id=q.poem_id AND pl.line_index=q.blank_line_index " +
            "LEFT JOIN eff_override ov ON ov.poem_id=q.poem_id";

        private const string WFrom = "FROM wordcloze_questions w JOIN poems p ON p.id=w.poem_id";

        // ---- question pool ----
        public int CountQuestionPool(ChallengeSettings s)
        {
            var (where, args) = QuestionWhere(s);
            return _conn.ExecuteScalar<int>("SELECT COUNT(*) " + QFrom + where, args.ToArray());
        }

        public List<string> QuestionPoolIds(ChallengeSettings s)
        {
            var (where, args) = QuestionWhere(s);
            return Ids(_conn.Query<IdRow>("SELECT q.id AS Id " + QFrom + where + " ORDER BY q.rowid", args.ToArray()));
        }

        public List<int> QuestionDifficultyTiers()
        {
            return Ints(_conn.Query<IntRow>(
                "SELECT DISTINCT (" + LineDiff + ") AS Val " + QFrom + " ORDER BY Val"));
        }

        private (string, List<object>) QuestionWhere(ChallengeSettings s)
        {
            var clauses = new List<string>();
            var args = new List<object>();
            AddIn(clauses, args, "p.dynasty", s?.Dynasties);
            AddIn(clauses, args, "p.type", s?.Types);
            AddInInts(clauses, args, "(" + LineDiff + ")", s?.Difficulties);
            return (clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "", args);
        }

        // ---- wordcloze pool (q.difficulty is a stored field; not override-coupled) ----
        public int CountWordClozePool(ChallengeSettings s, ICollection<int> blankCounts)
        {
            var (where, args) = WordClozeWhere(s, blankCounts);
            return _conn.ExecuteScalar<int>("SELECT COUNT(*) " + WFrom + where, args.ToArray());
        }

        public List<string> WordClozePoolIds(ChallengeSettings s, ICollection<int> blankCounts)
        {
            var (where, args) = WordClozeWhere(s, blankCounts);
            return Ids(_conn.Query<IdRow>("SELECT w.id AS Id " + WFrom + where + " ORDER BY w.rowid", args.ToArray()));
        }

        public List<int> WordClozeDifficultyTiers() =>
            Ints(_conn.Query<IntRow>("SELECT DISTINCT difficulty AS Val FROM wordcloze_questions ORDER BY Val"));

        public List<int> WordClozeBlankCounts() =>
            Ints(_conn.Query<IntRow>("SELECT DISTINCT blank_count AS Val FROM wordcloze_questions ORDER BY Val"));

        private (string, List<object>) WordClozeWhere(ChallengeSettings s, ICollection<int> blankCounts)
        {
            var clauses = new List<string>();
            var args = new List<object>();
            AddIn(clauses, args, "p.dynasty", s?.Dynasties);
            AddIn(clauses, args, "p.type", s?.Types);
            AddInInts(clauses, args, "w.difficulty", s != null ? s.Difficulties : null);
            AddInInts(clauses, args, "w.blank_count", blankCounts);
            return (clauses.Count > 0 ? " WHERE " + string.Join(" AND ", clauses) : "", args);
        }

        // ---- helpers ----
        private static void AddIn(List<string> clauses, List<object> args, string col, ICollection<string> values)
        {
            if (values == null || values.Count == 0) return;
            clauses.Add(col + " IN (" + Placeholders(values.Count) + ")");
            foreach (var v in values) args.Add(v);
        }

        private static void AddInInts(List<string> clauses, List<object> args, string expr, ICollection<int> values)
        {
            if (values == null || values.Count == 0) return;
            clauses.Add(expr + " IN (" + Placeholders(values.Count) + ")");
            foreach (var v in values) args.Add(v);
        }

        private static string Placeholders(int n)
        {
            var sb = new System.Text.StringBuilder(n * 2);
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append(','); sb.Append('?'); }
            return sb.ToString();
        }

        private static List<string> Ids(List<IdRow> rows)
        {
            var list = new List<string>(rows.Count);
            foreach (var r in rows) list.Add(r.Id);
            return list;
        }
        private static List<int> Ints(List<IntRow> rows)
        {
            var list = new List<int>(rows.Count);
            foreach (var r in rows) list.Add(r.Val);
            return list;
        }

        private class IdRow { public string Id { get; set; } }
        private class IntRow { public int Val { get; set; } }
    }
}
