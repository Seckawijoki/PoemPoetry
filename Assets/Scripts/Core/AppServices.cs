using System.Threading.Tasks;
using PoemPoetry.Data;
using PoemPoetry.Services;

namespace PoemPoetry.Core
{
    /// <summary>
    /// Composition root + service container. Holds every service the UI talks to. Built once
    /// at startup by the bootstrapper; swapping local storage for a REST backend means changing
    /// only the repository/source construction in <see cref="CreateAsync"/>.
    /// </summary>
    public sealed class AppServices
    {
        public ContentService Content { get; }
        public RhymeService Rhyme { get; }          // may be null if the rhyme dictionary isn't shipped
        public QuizService Quiz { get; }
        public RecordService Records { get; }
        public FavoriteService Favorites { get; }
        public WrongBookService WrongBook { get; }
        public SettingsService Settings { get; }
        public DifficultyService Difficulty { get; }

        public AppServices(
            ContentService content, RhymeService rhyme, QuizService quiz, RecordService records,
            FavoriteService favorites, WrongBookService wrongBook, SettingsService settings,
            DifficultyService difficulty)
        {
            Content = content;
            Rhyme = rhyme;
            Quiz = quiz;
            Records = records;
            Favorites = favorites;
            WrongBook = wrongBook;
            Settings = settings;
            Difficulty = difficulty;
        }

        public static async Task<AppServices> CreateAsync(
            IContentSource content, string userDataDir, IClock clock = null, IRandomSource rng = null,
            SqliteContentDb contentDb = null)
        {
            clock = clock ?? new SystemClock();
            rng = rng ?? new SystemRandomSource();

            // contentDb (when supplied) pushes pool filtering down to indexed SQL; null keeps the
            // in-memory path (unit tests / fallback).
            var contentService = await ContentService.LoadAsync(content, contentDb);

            RhymeService rhyme = null;
            try { rhyme = await RhymeService.LoadAsync(content); }
            catch { /* rhyme dictionary optional at runtime; used mainly by editor tools / endless mode */ }

            // User data now lives in one transactional SQLite file (user.db); on first open it
            // imports any pre-existing JSON via the old repos and renames them aside.
            var userDb = await SqliteUserDatabase.OpenAsync(userDataDir);

            var settings = new SettingsService(new SqliteSettingsStore(userDb));
            await settings.InitAsync();

            var difficulty = new DifficultyService(new SqliteDifficultyOverrideStore(userDb), contentService);
            await difficulty.InitAsync();

            return new AppServices(
                contentService,
                rhyme,
                new QuizService(rng),
                new RecordService(new SqliteRecordRepository(userDb), clock),
                new FavoriteService(new SqliteFavoriteRepository(userDb), clock),
                new WrongBookService(new SqliteWrongBookRepository(userDb), clock),
                settings,
                difficulty);
        }
    }

    /// <summary>
    /// Global access point to the built services (set once by the bootstrapper).
    /// Named GameApp (not App) to avoid clashing with the PoemPoetry.App namespace.
    /// </summary>
    public static class GameApp
    {
        public static AppServices Services { get; set; }
    }
}
