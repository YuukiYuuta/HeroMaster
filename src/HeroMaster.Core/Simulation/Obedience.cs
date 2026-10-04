using System.Collections.Generic;
using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    public enum OrderResponse
    {
        Enthusiastic,
        Complies,
        Grudging,
        Refuses,
        Boycott
    }

    public sealed class ObedienceResult
    {
        public int Readiness { get; set; }
        public OrderResponse Response { get; set; }

        /// <summary>Сражается ли герой в рейде (хотя бы вполсилы).</summary>
        public bool Fights => Response == OrderResponse.Enthusiastic
                              || Response == OrderResponse.Complies
                              || Response == OrderResponse.Grudging;
    }

    /// <summary>
    /// Послушание в бою. На вылазку герой идёт всегда — так велит контракт призыва.
    /// Но в самом рейде он решает, как сражаться: охотно, честно, вполсилы или вовсе отказаться драться.
    /// Готовность считает код: доверие + поправки на решимость, дисциплину, усталость, стресс, состав команды
    /// и убеждения о мастере (старые обиды помнятся, даже когда доверие уже вернулось).
    /// </summary>
    public static class Obedience
    {
        public const string NoExplanationValueId = "demands_explanations";

        public static ObedienceResult Evaluate(GameWorld world, GameRules rules, Hero hero, IReadOnlyList<string> team)
        {
            var c = rules.Config.Obedience;

            if (hero.State.Status == MoodStatus.Boycott)
                return new ObedienceResult { Readiness = 0, Response = OrderResponse.Boycott };

            int readiness = world.TowardMaster(hero.Id).Trust
                            + (hero.State.Resolve - 50) * c.ResolvePercent / 100
                            + (hero.Traits.Discipline - 50) * c.DisciplinePercent / 100
                            - hero.State.Fatigue * c.FatiguePercent / 100
                            - hero.State.Stress * c.StressPercent / 100
                            + Memory.Reflection.MasterAttitude(rules, hero);

            foreach (var otherId in team)
            {
                if (otherId == hero.Id)
                    continue;
                var r = world.GetRelationship(hero.Id, otherId);
                if (r.Rivalry >= c.RivalryThreshold)
                    readiness -= c.RivalInTeamPenalty;
                if (r.Affection >= c.FriendAffectionThreshold)
                    readiness += c.FriendInTeamBonus;
            }

            if (hero.Values.Contains(NoExplanationValueId))
                readiness -= c.NoExplanationPenalty;

            if (c.Jitter > 0)
                readiness += world.Rng.Range(-c.Jitter, c.Jitter);

            OrderResponse response;
            if (readiness >= c.EnthusiasticFrom) response = OrderResponse.Enthusiastic;
            else if (readiness >= c.CompliesFrom) response = OrderResponse.Complies;
            else if (readiness >= c.GrudgingFrom) response = OrderResponse.Grudging;
            else response = OrderResponse.Refuses;

            return new ObedienceResult { Readiness = readiness, Response = response };
        }
    }
}
