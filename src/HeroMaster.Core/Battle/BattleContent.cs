using System.Collections.Generic;
using System.Linq;

namespace HeroMaster.Core.Battle
{
    /// <summary>Описание монстра из data/monsters.json.</summary>
    public sealed class MonsterDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Родительный падеж множественного числа: «4 гоблинов» не говорят, а «волна из 4 гоблинов» — да.</summary>
        public string NamePlural { get; set; } = "";
        public int Hp { get; set; }
        public int Attack { get; set; }
        /// <summary>За сколько тиков монстр переходит в соседнюю зону.</summary>
        public int MoveTicks { get; set; } = 4;
        /// <summary>Сколько золота даёт убитый монстр.</summary>
        public int Loot { get; set; }
    }

    public sealed class MonsterCatalog
    {
        public List<MonsterDefinition> Monsters { get; set; } = new();

        public MonsterDefinition Get(string id) =>
            Monsters.FirstOrDefault(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Нет монстра «{id}».");

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>();
            foreach (var m in Monsters)
            {
                if (!ids.Add(m.Id)) errors.Add($"Повторяется id монстра «{m.Id}».");
                if (m.Hp < 1 || m.Attack < 0 || m.MoveTicks < 1 || m.Loot < 0)
                    errors.Add($"Монстр «{m.Id}»: hp ≥ 1, attack ≥ 0, moveTicks ≥ 1, loot ≥ 0.");
            }
            return errors;
        }
    }

    /// <summary>Зона карты. Узкая зона не даёт врагам навалиться толпой, укрытие снижает урон.</summary>
    public sealed class ZoneDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Координаты для рисования в панели, 0–100.</summary>
        public int X { get; set; }
        public int Y { get; set; }
        /// <summary>Сколько врагов одновременно могут атаковать в этой зоне.</summary>
        public int Width { get; set; } = 4;
        /// <summary>На сколько процентов укрытие снижает получаемый урон.</summary>
        public int Cover { get; set; }
        /// <summary>Отсюда приходят враги; герои тут не занимают позицию.</summary>
        public bool Spawn { get; set; }
        public List<string> Links { get; set; } = new();
    }

    public sealed class WaveSpawn
    {
        public string Monster { get; set; } = "";
        public int Count { get; set; }
        public string Zone { get; set; } = "";
    }

    public sealed class WaveDefinition
    {
        public int AtTick { get; set; }
        public List<WaveSpawn> Spawns { get; set; } = new();
    }

    /// <summary>Миссия из data/missions/*.json: карта из зон и волны врагов.</summary>
    public sealed class MissionDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string StartZone { get; set; } = "";
        public List<ZoneDefinition> Zones { get; set; } = new();
        public List<WaveDefinition> Waves { get; set; } = new();

        public ZoneDefinition Zone(string id) =>
            Zones.FirstOrDefault(z => z.Id == id)
            ?? throw new KeyNotFoundException($"В миссии «{Id}» нет зоны «{id}».");

        /// <summary>Число переходов между зонами (поиск в ширину). -1 — пути нет.</summary>
        public int Distance(string from, string to)
        {
            if (from == to) return 0;
            var seen = new HashSet<string> { from };
            var frontier = new List<string> { from };
            for (int d = 1; frontier.Count > 0; d++)
            {
                var next = new List<string>();
                foreach (var id in frontier)
                    foreach (var link in Zone(id).Links)
                    {
                        if (link == to) return d;
                        if (seen.Add(link)) next.Add(link);
                    }
                frontier = next;
            }
            return -1;
        }

        /// <summary>Следующая зона на кратчайшем пути. При равных путях — первая по списку связей (детерминированно).</summary>
        public string NextStep(string from, string to)
        {
            if (from == to) return from;
            int best = int.MaxValue;
            string step = from;
            foreach (var link in Zone(from).Links)
            {
                int d = Distance(link, to);
                if (d >= 0 && d < best)
                {
                    best = d;
                    step = link;
                }
            }
            return step;
        }

        /// <summary>Расстояние до ближайшей зоны, откуда приходят враги.</summary>
        public int DistanceToSpawns(string zone) =>
            Zones.Where(z => z.Spawn).Select(z => Distance(zone, z.Id)).Where(d => d >= 0).DefaultIfEmpty(0).Min();

        public List<string> Validate(MonsterCatalog monsters)
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(Zones.Select(z => z.Id));
            if (!ids.Contains(StartZone)) errors.Add($"Миссия «{Id}»: нет стартовой зоны «{StartZone}».");
            if (!Zones.Any(z => z.Spawn)) errors.Add($"Миссия «{Id}»: нет зоны появления врагов.");
            foreach (var z in Zones)
            {
                foreach (var link in z.Links)
                {
                    if (!ids.Contains(link)) errors.Add($"Зона «{z.Id}» связана с несуществующей «{link}».");
                    else if (!Zone(link).Links.Contains(z.Id)) errors.Add($"Связь «{z.Id}» → «{link}» должна быть в обе стороны.");
                }
                if (z.Width < 1 || z.Cover < 0 || z.Cover > 90) errors.Add($"Зона «{z.Id}»: width ≥ 1, cover 0–90.");
            }
            foreach (var w in Waves)
                foreach (var s in w.Spawns)
                {
                    if (monsters.Monsters.All(m => m.Id != s.Monster)) errors.Add($"Волна на тике {w.AtTick}: неизвестный монстр «{s.Monster}».");
                    if (!ids.Contains(s.Zone)) errors.Add($"Волна на тике {w.AtTick}: неизвестная зона «{s.Zone}».");
                    if (s.Count < 1) errors.Add($"Волна на тике {w.AtTick}: count ≥ 1.");
                }
            if (Waves.Count == 0) errors.Add($"Миссия «{Id}»: нет волн.");
            return errors;
        }
    }
}
