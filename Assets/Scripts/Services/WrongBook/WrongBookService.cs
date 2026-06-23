using System.Collections.Generic;
using System.Threading.Tasks;
using PoemPoetry.Data;

namespace PoemPoetry.Services
{
    /// <summary>
    /// 错题本 with a 3-box Leitner schedule. A wrong answer (in any mode) drops the item to
    /// box 1; a correct review promotes it; getting it right out of box 3 graduates it.
    /// </summary>
    public sealed class WrongBookService
    {
        // Days until an item in each box becomes due again.
        private static readonly int[] BoxIntervalDays = { 1, 3, 7 };

        private readonly IWrongBookRepository _repo;
        private readonly IClock _clock;

        public WrongBookService(IWrongBookRepository repo, IClock clock)
        {
            _repo = repo;
            _clock = clock ?? new SystemClock();
        }

        /// <summary>Record a missed question: add it or reset it to box 1.</summary>
        public async Task RegisterWrongAsync(string questionId, string poemId)
        {
            var e = await _repo.GetAsync(questionId);
            if (e == null)
                e = new WrongBookEntry { QuestionId = questionId, PoemId = poemId, AddedAtUtc = _clock.UtcNow.ToString("o") };
            e.Box = 1;
            e.LastResult = "wrong";
            e.ReviewCount++;
            e.NextReviewUtc = _clock.UtcNow.AddDays(BoxIntervalDays[0]).ToString("o");
            await _repo.UpsertAsync(e);
        }

        /// <summary>Record a review outcome: correct promotes (and graduates from box 3); wrong resets.</summary>
        public async Task RegisterReviewResultAsync(string questionId, bool correct)
        {
            var e = await _repo.GetAsync(questionId);
            if (e == null) return;
            e.ReviewCount++;
            if (correct)
            {
                if (e.Box >= BoxIntervalDays.Length) { await _repo.RemoveAsync(questionId); return; }
                e.Box++;
                e.LastResult = "right";
            }
            else
            {
                e.Box = 1;
                e.LastResult = "wrong";
            }
            e.NextReviewUtc = _clock.UtcNow.AddDays(BoxIntervalDays[e.Box - 1]).ToString("o");
            await _repo.UpsertAsync(e);
        }

        public Task<IReadOnlyList<WrongBookEntry>> GetAllAsync() => _repo.GetAllAsync();
        public Task<IReadOnlyList<WrongBookEntry>> GetDueAsync() => _repo.GetDueAsync(_clock.UtcNow);

        public async Task<int> GetDueCountAsync()
        {
            var due = await _repo.GetDueAsync(_clock.UtcNow);
            return due.Count;
        }
    }
}
