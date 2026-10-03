using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;

namespace HeroMaster.Core.Battle
{
    public enum BattleOutcome
    {
        Running,
        Victory,
        Defeat
    }

    /// <summary>Герой в бою: где стоит, куда идёт, сколько здоровья, как сражается и чего добился.</summary>
    public sealed class BattleHero
    {
        public Hero Hero { get; set; } = null!;
        public string Zone { get; set; } = "";
        public string TargetZone { get; set; } = "";
        public int MoveCooldown { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public Conduct Conduct { get; set; }
        public OrderResponse StartResponse { get; set; }
        public int StartReadiness { get; set; }
        public int AttackCooldown { get; set; }
        public bool WasAttacked { get; set; }
        public bool Injured { get; set; }
        public bool Dead { get; set; }
        public string KilledBy { get; set; } = "";
        public int Kills { get; set; }
        public int DamageDealt { get; set; }
        public int DamageTaken { get; set; }
        public int Healed { get; set; }

        public bool Alive => !Dead;
        public int HpPercent => MaxHp == 0 ? 0 : Hp * 100 / MaxHp;
        /// <summary>Очки для выбора MVP: убийства весят больше всего, затем урон и лечение.</summary>
        public int Score => Kills * 10 + DamageDealt + Healed;
    }

    public sealed class BattleMonster
    {
        public int Uid { get; set; }
        public MonsterDefinition Def { get; set; } = null!;
        public string Zone { get; set; } = "";
        public int Hp { get; set; }
        public int MoveCooldown { get; set; }
        public int AttackCooldown { get; set; }
        public bool Dead { get; set; }
        public bool Alive => !Dead;
    }

    /// <summary>Строка ленты боя. Важные строки после боя попадают в общий журнал.</summary>
    public sealed class BattleFeedEntry
    {
        public int Tick { get; set; }
        public string Kind { get; set; } = "";
        public string Text { get; set; } = "";
        public string[] Actors { get; set; } = new string[0];
        public int Importance { get; set; }
        public int Emotion { get; set; }
    }

    /// <summary>Всё состояние идущего боя. Живёт в памяти, пока бой не закончится.</summary>
    public sealed class BattleState
    {
        public MissionDefinition Mission { get; set; } = null!;
        public MonsterCatalog? MonsterCatalog { get; set; }
        public int Tick { get; set; }
        public List<BattleHero> Heroes { get; } = new();
        public List<BattleMonster> Monsters { get; } = new();
        public int WavesSpawned { get; set; }
        public int NextMonsterUid { get; set; } = 1;
        public int Loot { get; set; }
        public List<BattleFeedEntry> Feed { get; } = new();
        public BattleOutcome Outcome { get; set; } = BattleOutcome.Running;

        public bool IsOver => Outcome != BattleOutcome.Running;
        public int TotalWaves => Mission.Waves.Count;
        public int MonstersKilled => Monsters.Count(m => m.Dead);

        /// <summary>Лучший боец по очкам; при равенстве — тот, кто раньше в списке.</summary>
        public BattleHero? Mvp => Heroes.Where(h => h.Score > 0).OrderByDescending(h => h.Score).FirstOrDefault();

        public BattleFeedEntry Say(string kind, string text, int importance, int emotion, params string[] actors)
        {
            var e = new BattleFeedEntry { Tick = Tick, Kind = kind, Text = text, Importance = importance, Emotion = emotion, Actors = actors };
            Feed.Add(e);
            return e;
        }
    }
}
