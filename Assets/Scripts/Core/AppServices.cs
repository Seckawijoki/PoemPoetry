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
            IContentSource content, string userDataDir, IClock clock = null, IRandomSource rng = null)
        {
            clock = clock ?? new SystemClock();
            rng = rng ?? new SystemRandomSource();

            var contentService = await ContentService.LoadAsync(content);

            RhymeService rhyme = null;
            try { rhyme = await RhymeService.LoadAsync(content); }
            catch { /* rhyme dictionary optional at runtime; used mainly by editor tools / endless mode */ }

            var settings = new SettingsService(new JsonSettingsStore(userDataDir));
            await settings.InitAsync();

            var difficulty = new DifficultyService(new JsonDifficultyOverrideStore(userDataDir), contentService);
            await difficulty.InitAsync();

            return new AppServices(
                contentService,
                rhyme,
                new QuizService(rng),
                new RecordService(new JsonRecordRepository(userDataDir), clock),
                new FavoriteService(new JsonFavoriteRepository(userDataDir), clock),
                new WrongBookService(new JsonWrongBookRepository(userDataDir), clock),
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
