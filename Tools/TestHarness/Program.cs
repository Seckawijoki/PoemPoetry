using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PoemPoetry.Core;
using PoemPoetry.Data;
using PoemPoetry.Services;

// Standalone verifier for the UnityEngine-free core. Doubles as the offline content
// pipeline: it annotates the seed poems, generates questions, and writes the shipped
// StreamingAssets JSON, then asserts every pure-logic layer. Run via build_and_test.ps1.
internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

    private static int Main()
    {
        try
        {
            return MainAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var detail = ex.ToString();
            try { File.WriteAllText(@"f:\UnityProjects\PoemPoetry\Tools\TestHarness\error.log", detail, Utf8NoBom); }
            catch { }
            Console.WriteLine("EXCEPTION: " + ex.GetType().FullName);
            // ASCII-safe message so the CLR's emergency printer never chokes on CJK.
            var sb = new StringBuilder();
            foreach (var c in ex.Message) sb.Append(c < 128 ? c : '?');
            Console.WriteLine("MESSAGE: " + sb);
            Console.WriteLine("(full detail written to Tools\\TestHarness\\error.log)");
            return 99;
        }
    }

    private static async Task<int> MainAsync()
    {
        try { Console.OutputEncoding = Encoding.UTF8; } catch { /* redirected console */ }

        string root = @"f:\UnityProjects\PoemPoetry";
        string buildDataDir = Path.Combine(root, @"Tools\SampleContent"); // build-time inputs/outputs (NOT shipped)
        string seedPath = Path.Combine(buildDataDir, "poems_seed.json");
        string saDir = Path.Combine(root, @"Assets\StreamingAssets");
        string dataDir = Path.Combine(saDir, "PoemData");                 // runtime-loaded, shipped assets
        Directory.CreateDirectory(dataDir);

        // ---- 1. Unit tests that don't need content ----
        Section("PinyinRhyme.Final");
        CheckEq(PinyinRhyme.Final("xiang1"), "iang", "xiang -> iang");
        CheckEq(PinyinRhyme.Final("guang1"), "uang", "guang -> uang");
        CheckEq(PinyinRhyme.Final("zhi1"), "i_buzz", "zhi -> i_buzz (buzzing i)");
        CheckEq(PinyinRhyme.Final("ji2"), "i", "ji -> i (front i)");
        CheckEq(PinyinRhyme.Final("yue4"), "üe", "yue -> üe");
        CheckEq(PinyinRhyme.Final("jian1"), "ian", "jian -> ian");
        CheckEq(PinyinRhyme.Final("jun1"), "ün", "jun -> ün (j+u = ü)");
        CheckEq(PinyinRhyme.Final("xue3"), "üe", "xue -> üe");
        CheckEq(PinyinRhyme.Final("wu3"), "u", "wu -> u");
        CheckEq(PinyinRhyme.Final("yi1"), "i", "yi -> i");
        CheckEq(PinyinRhyme.Final("you2"), "iou", "you -> iou");
        CheckEq(PinyinRhyme.Final("lv4"), "ü", "lv -> ü");
        CheckEq(PinyinRhyme.Final("er2"), "er", "er -> er");
        CheckEq(PinyinRhyme.GroupOf("iang"), "10", "iang -> group 10");
        CheckEq(PinyinRhyme.GroupOf("i_buzz"), "13", "i_buzz -> group 13");
        CheckEq(PinyinRhyme.GroupOf("üe"), "3", "üe -> group 3");

        Section("ScoreMath");
        CheckEq(ScoreMath.AccuracyPercent(7, 10), 70, "7/10 = 70%");
        CheckEq(ScoreMath.AccuracyPercent(1, 3), 33, "1/3 = 33%");
        CheckEq(ScoreMath.AccuracyPercent(2, 3), 67, "2/3 = 67% (away-from-zero)");
        CheckEq(ScoreMath.AccuracyPercent(0, 5), 0, "0/5 = 0%");
        CheckEq(ScoreMath.AccuracyPercent(5, 5), 100, "5/5 = 100%");
        CheckEq(ScoreMath.AccuracyPercent(0, 0), 0, "0/0 = 0% (no divide-by-zero)");
        CheckEq(ScoreMath.BestStreak(new[] { true, true, false, true, true, true, false }), 3, "best streak = 3");
        CheckEq(ScoreMath.BestStreak(new bool[0]), 0, "empty streak = 0");

        // ---- 2. Build content: read seed, annotate, generate, write StreamingAssets ----
        Section("Content pipeline (annotate + generate + write)");
        var seed = PoemJson.Deserialize<PoemFile>(File.ReadAllText(seedPath, Encoding.UTF8));
        Check(seed != null && seed.Poems.Count >= 60, $"seed has the expanded library ({seed?.Poems.Count} poems)");

        // rhyme_groups.json (from the built-in default map) must exist before RhymeService loads.
        var rgFile = new RhymeGroupFile();
        foreach (var kv in PinyinRhyme.DefaultGroupMap) rgFile.Groups[kv.Key] = kv.Value;
        Write(Path.Combine(dataDir, "rhyme_groups.json"), rgFile);

        var bootSource = new JsonContentSource(new FileRawTextLoader(saDir));
        var rhyme = await RhymeService.LoadAsync(bootSource);

        // Annotate each poem (two passes: rhyme-group + 平水韵 context disambiguates 多音字).
        foreach (var poem in seed.Poems)
        {
            foreach (var line in poem.Lines) rhyme.Annotate(line);
            var groups = new HashSet<string>();
            var pingshui = new HashSet<string>();
            foreach (var line in poem.Lines)
                if (line.IsRhymeLine)
                {
                    if (!string.IsNullOrEmpty(line.RhymeGroup)) groups.Add(line.RhymeGroup);
                    if (!string.IsNullOrEmpty(line.PingshuiRhyme)) pingshui.Add(line.PingshuiRhyme);
                }
            foreach (var line in poem.Lines)
                if (line.IsRhymeLine) rhyme.Annotate(line, groups, pingshui);
        }

        // Spot-check annotations.
        var jingyesi = FindPoem(seed, "tang-libai-jingyesi");
        CheckEq(jingyesi.Lines[0].CharCount, 5, "静夜思 line is 5 chars");
        CheckEq(jingyesi.Lines[0].RhymeGroup, "10", "床前明月光 -> group 10");
        CheckEq(jingyesi.Lines[3].RhymeGroup, "10", "低头思故乡 -> group 10");
        var zaofa = FindPoem(seed, "tang-libai-zaofabaidicheng");
        CheckEq(zaofa.Lines[0].CharCount, 7, "早发白帝城 line is 7 chars");
        CheckEq(zaofa.Lines[0].RhymeGroup, "8", "朝辞白帝彩云间 -> group 8");
        CheckEq(zaofa.Lines[1].RhymeGroup, "8", "千里江陵一日还 -> group 8 (还=huán in context)");
        // 平水韵 (Part B): finer than 新韵 — 光/乡 both 下平七阳; 间/还 both 上平十五删 (多音 间 disambiguated to 删).
        CheckEq(jingyesi.Lines[0].PingshuiRhyme, "P22阳", "床前明月光 -> 平水韵 七阳");
        CheckEq(jingyesi.Lines[3].PingshuiRhyme, "P22阳", "低头思故乡 -> 平水韵 七阳 (同韵部)");
        CheckEq(zaofa.Lines[1].PingshuiRhyme, "P15删", "千里江陵一日还 -> 平水韵 十五删");
        CheckEq(zaofa.Lines[0].PingshuiRhyme, "P15删", "朝辞白帝彩云间 -> 十五删 (间 多音, context 消歧)");
        var xiangsi = FindPoem(seed, "tang-wangwei-xiangsi");
        CheckEq(xiangsi.Lines[1].RhymeGroup, "13", "春来发几枝 -> group 13");

        // ToneService (平仄) — needed for 平仄型 clustering and reused by the wordcloze pipeline below.
        var charPinyin = await bootSource.LoadCharPinyinAsync();
        var tone = new ToneService(charPinyin);

        // Generate the v2 bank: cluster every corpus line by (字数,韵组,平仄型) into a shared 干扰项池,
        // then emit one lightweight question per blankable 韵脚句 (诗) / 任意句 (词/曲).
        var gen = new QuestionGenerator(seed.Poems, new SystemRandomSource(20260622));
        var bank = gen.BuildBank(seed.Poems, tone, rhymeLinesOnlyForShi: true);
        var questions = bank.Questions;

        Check(questions.Count >= 150, $"large question bank generated, got {questions.Count}");
        Check(bank.Clusters.Count > 0, $"shared distractor clusters built ({bank.Clusters.Count})");
        var qPoemIds = new HashSet<string>();
        foreach (var q in questions) qPoemIds.Add(q.PoemId);
        Check(qPoemIds.Contains(jingyesi.Id), "静夜思 generates (richer library = cross-poem distractors)");
        var jianjia = FindPoem(seed, "xianqin-shijing-jianjia");
        CheckEq(jianjia.Lines[0].CharCount, 4, "蒹葭苍苍 is 4 chars (四言)");
        CheckEq(jianjia.Lines[0].RhymeGroup, "10", "蒹葭苍苍 -> group 10");
        Check(qPoemIds.Contains(jianjia.Id), "蒹葭 generates 四言 questions (cross 短歌行)");

        // 韵脚覆盖率回归：char_pinyin 须覆盖所有韵脚字，否则整首诗的韵脚句拿不到 RhymeGroup → 漏题
        // (曾因字典只有 605 字、缺 70% 语料字，漏掉 元日/忆江南/芙蓉楼/寄扬州/塞下曲/贾生 等 6 首)。
        int rhymeNoGroup = 0, rhymeTotal = 0;
        foreach (var poem in seed.Poems)
            foreach (var l in poem.Lines)
                if (l.IsRhymeLine) { rhymeTotal++; if (string.IsNullOrEmpty(l.RhymeGroup)) rhymeNoGroup++; }
        Check(rhymeNoGroup == 0, $"每个韵脚都解析出新韵韵组 ({rhymeTotal - rhymeNoGroup}/{rhymeTotal})；字典缺字会整首漏题");
        foreach (var title in new[] { "元日", "芙蓉楼送辛渐", "寄扬州韩绰判官", "贾生" })
        {
            Poem hit = null;
            foreach (var p in seed.Poems) if ((p.Title ?? "").Contains(title)) { hit = p; break; }
            Check(hit != null && qPoemIds.Contains(hit.Id), $"《{title}》 现在能出题（曾因缺字漏题）");
        }

        // Cluster consistency: every member shares the cluster's 字数/韵组/平仄型, deduped.
        bool clustersOk = true;
        int maxClusterLines = 0;
        foreach (var c in bank.Clusters)
        {
            var seenC = new HashSet<string>();
            maxClusterLines = System.Math.Max(maxClusterLines, c.Lines.Count);
            foreach (var ln in c.Lines)
            {
                if (ln.CharCount != c.CharCount || ln.RhymeGroup != c.RhymeGroup) clustersOk = false;
                if (tone.ToneType(ln.Text) != c.ToneType) clustersOk = false;
                if (!seenC.Add(ln.Text)) clustersOk = false;
            }
        }
        Check(clustersOk, "every cluster: members share 字数/韵组/平仄型, deduped");
        Console.WriteLine($"  簇数={bank.Clusters.Count}，最大簇={maxClusterLines} 句，题/簇均摊={(double)questions.Count / bank.Clusters.Count:F1}");

        // Runtime distractor validation: fill each question's pool from its cluster via ContentService.
        var vContent = new ContentService(seed.Poems, questions, null, bank.Clusters);
        var vRng = new SystemRandomSource(123);
        bool allGood = true;
        int psBoth = 0, psSame = 0;     // 平水韵同韵部占比
        int ttTotal = 0, ttSame = 0;    // 同平仄型占比
        foreach (var q in questions)
        {
            vContent.PopulateDistractors(q, int.MaxValue, vRng);
            var poem = FindPoem(seed, q.PoemId);
            var target = poem.Lines[q.BlankLineIndex];
            string tt = tone.ToneType(target.Text);
            if (q.Distractors.Count < 3 || q.Distractors.Count > 20) allGood = false;  // pool size 3..20
            var seenTexts = new HashSet<string> { q.Correct.Text };
            foreach (var d in q.Distractors)
            {
                if (d.CharCount != target.CharCount) allGood = false;      // same 字数
                if (d.RhymeGroup != target.RhymeGroup) allGood = false;    // same 韵组
                if (d.SourcePoemId == q.PoemId) allGood = false;           // different poem
                if (!seenTexts.Add(d.Text)) allGood = false;               // distinct, != correct
                ttTotal++; if (tone.ToneType(d.Text) == tt) ttSame++;
                if (!string.IsNullOrEmpty(d.Pingshui) && !string.IsNullOrEmpty(target.PingshuiRhyme))
                { psBoth++; if (d.Pingshui == target.PingshuiRhyme) psSame++; }
            }
        }
        Check(allGood, "runtime distractor pools: 3..20 each, same 字数, same 韵组, different poem, distinct");
        double psRate = psBoth > 0 ? (double)psSame / psBoth : 0;
        double ttRate = ttTotal > 0 ? (double)ttSame / ttTotal : 0;
        Console.WriteLine($"  平水韵同韵部干扰项占比: {psSame}/{psBoth} = {psRate:P0}; 同平仄型干扰项占比: {ttSame}/{ttTotal} = {ttRate:P0}");
        Check(psBoth > 0 && psRate > 0.30, $"平水韵打分生效：多数干扰项也同平水韵部 ({psRate:P0})");
        Check(ttRate > 0.80, $"干扰项绝大多数同平仄型（簇内优先）({ttRate:P0})");

        // Write shipped content (v2 bank: clusters + lightweight questions).
        Write(Path.Combine(dataDir, "poems.json"), new PoemFile { Poems = seed.Poems });
        Write(Path.Combine(dataDir, "questions.json"), bank);
        Console.WriteLine("  wrote poems.json / questions.json / rhyme_groups.json to StreamingAssets");

        // ---- 2b. 逐词填空 (残句调控): enrich word bank, generate tile-cloze questions ----
        Section("WordCloze pipeline (enrich bank + generate + write)");

        var catFile = PoemJson.Deserialize<SemanticCategoryFile>(
            File.ReadAllText(Path.Combine(buildDataDir, "semantic_categories.json"), Encoding.UTF8));
        Check(catFile != null && catFile.Categories.Count >= 5, $"semantic categories loaded ({catFile?.Categories.Count})");

        var bankSeed = PoemJson.Deserialize<WordBankFile>(
            File.ReadAllText(Path.Combine(buildDataDir, "word_bank_seed.json"), Encoding.UTF8));
        Check(bankSeed != null && bankSeed.Words.Count >= 40, $"word bank seed loaded ({bankSeed?.Words.Count} words)");

        // Enrich each word: per-char primary pinyin, 平仄 string, and source lines from the corpus.
        foreach (var w in bankSeed.Words)
        {
            w.CharCount = new System.Globalization.StringInfo(w.Text).LengthInTextElements;
            w.Tone = tone.ToneString(w.Text);
            w.Pinyin = new List<string>();
            foreach (var ch in SplitToChars(w.Text))
                w.Pinyin.Add(charPinyin.TryGetValue(ch, out var rs) && rs.Count > 0 ? rs[0] : "");
            w.Sources = new List<string>();
            foreach (var p in seed.Poems)
                foreach (var line in p.Lines)
                    if (line.Text.Contains(w.Text) && w.Sources.Count < 8) w.Sources.Add(line.Text);
        }
        Write(Path.Combine(buildDataDir, "word_bank.json"), bankSeed); // enriched bank: inspection artifact, not shipped/loaded

        var wcGen = new WordClozeGenerator(seed.Poems, bankSeed.Words, catFile.Categories, tone,
            new SystemRandomSource(20260624));
        var wcQuestions = new List<WordClozeQuestion>();
        foreach (var poem in seed.Poems)
            wcQuestions.AddRange(wcGen.GenerateForPoem(poem));
        Check(wcQuestions.Count >= 100, $"wordcloze bank generated ({wcQuestions.Count} questions)");
        int n1 = 0, n2 = 0, n3 = 0, n4 = 0;
        foreach (var q in wcQuestions) { int bc = q.Blanks.Count; if (bc == 1) n1++; else if (bc == 2) n2++; else if (bc == 3) n3++; else n4++; }
        Console.WriteLine($"  挖空数分布: 1空={n1} 2空={n2} 3空={n3} 4空={n4}");
        Check(n1 > 0 && n2 > 0, "bank covers both 单空 and 多空 shapes (挖空数 selector is meaningful)");

        // Validate every generated wordcloze question (shapes: 1/2/3 空, 1~3 句).
        bool wcGood = true;
        foreach (var q in wcQuestions)
        {
            var poem = FindPoem(seed, q.PoemId);
            if (q.Blanks.Count < 1 || q.Blanks.Count > 4) wcGood = false;
            // Shown lines: explicit LineIndices, else the single BlankLineIndex.
            var shown = new HashSet<int>(q.LineIndices != null && q.LineIndices.Count > 0
                ? q.LineIndices : new List<int> { q.BlankLineIndex });
            int lastLine = -1, prevEnd = -1;
            var answerSeq = new List<string>();
            foreach (var b in q.Blanks)
            {
                if (!shown.Contains(b.LineIndex)) wcGood = false;
                if (b.LineIndex < 0 || b.LineIndex >= poem.Lines.Count) { wcGood = false; continue; }
                int lineLen = new System.Globalization.StringInfo(poem.Lines[b.LineIndex].Text).LengthInTextElements;
                if (b.Count < 1 || b.Count > 3) wcGood = false;                     // 1~3-char blanks
                if (b.Count == 1 && b.Pos != "v" && b.Pos != "p") wcGood = false;   // 单字空只能 动词/介词
                if (b.Start < 0 || b.Start + b.Count > lineLen) wcGood = false;     // in-bounds of its own line
                if (b.LineIndex < lastLine) wcGood = false;                         // blanks ordered by (line, start)
                if (b.LineIndex != lastLine) prevEnd = -1;                          // reset per line
                if (b.Start <= prevEnd) wcGood = false;                             // non-overlapping within line
                prevEnd = b.Start + b.Count - 1; lastLine = b.LineIndex;
                if (b.AnswerChars.Count != b.Count) wcGood = false;
                answerSeq.AddRange(b.AnswerChars);
            }
            // pool covers every answer char, has distractors, and fills a full rows×cols grid.
            var poolCount = new Dictionary<string, int>();
            foreach (var t in q.TilePool) { poolCount.TryGetValue(t, out var c); poolCount[t] = c + 1; }
            foreach (var a in answerSeq) { if (!poolCount.TryGetValue(a, out var c) || c < 1) wcGood = false; }
            if (q.TilePool.Count <= answerSeq.Count) wcGood = false;
            int pn = q.TilePool.Count, prows = WordClozeGenerator.GridRows(pn), pcols = (pn + prows - 1) / prows;
            if (pn % prows != 0 || pcols > WordClozeGenerator.MaxGridCols) wcGood = false; // full ≤8-col rectangle
        }
        Check(wcGood, "all wordcloze: 1~3 空 (单字空仅 动词/介词), in-bounds & non-overlapping, pool covers answers + full grid");
        Write(Path.Combine(dataDir, "word_questions.json"), new WordClozeQuestionFile { Questions = wcQuestions });
        Console.WriteLine("  wrote word_questions.json to StreamingAssets; word_bank.json to Tools/SampleContent (build-only)");

        // ---- 3. End-to-end content load through the real shipped files ----
        Section("Content load (JsonContentSource over shipped files)");
        var content = await ContentService.LoadAsync(bootSource);
        Check(content.Poems.Count >= 60, $"loaded the expanded library ({content.Poems.Count} poems)");
        Check(content.QuestionCount >= 21, $"loaded questions ({content.QuestionCount})");
        Check(content.WordClozeCount >= 100, $"loaded wordcloze questions ({content.WordClozeCount})");
        Check(content.GetWordClozePool(new ChallengeSettings()).Count == content.WordClozeCount,
            "unfiltered wordcloze pool == full bank");
        var wcCounts = content.GetWordClozeBlankCounts();
        Check(wcCounts.Contains(1) && wcCounts.Contains(2), $"挖空数 present: [{string.Join(",", wcCounts)}]");
        int onlyOne = content.GetWordClozePool(new ChallengeSettings(), new List<int> { 1 }).Count;
        int onlyTwo = content.GetWordClozePool(new ChallengeSettings(), new List<int> { 2 }).Count;
        Check(onlyOne > 0 && onlyTwo > 0 && onlyOne + onlyTwo <= content.WordClozeCount,
            $"挖空数 filter partitions pool (1空={onlyOne}, 2空={onlyTwo})");
        Check(content.GetPoem("tang-libai-jingyesi") != null, "GetPoem resolves by id");
        var rhyme2 = await RhymeService.LoadAsync(bootSource);
        CheckEq(rhyme2.GroupForChar("间"), "8", "reloaded rhyme: 间 -> 8");

        // ---- 3b. SqliteContentSource parity (content.db must mirror the JSON exactly) ----
        // content.db is compiled from the JSON by Tools/ChinesePoetryImport/build_db.py. If this
        // section fails, rebuild it: `python Tools/ChinesePoetryImport/build_db.py`.
        Section("SqliteContentSource parity (DB vs JSON)");
        var dbPath = Path.Combine(dataDir, "content.db");
        if (!File.Exists(dbPath))
        {
            Console.WriteLine("  SKIP: content.db not found — run `python Tools/ChinesePoetryImport/build_db.py`.");
        }
        else
        {
            var sqlSource = new SqliteContentSource(dbPath);
            var dbContent = await ContentService.LoadAsync(sqlSource);
            CheckEq(dbContent.Poems.Count, content.Poems.Count, "DB poem count == JSON");
            CheckEq(dbContent.QuestionCount, content.QuestionCount, "DB question count == JSON");
            CheckEq(dbContent.WordClozeCount, content.WordClozeCount, "DB wordcloze count == JSON");
            CheckEq(string.Join(",", dbContent.GetDynasties()), string.Join(",", content.GetDynasties()), "DB dynasties == JSON");
            CheckEq(string.Join(",", dbContent.GetTypes()), string.Join(",", content.GetTypes()), "DB types == JSON");
            CheckEq(string.Join(",", dbContent.GetDifficultyTiers()), string.Join(",", content.GetDifficultyTiers()), "DB difficulty tiers == JSON");

            // Field-level spot check: 静夜思 round-trips through the DB.
            var jp = dbContent.GetPoem("tang-libai-jingyesi");
            Check(jp != null, "DB GetPoem(静夜思) resolves");
            CheckEq(jp.Lines.Count, 4, "DB 静夜思 has 4 lines");
            CheckEq(jp.Lines[0].Text, "床前明月光", "DB 静夜思 line0 text");
            CheckEq(jp.Lines[0].RhymeGroup, "10", "DB 静夜思 line0 韵组");
            CheckEq(jp.Lines[0].PingshuiRhyme, "P22阳", "DB 静夜思 line0 平水韵");
            Check(jp.Lines[0].Famous, "DB 静夜思 line0 名句 flag preserved");

            // Clusters loaded → runtime distractor pools still satisfy the hard constraints.
            var dbRt = dbContent.BuildRuntimeQuestions(new ChallengeSettings { QuestionCount = 8 }, new SystemRandomSource(5), 8);
            Check(dbRt.Count >= 8, $"DB runtime generated >= 8 candidates ({dbRt.Count})");
            bool dbRtOk = true;
            foreach (var rq in dbRt)
            {
                if (rq.Distractors.Count < 3 || rq.Distractors.Count > 20) dbRtOk = false;
                foreach (var d in rq.Distractors)
                    if (d.CharCount != rq.Correct.CharCount || d.RhymeGroup != rq.Correct.RhymeGroup) dbRtOk = false;
            }
            Check(dbRtOk, "DB runtime distractor pools: 3..20 each, same 字数+韵组");

            // Rhyme dictionaries load from the DB.
            var dbRhyme = await RhymeService.LoadAsync(sqlSource);
            CheckEq(dbRhyme.GroupForChar("间"), "8", "DB rhyme: 间 -> 8");
            var dbCp = await sqlSource.LoadCharPinyinAsync();
            Check(dbCp.ContainsKey("光") && dbCp["光"].Count > 0 && dbCp["光"][0] == "guang1", "DB char_pinyin: 光 -> guang1");
        }

        // ---- 3c. P2: SQL pool filtering parity (DB-backed ContentService vs in-memory) ----
        Section("P2: SQL pool filtering parity");
        if (File.Exists(dbPath))
        {
            var memC = await ContentService.LoadAsync(bootSource);                                  // in-memory full-scan
            var sqlC = await ContentService.LoadAsync(new SqliteContentSource(dbPath), new SqliteContentDb(dbPath)); // SQL filtering

            CheckEq(string.Join(",", sqlC.GetDifficultyTiers()), string.Join(",", memC.GetDifficultyTiers()), "P2 difficulty tiers parity");
            CheckEq(string.Join(",", sqlC.GetWordClozeDifficultyTiers()), string.Join(",", memC.GetWordClozeDifficultyTiers()), "P2 wordcloze tiers parity");
            CheckEq(string.Join(",", sqlC.GetWordClozeBlankCounts()), string.Join(",", memC.GetWordClozeBlankCounts()), "P2 挖空数 set parity");

            // Build a battery of settings: all, per-dynasty, per-type, per-tier, and a combo.
            var p2Dyns = memC.GetDynasties(); var p2Types = memC.GetTypes(); var p2Tiers = memC.GetDifficultyTiers();
            var p2Battery = new List<ChallengeSettings> { new ChallengeSettings() };
            foreach (var d in p2Dyns) p2Battery.Add(new ChallengeSettings { Dynasties = new List<string> { d } });
            foreach (var t in p2Types) p2Battery.Add(new ChallengeSettings { Types = new List<string> { t } });
            foreach (var tier in p2Tiers) p2Battery.Add(new ChallengeSettings { Difficulties = new List<int> { tier } });
            if (p2Dyns.Count > 0 && p2Tiers.Count > 0)
                p2Battery.Add(new ChallengeSettings { Dynasties = new List<string> { p2Dyns[0] }, Difficulties = new List<int> { p2Tiers[0] } });

            bool poolOk = true, wcOk = true;
            foreach (var s in p2Battery)
            {
                if (sqlC.CountPool(s) != memC.CountPool(s)) poolOk = false;
                if (string.Join(",", sqlC.GetPool(s).ConvertAll(q => q.Id)) != string.Join(",", memC.GetPool(s).ConvertAll(q => q.Id))) poolOk = false;
                if (sqlC.CountWordClozePool(s) != memC.CountWordClozePool(s)) wcOk = false;
                if (string.Join(",", sqlC.GetWordClozePool(s).ConvertAll(q => q.Id)) != string.Join(",", memC.GetWordClozePool(s).ConvertAll(q => q.Id))) wcOk = false;
            }
            Check(poolOk, $"P2 question pool parity (count + id order) across {p2Battery.Count} settings");
            Check(wcOk, "P2 wordcloze pool parity across settings");

            // 挖空数 filter parity.
            var p2Bcs = memC.GetWordClozeBlankCounts();
            if (p2Bcs.Count > 0)
            {
                var bcSet = new List<int> { p2Bcs[0] };
                CheckEq(sqlC.CountWordClozePool(new ChallengeSettings(), bcSet), memC.CountWordClozePool(new ChallengeSettings(), bcSet), "P2 挖空数 filter parity");
            }

            // BuildRuntimeQuestions: order-preserving grouping ⇒ same seed yields identical selection.
            var rqA = sqlC.BuildRuntimeQuestions(new ChallengeSettings { QuestionCount = 8 }, new SystemRandomSource(5), 8);
            var rqB = memC.BuildRuntimeQuestions(new ChallengeSettings { QuestionCount = 8 }, new SystemRandomSource(5), 8);
            CheckEq(string.Join(",", rqA.ConvertAll(q => q.Id)), string.Join(",", rqB.ConvertAll(q => q.Id)), "P2 BuildRuntimeQuestions identical for same seed");

            // Difficulty override flows into SQL line-difficulty (temp table) the same as in-memory tiers.
            sqlC.SetDifficulty("tang-libai-jingyesi", 3);
            memC.SetDifficulty("tang-libai-jingyesi", 3);
            CheckEq(string.Join(",", sqlC.GetDifficultyTiers()), string.Join(",", memC.GetDifficultyTiers()), "P2 tiers parity after SetDifficulty override");
            var d3 = new ChallengeSettings { Difficulties = new List<int> { 3 } };
            CheckEq(sqlC.CountPool(d3), memC.CountPool(d3), "P2 pool parity after override (tier 3)");
            CheckEq(string.Join(",", sqlC.GetPool(d3).ConvertAll(q => q.Id)), string.Join(",", memC.GetPool(d3).ConvertAll(q => q.Id)), "P2 pool id parity after override");
        }
        else Console.WriteLine("  SKIP: content.db not found — run build_db.py.");

        // ---- 4. QuizService ----
        Section("QuizService");
        var quiz = new QuizService(new SystemRandomSource(7));
        var pool = content.GetPool(new ChallengeSettings { QuestionCount = 10 }); // no filters = all
        Check(pool.Count > 0, "unfiltered pool non-empty");
        // difficulty/dynasty multi-select filters
        var tiers = content.GetDifficultyTiers();
        Check(tiers.Count >= 2, $"multiple difficulty tiers present ({string.Join(",", tiers)})");
        int tierSum = 0;
        foreach (var t in tiers)
            tierSum += content.GetPool(new ChallengeSettings { Difficulties = new List<int> { t } }).Count;
        CheckEq(tierSum, pool.Count, "difficulty tiers partition the pool");
        CheckEq(content.GetPool(new ChallengeSettings { Difficulties = new List<int> { 99 } }).Count, 0,
            "unknown difficulty matches nothing");
        Check(content.GetPool(new ChallengeSettings { Dynasties = new List<string> { "唐" } }).Count > 0
            && content.GetPool(new ChallengeSettings { Dynasties = new List<string> { "唐" } }).Count < pool.Count,
            "dynasty [唐] is a non-trivial subset");
        CheckEq(content.GetPool(new ChallengeSettings { Dynasties = new List<string> { "无此朝代" } }).Count, 0,
            "unknown dynasty matches nothing");
        CheckEq(content.CountPool(new ChallengeSettings()), pool.Count, "CountPool == GetPool().Count");
        // 体裁 (type) filter
        int ciCount = content.GetPool(new ChallengeSettings { Types = new List<string> { "词" } }).Count;
        int shiCount = content.GetPool(new ChallengeSettings { Types = new List<string> { "诗" } }).Count;
        Check(ciCount > 0 && shiCount > 0 && ciCount < pool.Count, "type filter 诗/词 works");
        // runtime distractor generation
        var rt = content.BuildRuntimeQuestions(new ChallengeSettings { QuestionCount = 8 }, new SystemRandomSource(5), 8);
        Check(rt.Count >= 8, $"runtime generated >= 8 candidates ({rt.Count})");
        bool rtOk = true;
        foreach (var rq in rt)
        {
            if (rq.Distractors.Count < 3 || rq.Distractors.Count > 20) rtOk = false;
            foreach (var d in rq.Distractors)
                if (d.CharCount != rq.Correct.CharCount || d.RhymeGroup != rq.Correct.RhymeGroup) rtOk = false;
        }
        Check(rtOk, "runtime distractor pools: 3..20 each, same 字数+韵组");
        var rt0 = content.BuildRuntimeQuestions(new ChallengeSettings { Difficulties = new List<int> { 0 }, QuestionCount = 5 },
            new SystemRandomSource(9), 5);
        bool d0only = true;
        foreach (var rq in rt0)
            foreach (var d in rq.Distractors)
            {
                var dp = content.GetPoem(d.SourcePoemId);
                if (dp != null && dp.Difficulty > 0) d0only = false;
            }
        Check(d0only, "tier-0 selection: distractors only from difficulty<=0 poems");
        // in-memory difficulty override
        var ovPoem = content.Poems[0].Id;
        int ovOrig = content.GetPoem(ovPoem).Difficulty;
        content.SetDifficulty(ovPoem, 3);
        CheckEq(content.GetPoem(ovPoem).Difficulty, 3, "SetDifficulty updates tier in memory");
        content.ApplyDifficultyOverrides(new Dictionary<string, int> { { ovPoem, ovOrig } });
        CheckEq(content.GetPoem(ovPoem).Difficulty, ovOrig, "ApplyDifficultyOverrides restores tier");

        // per-line difficulty derivation (DifficultyRules)
        CheckEq(DifficultyRules.LineDifficulty(0, true), 0, "tier0 -> 0");
        CheckEq(DifficultyRules.LineDifficulty(1, true), 1, "tier1 名句 -> 1");
        CheckEq(DifficultyRules.LineDifficulty(1, false), 2, "tier1 其余 -> 2");
        CheckEq(DifficultyRules.LineDifficulty(2, true), 2, "tier2 名句 -> 2");
        CheckEq(DifficultyRules.LineDifficulty(2, false), 3, "tier2 其余 -> 3");
        CheckEq(DifficultyRules.LineDifficulty(3, false), 3, "tier3 -> 3");
        var synth = new Poem { Difficulty = 1, Lines = { new PoemLine { Famous = true }, new PoemLine { Famous = false } } };
        CheckEq(DifficultyRules.LineDifficulty(synth, 0), 1, "synthetic famous line -> 1");
        CheckEq(DifficultyRules.LineDifficulty(synth, 1), 2, "synthetic other line -> 2");
        CheckEq(DifficultyRules.AvgDifficulty(synth), 2, "avg (1+2)/2 rounded -> 2");
        var gp = new Poem { Lines = { new PoemLine { Group = 5 }, new PoemLine() } };
        CheckEq(DifficultyRules.EffectiveGroup(gp, 0), 5, "explicit group respected");
        CheckEq(DifficultyRules.EffectiveGroup(gp, 1), 0, "auto group = index/2");
        // explicit per-line difficulty override
        var ovp = new Poem { Difficulty = 1, Lines = { new PoemLine { Famous = false }, new PoemLine { Famous = true, Diff = 3 } } };
        CheckEq(DifficultyRules.LineDifficulty(ovp, 0), 2, "no override -> derived (档1 其余 = 2)");
        CheckEq(DifficultyRules.LineDifficulty(ovp, 1), 3, "explicit override -> 3 (ignores 名句)");
        CheckEq(DifficultyRules.AvgDifficulty(ovp), 3, "avg (2+3)/2 -> 3 with override");
        CheckEq(quiz.TimeLimitSeconds(quiz.Prepare(rt[0])) >= 6, true, "time limit >= 6s");
        // Session is built from runtime questions (distractor pools already filled from clusters).
        var session = quiz.BuildSession(
            content.BuildRuntimeQuestions(new ChallengeSettings { QuestionCount = 5 }, new SystemRandomSource(7), 5),
            new ChallengeSettings { QuestionCount = 5 });
        CheckEq(session.Total, 5, "session has 5 questions");
        bool indicesOk = true, optionCountOk = true;
        var poemsInSession = new HashSet<string>();
        foreach (var qq in session.Questions)
        {
            if (qq.Options.Count != 4) optionCountOk = false;
            if (qq.Options[qq.CorrectIndex] != qq.Source.Correct) indicesOk = false;
            poemsInSession.Add(qq.PoemId);
        }
        Check(optionCountOk, "every question shows 4 options");
        Check(indicesOk, "CorrectIndex points at the correct option after shuffle");
        CheckEq(poemsInSession.Count, 5, "no repeated poem within the session");

        // 出题：从干扰项池中随机取 3 个 —— pick a runtime question whose cluster pool is larger than 3
        // and verify each Prepare draws exactly 3 distinct distractors that all belong to that pool.
        Question poolQ = null;
        foreach (var q in rt) if (q.Distractors.Count > 3) { poolQ = q; break; }
        Check(poolQ != null, "at least one runtime question has a distractor pool > 3");
        if (poolQ != null)
        {
            var poolTexts = new HashSet<string>();
            foreach (var d in poolQ.Distractors) poolTexts.Add(d.Text);
            bool sampleOk = true;
            var sampledTexts = new HashSet<string>();
            for (int t = 0; t < 30; t++)
            {
                var prepared = quiz.Prepare(poolQ);
                if (prepared.Options.Count != 4) sampleOk = false;
                var distinct = new HashSet<string>();
                int correctCount = 0;
                foreach (var o in prepared.Options)
                {
                    if (!distinct.Add(o.Text)) sampleOk = false;             // 4 options all distinct
                    if (o.Text == poolQ.Correct.Text) correctCount++;
                    else { if (!poolTexts.Contains(o.Text)) sampleOk = false; sampledTexts.Add(o.Text); }
                }
                if (correctCount != 1) sampleOk = false;                     // exactly the correct + 3 distractors
            }
            Check(sampleOk, "Prepare samples exactly 3 in-pool distractors + 1 correct, all distinct");
            Check(sampledTexts.Count > 3, $"sampling varies across attempts (saw {sampledTexts.Count} distinct distractors)");
        }

        var first = session.Questions[0];
        var rWrong = quiz.BuildResult(first, (first.CorrectIndex + 1) % 4, 1500);
        var rRight = quiz.BuildResult(first, first.CorrectIndex, 1200);
        Check(!rWrong.IsCorrect && rRight.IsCorrect, "BuildResult scores right/wrong correctly");
        CheckEq(rRight.CorrectText, first.Source.Correct.Text, "result carries the correct line text");

        // ---- 5. Repositories round-trip (temp dir, SQLite user.db) ----
        Section("Repositories round-trip (SQLite user.db)");
        string tmp = Path.Combine(Path.GetTempPath(), "poempoetry_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var clock = new FixedClock(new DateTime(2026, 6, 22, 9, 0, 0, DateTimeKind.Utc));
            var userDb = await SqliteUserDatabase.OpenAsync(tmp);

            // Records
            var records = new RecordService(new SqliteRecordRepository(userDb), clock);
            var items = new List<QuestionResult>
            {
                new QuestionResult { QuestionId = "a", IsCorrect = true },
                new QuestionResult { QuestionId = "b", IsCorrect = true },
                new QuestionResult { QuestionId = "c", IsCorrect = false },
                new QuestionResult { QuestionId = "d", IsCorrect = true },
            };
            var rec = await records.SaveCompletedAsync(items, new ChallengeSettings { QuestionCount = 4 }, 90);
            CheckEq(rec.AccuracyPercent, 75, "saved record accuracy = 75%");
            var summaries = await records.GetAllAsync();
            CheckEq(summaries.Count, 1, "one record summary persisted");
            var loaded = await records.GetAsync(rec.Id);
            Check(loaded != null && loaded.Items.Count == 4, "record reloads with all 4 items");

            // slide record persists its grid snapshot
            var snapR = new SlideSnapshot { Size = 4 };
            snapR.Cells.AddRange(new[] { "床", "前", "明", "月", "光", "好", "学", "习", "天", "天", "向", "上", "加", "油", "干", "劲" });
            snapR.Targets.Add(new SlideTargetSnapshot { Text = "床前明月", Title = "静夜思", PoemId = "p", Found = true, Cells = new List<int> { 0, 1, 2, 3 } });
            var sr = await records.SaveCompletedAsync(new List<QuestionResult>(), new ChallengeSettings(), 10, "slide", snapR);
            var srl = await records.GetAsync(sr.Id);
            Check(srl != null && srl.Slide != null && srl.Slide.Size == 4 && srl.Slide.Targets.Count == 1,
                "slide record persists grid snapshot");

            // Favorites
            var favRepo = new SqliteFavoriteRepository(userDb);
            var favorites = new FavoriteService(favRepo, clock);
            int changed = 0; favorites.FavoritesChanged += () => changed++;
            await favorites.AddAsync("poem-1");
            Check(await favorites.IsFavoriteAsync("poem-1"), "favorite added");
            await favorites.AddAsync("poem-1"); // idempotent
            CheckEq((await favorites.GetAllAsync()).Count, 1, "favorites de-duplicated");
            bool nowOn = await favorites.ToggleAsync("poem-1");
            Check(!nowOn && !(await favorites.IsFavoriteAsync("poem-1")), "toggle removes favorite");
            Check(changed >= 2, "FavoritesChanged fired");

            // 错题本 Leitner
            var wrongRepo = new SqliteWrongBookRepository(userDb);
            var wrongbook = new WrongBookService(wrongRepo, clock);
            await wrongbook.RegisterWrongAsync("q-1", "poem-1");
            CheckEq((await wrongbook.GetAllAsync()).Count, 1, "wrong question recorded");
            CheckEq(await wrongbook.GetDueCountAsync(), 0, "not due immediately (box1 = +1 day)");
            clock.Advance(TimeSpan.FromDays(2));
            CheckEq(await wrongbook.GetDueCountAsync(), 1, "due after 2 days");
            await wrongbook.RegisterReviewResultAsync("q-1", true); // box1 -> box2
            await wrongbook.RegisterReviewResultAsync("q-1", true); // box2 -> box3
            var e = await wrongRepo.GetAsync("q-1");
            CheckEq(e.Box, 3, "two correct reviews promote to box 3");
            await wrongbook.RegisterReviewResultAsync("q-1", true); // box3 -> graduate
            CheckEq((await wrongbook.GetAllAsync()).Count, 0, "correct out of box 3 graduates (removed)");

            // Settings (reopen the DB to prove it persists across a fresh connection)
            var settings = new SettingsService(new SqliteSettingsStore(userDb));
            await settings.InitAsync();
            CheckEq(settings.Current.LastChallengeLength, 10, "settings default length = 10");
            await settings.SetLastChallengeLengthAsync(20);
            var userDb2 = await SqliteUserDatabase.OpenAsync(tmp);
            var settings2 = new SettingsService(new SqliteSettingsStore(userDb2));
            await settings2.InitAsync();
            CheckEq(settings2.Current.LastChallengeLength, 20, "settings persist across reload");

            // difficulty override store round-trip
            await new SqliteDifficultyOverrideStore(userDb).SaveAsync(new Dictionary<string, int> { { "poem-x", 4 } });
            var dov = await new SqliteDifficultyOverrideStore(userDb).LoadAsync();
            Check(dov.ContainsKey("poem-x") && dov["poem-x"] == 4, "difficulty override store round-trip");

            // JSON → SQLite migration: seed JSON via the OLD repos, then open user.db over that dir.
            var mdir = Path.Combine(tmp, "migration");
            Directory.CreateDirectory(mdir);
            await new JsonFavoriteRepository(mdir).AddAsync(new FavoriteEntry { PoemId = "mig-poem", AddedAtUtc = "2026-06-22T00:00:00Z" });
            await new JsonWrongBookRepository(mdir).UpsertAsync(new WrongBookEntry { QuestionId = "mig-q", PoemId = "mig-poem", Box = 2, NextReviewUtc = "2026-06-20T00:00:00Z" });
            await new JsonRecordRepository(mdir).SaveAsync(new ChallengeRecord { Id = "mig-rec", Mode = "challenge", Total = 3, CorrectCount = 2, AccuracyPercent = 67 });
            var migDb = await SqliteUserDatabase.OpenAsync(mdir);
            CheckEq((await new SqliteFavoriteRepository(migDb).GetAllAsync()).Count, 1, "migration imported favorite");
            CheckEq((await new SqliteWrongBookRepository(migDb).GetAllAsync()).Count, 1, "migration imported 错题");
            var migRec = await new SqliteRecordRepository(migDb).GetByIdAsync("mig-rec");
            Check(migRec != null && migRec.CorrectCount == 2, "migration imported full record");
            Check(!File.Exists(Path.Combine(mdir, "favorites.json")) && File.Exists(Path.Combine(mdir, "favorites.json.migrated")),
                "migration renames old JSON aside");

            // AppServices composition root (now opens user.db internally)
            var app = await AppServices.CreateAsync(bootSource, tmp, clock, new SystemRandomSource(1));
            Check(app.Content != null && app.Rhyme != null && app.Quiz != null && app.Difficulty != null,
                "AppServices wires all services");
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }

        // ---- 6. JSON round-trip ----
        Section("JSON round-trip");
        var q0 = questions[0];
        var json = PoemJson.Serialize(q0);
        var q0b = PoemJson.Deserialize<Question>(json);
        CheckEq(q0b.Correct.Text, q0.Correct.Text, "Question survives serialize/deserialize");
        CheckEq(q0b.ClusterId, q0.ClusterId, "clusterId survives round-trip");
        Check(!json.Contains("\"distractors\""), "v2: distractors NOT serialized per-question (resolved from cluster)");
        Check(json.Contains("\"poemId\""), "camelCase property naming in JSON");
        // Cluster round-trips with its shared line pool.
        var c0 = bank.Clusters[0];
        var c0b = PoemJson.Deserialize<LineCluster>(PoemJson.Serialize(c0));
        Check(c0b.Lines.Count == c0.Lines.Count && c0b.ToneType == c0.ToneType, "LineCluster survives round-trip");

        // ---- 7. GridWordSearch (滑动找诗 core) ----
        Section("GridWordSearch");
        string[] sampleLines = { "床前明月光", "疑是地上霜", "举头望明月", "低头思故乡", "春眠不觉晓" };
        var pool2 = new List<string> { "一", "二", "三", "四", "五", "六", "七", "八" };
        foreach (var level in new[] { 1, 2, 3, 4 })
        {
            var g = new GridWordSearch(9, level, allowOverlap: false, rng: new SystemRandomSource(100 + level));
            int placed = 0;
            foreach (var line in sampleLines)
            {
                var chars = SplitToChars(line);
                if (g.TryPlace(line, chars, "title")) placed++;
            }
            g.FillEmpty(pool2);

            // every cell filled
            bool allFilled = true;
            foreach (var c in g.Cells) if (string.IsNullOrEmpty(c)) allFilled = false;
            Check(allFilled, $"L{level}: grid fully filled");
            Check(placed >= 3, $"L{level}: placed >= 3 lines (got {placed})");

            // each placed target's cells: in-bounds, distinct, adjacency-consistent, and re-matchable
            bool ok = true;
            foreach (var t in g.Targets)
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < t.Cells.Count; i++)
                {
                    if (t.Cells[i] < 0 || t.Cells[i] >= 81) ok = false;
                    if (!seen.Add(t.Cells[i])) ok = false;            // self-avoiding
                    if (i > 0 && !g.Adjacent(t.Cells[i - 1], t.Cells[i])) ok = false;
                }
                if (g.TryMatch(t.Cells) == null) ok = false;          // forward trace matches
            }
            Check(ok, $"L{level}: targets in-bounds, self-avoiding, adjacent, matchable");

            // reverse trace must NOT match (forward-only) on a fresh grid
            var g2 = new GridWordSearch(9, level, false, new SystemRandomSource(500 + level));
            g2.TryPlace("床前明月光", SplitToChars("床前明月光"), "t");
            if (g2.Targets.Count > 0 && g2.Targets[0].Cells.Count >= 2)
            {
                var rev = new List<int>(g2.Targets[0].Cells);
                rev.Reverse();
                Check(g2.TryMatch(rev) == null, $"L{level}: reverse trace does NOT match (forward-only)");
            }
        }
        // straight levels only move in one direction (a straight line has constant step)
        var gs = new GridWordSearch(10, 1, false, new SystemRandomSource(7));
        gs.TryPlace("两个黄鹂", SplitToChars("两个黄鹂"), "t");
        bool straight = true;
        if (gs.Targets.Count > 0)
        {
            var cells = gs.Targets[0].Cells;
            int dr0 = cells[1] / 10 - cells[0] / 10, dc0 = cells[1] % 10 - cells[0] % 10;
            for (int i = 2; i < cells.Count; i++)
                if (cells[i] / 10 - cells[i - 1] / 10 != dr0 || cells[i] % 10 - cells[i - 1] % 10 != dc0) straight = false;
        }
        Check(straight, "L1 placement is a straight constant-step line");
        // larger grid size honored
        var gbig = new GridWordSearch(12, 1, false, new SystemRandomSource(3));
        CheckEq(gbig.Cells.Length, 144, "grid size 12 -> 144 cells");

        // snapshot round-trip (for slide replay)
        var gsnap = new GridWordSearch(8, 1, false, new SystemRandomSource(11));
        gsnap.TryPlace("床前明月光", SplitToChars("床前明月光"), "静夜思", "p1");
        gsnap.FillEmpty(pool2);
        var snap = new SlideSnapshot { Size = 8 };
        foreach (var c in gsnap.Cells) snap.Cells.Add(c);
        foreach (var tt in gsnap.Targets)
        {
            var ts = new SlideTargetSnapshot { Text = tt.Text, Title = tt.Title, PoemId = tt.PoemId, Found = false };
            foreach (var idx in tt.Cells) ts.Cells.Add(idx);
            snap.Targets.Add(ts);
        }
        var gFrom = GridWordSearch.FromSnapshot(snap);
        Check(gFrom.Size == 8 && gFrom.Targets.Count == gsnap.Targets.Count, "FromSnapshot restores grid + targets");
        if (gFrom.Targets.Count > 0)
            Check(gFrom.TryMatch(gFrom.Targets[0].Cells) != null, "FromSnapshot target re-matchable");

        // non-square (taller) grid
        var grect = new GridWordSearch(8, 12, 1, false, new SystemRandomSource(21));
        CheckEq(grect.Cells.Length, 96, "non-square 8x12 -> 96 cells");
        grect.TryPlace("床前明月光", SplitToChars("床前明月光"), "t");
        grect.FillEmpty(pool2);
        bool rectOk = true;
        foreach (var t in grect.Targets)
            for (int i = 0; i < t.Cells.Count; i++) { if (t.Cells[i] < 0 || t.Cells[i] >= 96) rectOk = false; }
        Check(rectOk, "non-square placement stays in bounds");

        // StraightPath diagonal/horizontal snap (L2)
        var gdiag = new GridWordSearch(10, 10, 2, false, new SystemRandomSource(22));
        var dpath = gdiag.StraightPath(0, 3 * 10 + 3); // (0,0)->(3,3)
        Check(dpath.Count == 4 && dpath[0] == 0 && dpath[3] == 33, "StraightPath diagonal (0,0)->(3,3) = 4 cells");
        var hpath = gdiag.StraightPath(0, 5); // (0,0)->(0,5)
        Check(hpath.Count == 6 && hpath[5] == 5, "StraightPath horizontal = 6 cells");

        // overlap (重叠字交叉): lines sharing chars must actually interlock crossword-style,
        // i.e. at least one cell ends up shared by two targets (not just theoretically allowed).
        string[] crossLines = { "床前明月光", "举头望明月", "低头思故乡", "疑是地上霜", "春眠不觉晓", "明月几时有" };
        bool sawCrossing = false;
        foreach (var level in new[] { 1, 2, 3, 4 })
        {
            var gx = new GridWordSearch(10, level, allowOverlap: true, rng: new SystemRandomSource(900 + level));
            foreach (var line in crossLines) gx.TryPlace(line, SplitToChars(line), "t");
            var owner = new Dictionary<int, int>();
            bool levelCross = false;
            for (int ti = 0; ti < gx.Targets.Count; ti++)
                foreach (var idx in gx.Targets[ti].Cells)
                {
                    if (owner.TryGetValue(idx, out int other) && other != ti) levelCross = true;
                    owner[idx] = ti;
                }
            // every crossing cell must agree on its character (re-matchable trace still holds)
            bool stillMatchable = true;
            foreach (var t in gx.Targets) if (gx.TryMatch(t.Cells) == null) stillMatchable = false;
            Check(stillMatchable, $"L{level} overlap: crossed targets still re-matchable");
            if (levelCross) sawCrossing = true;
        }
        Check(sawCrossing, "overlap mode actually produces a shared-cell crossing");

        // 8x8 still出题: small square grid (maxLine=8) must place several 5~8 字诗句 on every level,
        // both with and without overlap (the slide config now allows tiny grids via the cols slider).
        string[] small8 = { "床前明月光", "疑是地上霜", "举头望明月", "低头思故乡", "春眠不觉晓", "夜来风雨声" };
        foreach (var overlap in new[] { false, true })
            foreach (var level in new[] { 1, 2, 3, 4 })
            {
                var g8 = new GridWordSearch(8, 8, level, overlap, new SystemRandomSource(800 + level + (overlap ? 50 : 0)));
                int placed8 = 0;
                foreach (var line in small8)
                    if (g8.TryPlace(line, SplitToChars(line), "t")) placed8++;
                Check(placed8 >= 3, $"8x8 L{level} overlap={overlap}: placed >= 3 lines (got {placed8})");
            }

        // ---- Summary ----
        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($"  RESULT: {_passed} passed, {_failed} failed");
        Console.WriteLine("==================================================");
        return _failed == 0 ? 0 : 1;
    }

    private static Poem FindPoem(PoemFile file, string id)
    {
        foreach (var p in file.Poems) if (p.Id == id) return p;
        return null;
    }

    private static string[] SplitToChars(string s)
    {
        var si = new System.Globalization.StringInfo(s);
        int n = si.LengthInTextElements;
        var r = new string[n];
        for (int i = 0; i < n; i++) r[i] = si.SubstringByTextElements(i, 1);
        return r;
    }

    private static void Write(string path, object value) =>
        File.WriteAllText(path, PoemJson.Serialize(value), Utf8NoBom);

    private static void Section(string name) => Console.WriteLine("\n[" + name + "]");

    private static void Check(bool cond, string name)
    {
        if (cond) { _passed++; Console.WriteLine("  PASS " + name); }
        else { _failed++; Console.WriteLine("  FAIL " + name); }
    }

    private static void CheckEq<T>(T actual, T expected, string name)
    {
        if (Equals(actual, expected)) { _passed++; Console.WriteLine("  PASS " + name); }
        else { _failed++; Console.WriteLine($"  FAIL {name}  (expected [{expected}], got [{actual}])"); }
    }
}
