using System;

namespace AutoMotion.Models
{
    /// <summary>
    /// Basit bir Pomodoro odak-seansı zamanlayıcısı. Oyun karesinden bağımsız, gerçek zamana
    /// (DateTime) göre işler - böylece pencere arka planda/minimize haldeyken bile doğru sürer.
    /// </summary>
    public class PomodoroSession
    {
        public static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(25);

        public bool IsActive { get; private set; }
        public DateTime StartedAt { get; private set; }
        public TimeSpan Duration { get; private set; }

        public TimeSpan Remaining => IsActive
            ? TimeSpanMax(TimeSpan.Zero, StartedAt + Duration - DateTime.Now)
            : TimeSpan.Zero;

        private static TimeSpan TimeSpanMax(TimeSpan a, TimeSpan b) => a > b ? a : b;

        public void Start(TimeSpan? duration = null)
        {
            Duration = duration ?? DefaultDuration;
            StartedAt = DateTime.Now;
            IsActive = true;
        }

        public void Cancel() => IsActive = false;

        /// <summary>Her oyun karesinde çağrılır; seans bu çağrıda doğal olarak bittiyse (süre dolduysa)
        /// bir kez true döner ve seansı kapatır.</summary>
        public bool CheckCompleted()
        {
            if (!IsActive) return false;
            if (DateTime.Now < StartedAt + Duration) return false;

            IsActive = false;
            return true;
        }
    }
}
