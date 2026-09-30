using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>Фаза решений мастера: делёж добычи и подарки.</summary>
    public static class MasterPhase
    {
        public static void Run(GameWorld world, GameRules rules, MasterDecisions decisions)
        {
            ShareLoot(world, rules, decisions);
            GiveGifts(world, rules, decisions);
        }

        private static void ShareLoot(GameWorld world, GameRules rules, MasterDecisions decisions)
        {
            var pool = world.Master.PendingLoot;
            if (pool == null)
                return;

            var c = rules.Config.Loot;
            int day = world.Day;
            int given = decisions.LootShares?.Values.Sum() ?? 0;

            if (decisions.KeepLoot || given == 0)
            {
                foreach (var id in pool.Participants)
                    TrustRules.Change(world, id, -c.KeptAllTrustLoss);
                world.Master.Gold += pool.Amount;
                world.Log.Append(day, DayPhase.MasterDecisions, "loot_kept",
                    $"Мастер оставил всю добычу себе ({pool.Amount} золота). Участники вылазки ничего не получили.",
                    importance: 6, emotion: -3,
                    actors: pool.Participants.Append(Ids.Master).ToArray());
                world.Master.PendingLoot = null;
                return;
            }

            int equalShare = pool.Amount / pool.Participants.Count;
            foreach (var hero in world.Heroes)
            {
                int got = 0;
                decisions.LootShares!.TryGetValue(hero.Id, out got);
                hero.Purse += got;

                if (!pool.Participants.Contains(hero.Id))
                {
                    if (got > 0)
                        world.Log.Append(day, DayPhase.MasterDecisions, "loot_share",
                            $"{hero.Name} {hero.G("получил", "получила")} {got} золота из добычи, хотя не {hero.G("ходил", "ходила")} на вылазку.",
                            importance: 2, emotion: 1, actors: new[] { hero.Id, Ids.Master });
                    continue;
                }

                // Гордые ждут доли больше равной.
                int expected = equalShare * (100 + hero.Traits.Pride * c.PrideExpectationPercent / 100) / 100;
                if (expected <= 0)
                    expected = 1;
                int diffPercent = (got - expected) * 100 / expected;

                if (diffPercent >= -c.FairTolerancePercent)
                {
                    TrustRules.Change(world, hero.Id, c.FairShareTrustGain);
                    world.Log.Append(day, DayPhase.MasterDecisions, "loot_share",
                        $"{hero.Name} {hero.G("получил", "получила")} {got} золота — честная доля.",
                        importance: 2, emotion: 1, actors: new[] { hero.Id, Ids.Master });
                }
                else
                {
                    int shortfall = -diffPercent - c.FairTolerancePercent;
                    int loss = System.Math.Min(c.MaxTrustChange, (shortfall + 9) / 10 * c.TrustLossPerTenPercent);
                    TrustRules.Change(world, hero.Id, -loss);
                    world.Log.Append(day, DayPhase.MasterDecisions, "loot_share",
                        $"{hero.Name} {hero.G("получил", "получила")} только {got} золота, хотя {hero.G("рассчитывал", "рассчитывала")} примерно на {expected}.",
                        importance: 5, emotion: -2, actors: new[] { hero.Id, Ids.Master });
                }
            }

            world.Master.Gold += pool.Amount - given;
            world.Master.PendingLoot = null;
        }

        private static void GiveGifts(GameWorld world, GameRules rules, MasterDecisions decisions)
        {
            var c = rules.Config.Gifts;
            var kinds = c.KindsById();
            int day = world.Day;

            foreach (var order in decisions.Gifts)
            {
                var hero = world.GetHero(order.HeroId);
                var kind = kinds[order.KindId];
                world.Master.Gold -= kind.Cost;

                int value = kind.BaseTrust;
                if (!string.IsNullOrEmpty(kind.Trait))
                    value += hero.Traits.Get(kind.Trait) * kind.TraitBonusMax / 100;

                // Частые подарки обесцениваются: похоже на подкуп.
                hero.State.GiftDays.RemoveAll(d => day - d >= c.RepeatWindowDays);
                foreach (var _ in hero.State.GiftDays)
                    value = value * c.RepeatValuePercent / 100;
                hero.State.GiftDays.Add(day);

                int delta = TrustRules.Change(world, hero.Id, value);
                string reaction = delta > 0
                    ? $"{hero.Name} {hero.G("принял", "приняла")} подарок."
                    : $"{hero.Name} лишь пожал{hero.G("", "а")} плечами: подарки сыплются слишком часто.";
                world.Log.Append(day, DayPhase.MasterDecisions, "master_gift",
                    $"Подарок от мастера для {hero.NameGenitive}: {kind.Name}. {reaction}",
                    importance: 3, emotion: delta > 0 ? 2 : 0, actors: new[] { hero.Id, Ids.Master });
            }
        }
    }
}
