using HeroMaster.Core.Config;
using HeroMaster.Core.Content;

namespace HeroMaster.Core.Simulation
{
    /// <summary>Неизменяемые правила партии: каталог героев и числа баланса.</summary>
    public sealed class GameRules
    {
        public GameRules(HeroCatalog catalog, BalanceConfig config)
        {
            Catalog = catalog;
            Config = config;
        }

        public HeroCatalog Catalog { get; }
        public BalanceConfig Config { get; }
    }
}
