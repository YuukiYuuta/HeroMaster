using System;
using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Random;

namespace HeroMaster.Core.World
{
    /// <summary>
    /// Всё состояние партии. Сохраняется в один JSON-файл целиком,
    /// включая состояние генератора случайных чисел.
    /// </summary>
    public sealed class GameWorld
    {
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public ulong Seed { get; set; }
        public int Day { get; set; }
        public Pcg32 Rng { get; set; } = new();
        public List<Hero> Heroes { get; set; } = new();
        public List<Relationship> Relationships { get; set; } = new();
        public EventLog Log { get; set; } = new();

        public Hero GetHero(string id) =>
            Heroes.FirstOrDefault(h => h.Id == id)
            ?? throw new KeyNotFoundException($"Нет героя с id «{id}».");

        /// <summary>Что <paramref name="from"/> думает о <paramref name="to"/>.</summary>
        public Relationship GetRelationship(string from, string to) =>
            Relationships.FirstOrDefault(r => r.From == from && r.To == to)
            ?? throw new KeyNotFoundException($"Нет отношения {from}→{to}.");

        public Relationship TowardMaster(string heroId) => GetRelationship(heroId, Ids.Master);

        public static int Clamp(int value, int min = 0, int max = 100) => Math.Max(min, Math.Min(max, value));
    }
}
