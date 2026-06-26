using System.Collections.Generic;
using System.Threading.Tasks;
using SQLite4Unity3d;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Loads shipped content from the compiled <c>content.db</c> (built offline by
    /// Tools/ChinesePoetryImport/build_db.py). Drop-in replacement for <see cref="JsonContentSource"/>:
    /// it returns the exact same domain shapes, so <see cref="Services.ContentService"/> and
    /// <see cref="Services.RhymeService"/> work unchanged. Query-pushdown (filtering/凑题 in SQL)
    /// is layered on top of this equivalence step, not baked into it.
    ///
    /// Pure C# (no UnityEngine): the App layer hands us an absolute db path it has already
    /// provisioned into a readable location (persistentDataPath on device).
    /// </summary>
    public sealed class SqliteContentSource : IContentSource
    {
        private readonly string _dbPath;
        private SQLiteConnection _conn;

        public SqliteContentSource(string dbPath) { _dbPath = dbPath; }

        private SQLiteConnection Conn =>
            _conn ?? (_conn = new SQLiteConnection(_dbPath, SQLiteOpenFlags.ReadOnly));

        // ---- flat row DTOs (sqlite-net maps result columns to public properties by name) ----
        private class PoemRow
        {
            public string Id { get; set; }
            public string Dynasty { get; set; }
            public string Author { get; set; }
            public string Title { get; set; }
            public string Type { get; set; }
            public string Cipai { get; set; }
            public string Fame { get; set; }
            public int Difficulty { get; set; }
            public string Source { get; set; }
            public string Translation { get; set; }
            public string Appreciation { get; set; }
            public string TagsJson { get; set; }
        }

        private class LineRow
        {
            public string PoemId { get; set; }
            public int LineIndex { get; set; }
            public string Text { get; set; }
            public int CharCount { get; set; }
            public string LastChar { get; set; }
            public string RhymeFinal { get; set; }
            public string RhymeGroup { get; set; }
            public string PingshuiRhyme { get; set; }
            public string Tone { get; set; }
            public int IsRhymeLine { get; set; }
            public string PosPattern { get; set; }
            public int CoupletPartnerIndex { get; set; }
            public int Grp { get; set; }
            public int Famous { get; set; }
            public int Diff { get; set; }
        }

        private class ClusterRow
        {
            public int Id { get; set; }
            public int CharCount { get; set; }
            public string RhymeGroup { get; set; }
            public string ToneType { get; set; }
        }

        private class ClusterLineRow
        {
            public int ClusterId { get; set; }
            public string Text { get; set; }
            public int CharCount { get; set; }
            public string LastChar { get; set; }
            public string RhymeFinal { get; set; }
            public string RhymeGroup { get; set; }
            public string Pingshui { get; set; }
            public string PosPattern { get; set; }
            public string SourcePoemId { get; set; }
        }

        private class QuestionRow
        {
            public string Id { get; set; }
            public string PoemId { get; set; }
            public int BlankLineIndex { get; set; }
            public int ClusterId { get; set; }
            public string CorrectText { get; set; }
            public int CorrectCharCount { get; set; }
            public string CorrectLastChar { get; set; }
            public string CorrectRhymeFinal { get; set; }
            public string CorrectRhymeGroup { get; set; }
            public string CorrectPingshui { get; set; }
            public string CorrectPosPattern { get; set; }
            public string CorrectSourcePoemId { get; set; }
            public int Difficulty { get; set; }
            public string Explanation { get; set; }
            public string SourceMode { get; set; }
        }

        private class WordClozeRow
        {
            public string Id { get; set; }
            public string PoemId { get; set; }
            public int BlankLineIndex { get; set; }
            public string LineIndicesJson { get; set; }
            public string TilePoolJson { get; set; }
            public int Difficulty { get; set; }
        }

        private class BlankRow
        {
            public string QuestionId { get; set; }
            public int LineIndex { get; set; }
            public int Start { get; set; }
            public int Count { get; set; }
            public string AnswerCharsJson { get; set; }
            public string Pos { get; set; }
            public string Semantic { get; set; }
        }

        private class KvRow { public string K { get; set; } public string V { get; set; } }

        // ---- IContentSource ----

        public Task<IReadOnlyList<Poem>> LoadPoemsAsync()
        {
            var byId = new Dictionary<string, Poem>();
            var poems = new List<Poem>();
            foreach (var r in Conn.Query<PoemRow>(
                "SELECT id AS Id, dynasty AS Dynasty, author AS Author, title AS Title, type AS Type, " +
                "cipai AS Cipai, fame AS Fame, difficulty AS Difficulty, source AS Source, " +
                "translation AS Translation, appreciation AS Appreciation, tags_json AS TagsJson FROM poems"))
            {
                var p = new Poem
                {
                    Id = r.Id, Dynasty = r.Dynasty, Author = r.Author, Title = r.Title, Type = r.Type,
                    Cipai = r.Cipai, Fame = r.Fame, Difficulty = r.Difficulty, Source = r.Source,
                    Translation = r.Translation, Appreciation = r.Appreciation,
                    Tags = ParseStringList(r.TagsJson),
                    Lines = new List<PoemLine>(),
                };
                byId[p.Id] = p;
                poems.Add(p);
            }

            foreach (var r in Conn.Query<LineRow>(
                "SELECT poem_id AS PoemId, line_index AS LineIndex, text AS Text, char_count AS CharCount, " +
                "last_char AS LastChar, rhyme_final AS RhymeFinal, rhyme_group AS RhymeGroup, " +
                "pingshui_rhyme AS PingshuiRhyme, tone AS Tone, is_rhyme_line AS IsRhymeLine, " +
                "pos_pattern AS PosPattern, couplet_partner_index AS CoupletPartnerIndex, grp AS Grp, " +
                "famous AS Famous, diff AS Diff FROM poem_lines ORDER BY poem_id, line_index"))
            {
                if (!byId.TryGetValue(r.PoemId, out var p)) continue;
                p.Lines.Add(new PoemLine
                {
                    Text = r.Text, CharCount = r.CharCount, LastChar = r.LastChar, RhymeFinal = r.RhymeFinal,
                    RhymeGroup = r.RhymeGroup, PingshuiRhyme = r.PingshuiRhyme, Tone = r.Tone,
                    IsRhymeLine = r.IsRhymeLine != 0, PosPattern = r.PosPattern,
                    CoupletPartnerIndex = r.CoupletPartnerIndex, Group = r.Grp, Famous = r.Famous != 0, Diff = r.Diff,
                });
            }
            return Task.FromResult((IReadOnlyList<Poem>)poems);
        }

        public Task<QuestionFile> LoadQuestionBankAsync()
        {
            var file = new QuestionFile { SchemaVersion = 2, Clusters = new List<LineCluster>(), Questions = new List<Question>() };
            var clusterById = new Dictionary<int, LineCluster>();
            foreach (var r in Conn.Query<ClusterRow>(
                "SELECT id AS Id, char_count AS CharCount, rhyme_group AS RhymeGroup, tone_type AS ToneType FROM clusters"))
            {
                var c = new LineCluster { Id = r.Id, CharCount = r.CharCount, RhymeGroup = r.RhymeGroup, ToneType = r.ToneType, Lines = new List<QuestionOption>() };
                clusterById[c.Id] = c;
                file.Clusters.Add(c);
            }
            foreach (var r in Conn.Query<ClusterLineRow>(
                "SELECT cluster_id AS ClusterId, text AS Text, char_count AS CharCount, last_char AS LastChar, " +
                "rhyme_final AS RhymeFinal, rhyme_group AS RhymeGroup, pingshui AS Pingshui, " +
                "pos_pattern AS PosPattern, source_poem_id AS SourcePoemId FROM cluster_lines ORDER BY cluster_id, idx"))
            {
                if (!clusterById.TryGetValue(r.ClusterId, out var c)) continue;
                c.Lines.Add(new QuestionOption
                {
                    Text = r.Text, CharCount = r.CharCount, LastChar = r.LastChar, RhymeFinal = r.RhymeFinal,
                    RhymeGroup = r.RhymeGroup, Pingshui = r.Pingshui, PosPattern = r.PosPattern, SourcePoemId = r.SourcePoemId,
                });
            }
            foreach (var r in Conn.Query<QuestionRow>(
                "SELECT id AS Id, poem_id AS PoemId, blank_line_index AS BlankLineIndex, cluster_id AS ClusterId, " +
                "correct_text AS CorrectText, correct_char_count AS CorrectCharCount, correct_last_char AS CorrectLastChar, " +
                "correct_rhyme_final AS CorrectRhymeFinal, correct_rhyme_group AS CorrectRhymeGroup, " +
                "correct_pingshui AS CorrectPingshui, correct_pos_pattern AS CorrectPosPattern, " +
                "correct_source_poem_id AS CorrectSourcePoemId, difficulty AS Difficulty, " +
                "explanation AS Explanation, source_mode AS SourceMode FROM questions"))
            {
                file.Questions.Add(new Question
                {
                    Id = r.Id, PoemId = r.PoemId, BlankLineIndex = r.BlankLineIndex, ClusterId = r.ClusterId,
                    Difficulty = r.Difficulty, Explanation = r.Explanation, SourceMode = r.SourceMode,
                    Correct = new QuestionOption
                    {
                        Text = r.CorrectText, CharCount = r.CorrectCharCount, LastChar = r.CorrectLastChar,
                        RhymeFinal = r.CorrectRhymeFinal, RhymeGroup = r.CorrectRhymeGroup, Pingshui = r.CorrectPingshui,
                        PosPattern = r.CorrectPosPattern, SourcePoemId = r.CorrectSourcePoemId,
                    },
                });
            }
            return Task.FromResult(file);
        }

        public Task<IReadOnlyList<WordClozeQuestion>> LoadWordClozeQuestionsAsync()
        {
            var byId = new Dictionary<string, WordClozeQuestion>();
            var list = new List<WordClozeQuestion>();
            foreach (var r in Conn.Query<WordClozeRow>(
                "SELECT id AS Id, poem_id AS PoemId, blank_line_index AS BlankLineIndex, " +
                "line_indices_json AS LineIndicesJson, tile_pool_json AS TilePoolJson, difficulty AS Difficulty " +
                "FROM wordcloze_questions"))
            {
                var w = new WordClozeQuestion
                {
                    Id = r.Id, PoemId = r.PoemId, BlankLineIndex = r.BlankLineIndex,
                    LineIndices = ParseIntList(r.LineIndicesJson), TilePool = ParseStringList(r.TilePoolJson),
                    Difficulty = r.Difficulty, Blanks = new List<WordClozeBlank>(),
                };
                byId[w.Id] = w;
                list.Add(w);
            }
            foreach (var r in Conn.Query<BlankRow>(
                "SELECT question_id AS QuestionId, line_index AS LineIndex, start AS Start, count AS Count, " +
                "answer_chars_json AS AnswerCharsJson, pos AS Pos, semantic AS Semantic " +
                "FROM wordcloze_blanks ORDER BY question_id, blank_index"))
            {
                if (!byId.TryGetValue(r.QuestionId, out var w)) continue;
                w.Blanks.Add(new WordClozeBlank
                {
                    LineIndex = r.LineIndex, Start = r.Start, Count = r.Count,
                    AnswerChars = ParseStringList(r.AnswerCharsJson), Pos = r.Pos, Semantic = r.Semantic,
                });
            }
            return Task.FromResult((IReadOnlyList<WordClozeQuestion>)list);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCharPinyinAsync()
        {
            var result = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var r in Conn.Query<KvRow>("SELECT char AS K, pinyins_json AS V FROM char_pinyin"))
                result[r.K] = ParseStringList(r.V);
            return Task.FromResult((IReadOnlyDictionary<string, IReadOnlyList<string>>)result);
        }

        public Task<IReadOnlyDictionary<string, string>> LoadRhymeGroupsAsync()
        {
            var result = new Dictionary<string, string>();
            foreach (var r in Conn.Query<KvRow>("SELECT final AS K, group_id AS V FROM rhyme_groups"))
                result[r.K] = r.V;
            return Task.FromResult((IReadOnlyDictionary<string, string>)result);
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadPingshuiRhymeAsync()
        {
            var result = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var r in Conn.Query<KvRow>("SELECT char AS K, ids_json AS V FROM pingshui_rhyme"))
                result[r.K] = ParseStringList(r.V);
            return Task.FromResult((IReadOnlyDictionary<string, IReadOnlyList<string>>)result);
        }

        private static List<string> ParseStringList(string json) =>
            string.IsNullOrEmpty(json) ? new List<string>() : (PoemJson.Deserialize<List<string>>(json) ?? new List<string>());

        private static List<int> ParseIntList(string json) =>
            string.IsNullOrEmpty(json) ? new List<int>() : (PoemJson.Deserialize<List<int>>(json) ?? new List<int>());
    }
}
