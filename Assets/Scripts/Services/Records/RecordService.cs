using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>Builds and persists completed-challenge records; derives accuracy / streak.</summary>
    public sealed class RecordService
    {
        private readonly IRecordRepository _repo;
        private readonly IClock _clock;

        public RecordService(IRecordRepository repo, IClock clock)
        {
            _repo = repo;
            _clock = clock ?? new SystemClock();
        }

        public async Task<ChallengeRecord> SaveCompletedAsync(
            IReadOnlyList<QuestionResult> items,
            ChallengeSettings settings,
            int durationSeconds,
            string mode = "challenge",
            SlideSnapshot slide = null)
        {
            int correct = 0;
            var outcomes = new List<bool>(items.Count);
            foreach (var i in items) { if (i.IsCorrect) correct++; outcomes.Add(i.IsCorrect); }

            var record = new ChallengeRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                CompletedAtUtc = _clock.UtcNow.ToString("o"),
                Mode = mode,
                Settings = settings ?? new ChallengeSettings(),
                Total = items.Count,
                CorrectCount = correct,
                AccuracyPercent = ScoreMath.AccuracyPercent(correct, items.Count),
                DurationSeconds = durationSeconds,
                BestStreak = ScoreMath.BestStreak(outcomes),
                Items = new List<QuestionResult>(items),
                Slide = slide,
            };
            await _repo.SaveAsync(record);
            return record;
        }

        public Task<IReadOnlyList<ChallengeRecordSummary>> GetAllAsync() => _repo.GetAllSummariesAsync();
        public Task<ChallengeRecord> GetAsync(string id) => _repo.GetByIdAsync(id);
        public Task DeleteAsync(string id) => _repo.DeleteAsync(id);
    }
}
