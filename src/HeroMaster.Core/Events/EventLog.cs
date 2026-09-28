using System;
using System.Collections.Generic;
using System.Linq;

namespace HeroMaster.Core.Events
{
    public enum DayPhase
    {
        MorningReport,
        MasterDecisions,
        Expedition,
        BaseLife,
        NightReflection
    }

    /// <summary>
    /// Один факт, случившийся в игре. Лог событий — единственный источник правды:
    /// из него строится память героев и тексты для игрока.
    /// </summary>
    public sealed class GameEvent
    {
        public long Id { get; set; }
        public int Day { get; set; }
        public DayPhase Phase { get; set; }
        public string Type { get; set; } = "";
        public List<string> Actors { get; set; } = new();
        /// <summary>1–10: насколько событие запомнится.</summary>
        public int Importance { get; set; }
        /// <summary>-5…5: эмоциональная окраска.</summary>
        public int Emotion { get; set; }
        /// <summary>Факт одной строкой — то, что рассказчик может пересказать.</summary>
        public string Summary { get; set; } = "";
        public Dictionary<string, string> Data { get; set; } = new();
    }

    /// <summary>Журнал событий только на дописывание.</summary>
    public sealed class EventLog
    {
        public List<GameEvent> Events { get; set; } = new();
        public long NextId { get; set; } = 1;

        public GameEvent Append(
            int day,
            DayPhase phase,
            string type,
            string summary,
            int importance,
            int emotion,
            params string[] actors)
        {
            if (string.IsNullOrWhiteSpace(type))
                throw new ArgumentException("У события должен быть тип.", nameof(type));
            if (importance < 1 || importance > 10)
                throw new ArgumentOutOfRangeException(nameof(importance), "Важность должна быть от 1 до 10.");
            if (emotion < -5 || emotion > 5)
                throw new ArgumentOutOfRangeException(nameof(emotion), "Эмоция должна быть от -5 до 5.");

            var e = new GameEvent
            {
                Id = NextId++,
                Day = day,
                Phase = phase,
                Type = type,
                Summary = summary,
                Importance = importance,
                Emotion = emotion,
                Actors = actors.ToList()
            };
            Events.Add(e);
            return e;
        }

        public IEnumerable<GameEvent> ForDay(int day) => Events.Where(e => e.Day == day);

        public IEnumerable<GameEvent> Involving(string actorId) => Events.Where(e => e.Actors.Contains(actorId));
    }
}
