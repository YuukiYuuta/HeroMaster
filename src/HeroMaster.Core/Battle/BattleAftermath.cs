using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Battle
{
    /// <summary>
    /// Что бой оставляет после себя: опыт, усталость, стресс, доверие, добычу — и павших.
    /// Раны на базе заживают полностью, но пережитое остаётся (стресс, решимость, доверие).
    /// Смерть окончательна: павший уходит из отряда, товарищи горюют, друзья винят мастера.
    /// </summary>
    public static class BattleAftermath
    {
        /// <summary>Переносит итоги боя в мир. Возвращает id выживших участников.</summary>
        public static List<string> Apply(GameWorld world, GameRules rules, BattleState s)
        {
            var e = rules.Config.Expedition;
            var rc = rules.Config.Resolve;
            int day = world.Day;

            // Важные моменты боя попадают в общий журнал — из него потом строится память героев.
            foreach (var f in s.Feed.Where(f => f.Importance >= 3 && f.Kind != "arrived"))
            {
                var ev = world.Log.Append(day, DayPhase.Expedition, "battle_" + f.Kind, f.Text,
                    importance: f.Importance, emotion: f.Emotion,
                    actors: f.Actors.Length > 0 ? f.Actors : s.Heroes.Select(h => h.Hero.Id).ToArray());
                ev.Data["tick"] = f.Tick.ToString();
            }
            foreach (var bh in s.Heroes)
            {
                var conduct = world.Log.Events.LastOrDefault(x => x.Day == day && x.Type == "battle_conduct" && x.Actors.Contains(bh.Hero.Id));
                if (conduct != null)
                    conduct.Data["response"] = bh.StartResponse.ToString();
            }

            foreach (var bh in s.Heroes.Where(h => h.Alive))
            {
                var hero = bh.Hero;
                hero.State.Fatigue = GameWorld.Clamp(hero.State.Fatigue + e.FatigueGain);
                hero.State.Stress = GameWorld.Clamp(hero.State.Stress + e.StressGainMax * (100 - hero.Traits.Courage) / 100);

                int learning = rules.Config.Tier(hero.Stars).LearningSpeedPercent;
                int xp = bh.Conduct == Conduct.SelfDefense ? e.ExperienceGain / 2
                    : Combat.IsFighting(bh.Conduct) ? e.ExperienceGain : 0;
                hero.Experience += (xp + bh.Kills * e.KillExperience) * learning / 100;

                if (bh.Injured)
                {
                    hero.State.Resolve = GameWorld.Clamp(hero.State.Resolve - rc.InjuryLoss);
                    hero.State.Stress = GameWorld.Clamp(hero.State.Stress + e.InjuryStress);
                    TrustRules.Change(world, hero.Id, -e.InjuredTrustLoss);
                }
                else
                {
                    hero.State.Resolve = GameWorld.Clamp(hero.State.Resolve + rc.ExpeditionSurvivedGain);
                    if (Combat.IsFighting(bh.Conduct) && bh.Conduct != Conduct.SelfDefense)
                        TrustRules.Change(world, hero.Id, e.SuccessTrustGain);
                }
            }

            foreach (var dead in s.Heroes.Where(h => h.Dead))
                Bury(world, rules, s, dead);

            var survivors = s.Heroes.Where(h => h.Alive).Select(h => h.Hero.Id).ToList();
            Summarize(world, s, survivors);

            if (s.Loot > 0 && survivors.Count > 0)
                world.Master.PendingLoot = new LootPool { Amount = s.Loot, FromDay = day, Participants = survivors };

            return survivors;
        }

        private static void Bury(GameWorld world, GameRules rules, BattleState s, BattleHero dead)
        {
            var e = rules.Config.Expedition;
            var hero = dead.Hero;
            world.Heroes.Remove(hero);
            world.Fallen.Add(new FallenHero
            {
                Id = hero.Id,
                Name = hero.Name,
                Stars = hero.Stars,
                Profession = hero.Profession,
                Day = world.Day,
                Where = s.Mission.Name,
                KilledBy = dead.KilledBy
            });

            // Горе: каждому тяжело, а тем, кто был близок, — вдвойне. Друзья винят мастера.
            foreach (var other in world.Heroes)
            {
                int affection = world.GetRelationship(other.Id, hero.Id).Affection;
                other.State.Stress = GameWorld.Clamp(other.State.Stress + e.DeathGriefStress + affection * e.DeathGriefAffectionPercent / 100);
                if (affection >= e.DeathBlameAffection)
                    TrustRules.Change(world, other.Id, -e.DeathBlameTrustLoss);
            }

            world.Log.Append(world.Day, DayPhase.Expedition, "hero_died",
                $"{hero.Name} ({hero.Profession.ToLowerInvariant()}, {new string('★', hero.Stars)}) {hero.G("погиб", "погибла")} в бою: «{s.Mission.Name}». Отряд скорбит.",
                importance: 10, emotion: -5, actors: world.Heroes.Select(h => h.Id).Append(hero.Id).Append(Ids.Master).ToArray());
        }

        private static void Summarize(GameWorld world, BattleState s, List<string> survivors)
        {
            int day = world.Day;
            var ids = s.Heroes.Select(h => h.Hero.Id).ToList();
            string verdict = s.Outcome == BattleOutcome.Victory ? "Оборона выдержала" : "Отряд погиб";
            var stats = string.Join("; ", s.Heroes.Select(h => $"{h.Hero.Name}: убил {h.Kills}{(h.Healed > 0 ? $", вылечил {h.Healed}" : "")}{(h.Dead ? " — пал" : h.Injured ? " — ранен" : "")}"));
            var result = world.Log.Append(day, DayPhase.Expedition, "expedition_result",
                $"{verdict}: врагов убито {s.MonstersKilled}, добыча — {s.Loot} золота. {stats}.",
                importance: 6, emotion: s.Outcome == BattleOutcome.Victory ? 2 : -5,
                actors: ids.Append(Ids.Master).ToArray());
            result.Data["outcome"] = s.Outcome.ToString();
            result.Data["loot"] = s.Loot.ToString();

            var mvp = s.Mvp;
            if (mvp != null && mvp.Alive)
            {
                var h = mvp.Hero;
                world.Log.Append(day, DayPhase.Expedition, "mvp",
                    $"Лучший в бою — {h.Name}: убито врагов {mvp.Kills}, урон {mvp.DamageDealt}{(mvp.Healed > 0 ? $", вылечено {mvp.Healed}" : "")}.",
                    importance: 6, emotion: 3, actors: new[] { h.Id });
            }
        }
    }
}
