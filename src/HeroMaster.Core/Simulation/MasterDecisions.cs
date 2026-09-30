using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>Чем герой занимается на базе. Порядок важен: при равных оценках побеждает раньший.</summary>
    public enum Activity
    {
        Rest,
        Train,
        Socialize,
        Work,
        Brood
    }

    /// <summary>Всё, что мастер решил на этот день. Мастер влияет только поступками, прямого общения нет.</summary>
    public sealed class MasterDecisions
    {
        /// <summary>Кого мастер отправляет на вылазку. Пусто — вылазки нет.</summary>
        public List<string> Team { get; set; } = new();

        /// <summary>Сколько золота из добычи получает каждый герой. Остаток уходит мастеру.</summary>
        public Dictionary<string, int>? LootShares { get; set; }

        /// <summary>Мастер сознательно оставляет всю добычу себе.</summary>
        public bool KeepLoot { get; set; }

        public List<GiftOrder> Gifts { get; set; } = new();

        /// <summary>Намёк герою, чем заняться на базе. Герой сам решает, прислушаться ли.</summary>
        public Dictionary<string, Activity> Hints { get; set; } = new();

        public List<string> Validate(GameWorld world, GameRules rules)
        {
            var errors = new List<string>();
            var config = rules.Config;

            foreach (var id in Team)
                if (!world.HasHero(id))
                    errors.Add($"В команде неизвестный герой «{id}».");
            if (Team.Distinct().Count() != Team.Count)
                errors.Add("Герой указан в команде дважды.");
            if (Team.Count > config.Team.MaxSize)
                errors.Add($"В команде не больше {config.Team.MaxSize} героев.");
            if (Team.Count > 0 && Team.Count < config.Expedition.MinMembers)
                errors.Add($"Для вылазки нужно не меньше {config.Expedition.MinMembers} героев.");

            var pool = world.Master.PendingLoot;
            if (pool != null)
            {
                if (LootShares == null && !KeepLoot)
                    errors.Add($"Есть неподелённая добыча ({pool.Amount} золота): поделите её или оставьте себе.");
                if (LootShares != null && KeepLoot)
                    errors.Add("Нельзя одновременно делить добычу и оставлять её себе.");
            }
            else if (LootShares != null || KeepLoot)
            {
                errors.Add("Делить нечего: добычи нет.");
            }

            if (LootShares != null)
            {
                foreach (var share in LootShares)
                {
                    if (!world.HasHero(share.Key))
                        errors.Add($"Доля для неизвестного героя «{share.Key}».");
                    if (share.Value < 0)
                        errors.Add("Доля не может быть отрицательной.");
                }
                if (pool != null && LootShares.Values.Sum() > pool.Amount)
                    errors.Add($"Раздано больше, чем есть: {LootShares.Values.Sum()} из {pool.Amount}.");
            }

            var kinds = config.Gifts.KindsById();
            int cost = 0;
            foreach (var gift in Gifts)
            {
                if (!world.HasHero(gift.HeroId))
                    errors.Add($"Подарок неизвестному герою «{gift.HeroId}».");
                if (kinds.TryGetValue(gift.KindId, out var kind))
                    cost += kind.Cost;
                else
                    errors.Add($"Неизвестный подарок «{gift.KindId}».");
            }
            if (cost > world.Master.Gold)
                errors.Add($"На подарки нужно {cost} золота, а у мастера {world.Master.Gold}.");

            foreach (var hint in Hints)
            {
                if (!world.HasHero(hint.Key))
                    errors.Add($"Намёк неизвестному герою «{hint.Key}».");
                if (hint.Value == Activity.Brood)
                    errors.Add("Намекнуть «копить обиду» нельзя.");
            }

            return errors;
        }
    }

    public sealed class GiftOrder
    {
        public string HeroId { get; set; } = "";
        public string KindId { get; set; } = "";
    }
}
