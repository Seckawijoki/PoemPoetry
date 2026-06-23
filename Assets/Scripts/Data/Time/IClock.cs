using System;

namespace PoemPoetry.Data
{
    /// <summary>Abstracts "now" so time-dependent logic (records, 错题本 scheduling) is testable.</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    /// <summary>Test clock with a settable, advanceable instant.</summary>
    public sealed class FixedClock : IClock
    {
        public DateTime UtcNow { get; private set; }
        public FixedClock(DateTime utcNow) { UtcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc); }
        public void Advance(TimeSpan delta) { UtcNow = UtcNow + delta; }
    }
}
