using System.Collections.Generic;

namespace HeroMaster.Core.Model
{
    /// <summary>Черты характера, 0–100. Почти не меняются за игру.</summary>
    public sealed class Traits
    {
        public int Courage { get; set; }
        public int Pride { get; set; }
        public int Empathy { get; set; }
        public int Discipline { get; set; }
        public int Ambition { get; set; }
        public int Pragmatism { get; set; }

        public IEnumerable<KeyValuePair<string, int>> All()
        {
            yield return new KeyValuePair<string, int>("courage", Courage);
            yield return new KeyValuePair<string, int>("pride", Pride);
            yield return new KeyValuePair<string, int>("empathy", Empathy);
            yield return new KeyValuePair<string, int>("discipline", Discipline);
            yield return new KeyValuePair<string, int>("ambition", Ambition);
            yield return new KeyValuePair<string, int>("pragmatism", Pragmatism);
        }

        public Traits Clone() => (Traits)MemberwiseClone();
    }

    /// <summary>Отношение героя к мастеру, от которого зависит послушание.</summary>
    public enum MoodStatus
    {
        Normal,
        Discontented,
        OnEdge,
        Boycott
    }

    /// <summary>Быстро меняющееся состояние героя, 0–100.</summary>
    public sealed class HeroState
    {
        public int Fatigue { get; set; }
        public int Stress { get; set; } = 10;
        public int Morale { get; set; } = 60;
        public bool Injured { get; set; }
        public MoodStatus Status { get; set; } = MoodStatus.Normal;
        public int DaysOnEdge { get; set; }
    }

    /// <summary>Убеждение, выведенное из событий. Ключевые воспоминания не забываются.</summary>
    public sealed class Belief
    {
        public string Id { get; set; } = "";
        public string? AboutId { get; set; }
        public string Text { get; set; } = "";
        public int Strength { get; set; }
        public int FormedDay { get; set; }
        public List<long> SourceEventIds { get; set; } = new();
        public bool IsKeyMemory { get; set; }
    }

    /// <summary>Выученный приём: «в такой ситуации такое действие работает».</summary>
    public sealed class Technique
    {
        public string Id { get; set; } = "";
        public string Situation { get; set; } = "";
        public string Action { get; set; } = "";
        public int Strength { get; set; }
        public int Successes { get; set; }
        public int Failures { get; set; }
        public int LastPracticedDay { get; set; }
        public string LearnedFrom { get; set; } = "";
    }

    public sealed class Hero
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Звёздность 1–6: сила и потенциал героя. Растёт с опытом и обучением.</summary>
        public int Stars { get; set; } = 1;
        /// <summary>
        /// Скрытый потолок звёзд — реалистичный предел героя. Игрок его не видит.
        /// Обычные люди упираются в 2–3★; до 6★ способны дорасти единицы.
        /// </summary>
        public int MaxStars { get; set; } = 1;
        /// <summary>Профессия меняется вместе со звёздностью (деревенский парень → кузнец → …).</summary>
        public string Profession { get; set; } = "";
        public string Bio { get; set; } = "";
        public string Dream { get; set; } = "";
        public Traits Traits { get; set; } = new();
        public List<string> Values { get; set; } = new();
        public HeroState State { get; set; } = new();
        public List<Belief> Beliefs { get; set; } = new();
        public List<Technique> Techniques { get; set; } = new();
    }
}
