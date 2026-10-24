using UnityEngine;

namespace StorageLord.Core
{
    /// <summary>
    /// The in-game clock's scale (#53): how many real seconds make one game day, plus the
    /// conversions every system uses to turn game-time tuning (hours/days) into the real seconds
    /// timers actually tick in, and real seconds back into "2d 4h"-style display text. Runtime
    /// timers stay in real seconds; game time exists only at the data and display boundaries.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Game Clock Data", fileName = "GameClockData")]
    public class GameClockData : ScriptableObject
    {
        /// <summary>Fallback scale used when no GameClockData asset is wired.</summary>
        public const float DefaultRealSecondsPerGameDay = 300f;

        private const int MinutesPerDay = 24 * 60;

        private static GameClockData _fallback;

        /// <summary>
        /// A default-scale clock for when no GameClockData asset is wired, so no caller ever needs a
        /// null check. Created once and reused.
        /// </summary>
        public static GameClockData Fallback => _fallback != null ? _fallback : (_fallback = CreateInstance<GameClockData>());

        [Tooltip("Real seconds per in-game day. 300 = one game day every 5 real minutes.")]
        [Min(1f)]
        public float realSecondsPerGameDay = DefaultRealSecondsPerGameDay;

        /// <summary>Converts game hours to real seconds.</summary>
        public float GameHoursToSeconds(float gameHours)
        {
            return gameHours * realSecondsPerGameDay / 24f;
        }

        /// <summary>Converts game days to real seconds.</summary>
        public float GameDaysToSeconds(float gameDays)
        {
            return gameDays * realSecondsPerGameDay;
        }

        /// <summary>Converts real seconds into whole elapsed game minutes (rounded down).</summary>
        public int ToGameMinutes(float realSeconds)
        {
            return Mathf.FloorToInt(Mathf.Max(0f, realSeconds) / realSecondsPerGameDay * MinutesPerDay);
        }

        /// <summary>
        /// Rounds a remaining duration (real seconds) up to the step it's displayed at: whole hours
        /// from one game day upward, 10 game-minute steps below that. HUDs cache on this key, so a
        /// countdown row rebuilds only when its displayed text would actually change.
        /// </summary>
        public int RemainingDisplayKey(float realSeconds)
        {
            int minutes = Mathf.CeilToInt(Mathf.Max(0f, realSeconds) / realSecondsPerGameDay * MinutesPerDay);
            int step = minutes >= MinutesPerDay ? 60 : 10;
            return (minutes + step - 1) / step * step;
        }

        /// <summary>Formats a duration in game minutes as "2d 4h", "5h 20m" or "40m".</summary>
        public static string FormatGameMinutes(int gameMinutes)
        {
            int days = gameMinutes / MinutesPerDay;
            int hours = gameMinutes % MinutesPerDay / 60;
            int minutes = gameMinutes % 60;
            if (days > 0)
            {
                return hours > 0 ? $"{days}d {hours}h" : $"{days}d";
            }

            if (hours > 0)
            {
                return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h";
            }

            return $"{minutes}m";
        }

        /// <summary>Formats a remaining duration in real seconds at its display step (see RemainingDisplayKey).</summary>
        public string FormatRemaining(float realSeconds)
        {
            return FormatGameMinutes(RemainingDisplayKey(realSeconds));
        }

        /// <summary>The 1-based game day reached after <paramref name="runSeconds"/> real seconds of run time.</summary>
        public int GameDay(float runSeconds)
        {
            return Mathf.FloorToInt(Mathf.Max(0f, runSeconds) / realSecondsPerGameDay) + 1;
        }

        /// <summary>The game hour of the current day (0-23) after <paramref name="runSeconds"/> real seconds of run time.</summary>
        public int GameHour(float runSeconds)
        {
            return ToGameMinutes(runSeconds) % MinutesPerDay / 60;
        }
    }
}
