using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Battle;
using HeroMaster.Core.Config;
using HeroMaster.Core.Content;

namespace HeroMaster.Core.Simulation
{
    /// <summary>Неизменяемые правила партии: каталог героев, числа баланса, монстры и миссии.</summary>
    public sealed class GameRules
    {
        public GameRules(HeroCatalog catalog, BalanceConfig config, MonsterCatalog monsters, IEnumerable<MissionDefinition> missions)
        {
            Catalog = catalog;
            Config = config;
            Monsters = monsters;
            Missions = missions.ToList();
        }

        public HeroCatalog Catalog { get; }
        public BalanceConfig Config { get; }
        public MonsterCatalog Monsters { get; }
        public IReadOnlyList<MissionDefinition> Missions { get; }

        public MissionDefinition Mission(string id) =>
            Missions.FirstOrDefault(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Нет миссии «{id}».");
    }
}
