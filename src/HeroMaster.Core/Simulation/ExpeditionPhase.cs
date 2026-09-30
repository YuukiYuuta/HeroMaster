using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Simulation
{
    /// <summary>
    /// Вылазка — несколько стычек. Отказаться идти герой не может: контракт призыва.
    /// В рейд каждый входит со своим настроем (<see cref="Obedience"/>), но настрой меняется по ходу боя:
    /// ранение товарища на глазах может сломить дух, а тот, кто отказывался драться, начинает отбиваться,
    /// когда нападают на него самого — другого способа выжить нет.
    /// Исход решает в первую очередь сила героев (<see cref="Combat.Power"/>), настрой лишь немного её сдвигает.
    /// Настоящий автономный бой с советами мастера появится в группе 3.
    /// </summary>
    public static class ExpeditionPhase
    {
        private sealed class Member
        {
            public Hero Hero = null!;
            public ObedienceResult Start = null!;
            public Conduct Conduct;
            public bool InjuredHere;
        }

        /// <summary>Возвращает id героев, которые были на вылазке.</summary>
        public static List<string> Run(GameWorld world, GameRules rules, IReadOnlyList<string> team)
        {
            if (team.Count == 0)
                return new List<string>();

            var c = rules.Config.Expedition;
            int day = world.Day;

            var members = new List<Member>();
            foreach (var id in team)
            {
                var hero = world.GetHero(id);
                var start = Obedience.Evaluate(world, rules, hero, team);
                members.Add(new Member { Hero = hero, Start = start, Conduct = Combat.StartingConduct(start.Response) });
            }

            foreach (var m in members)
            {
                bool tired = m.Hero.State.Fatigue >= c.SentTiredThreshold;
                if (tired)
                    TrustRules.Change(world, m.Hero.Id, -c.SentTiredTrustLoss);

                var e = world.Log.Append(day, DayPhase.Expedition, "battle_conduct",
                    StartText(m.Hero, m.Start.Response, tired),
                    importance: Combat.IsFighting(m.Conduct) ? 2 : 6,
                    emotion: EmotionOf(m.Start.Response),
                    actors: new[] { m.Hero.Id, Ids.Master });
                e.Data["response"] = m.Start.Response.ToString();
                e.Data["readiness"] = m.Start.Readiness.ToString();
            }

            int loot = 0;
            for (int encounter = 1; encounter <= c.Encounters; encounter++)
                loot += Encounter(world, rules, members, encounter);

            Finish(world, rules, members, loot);
            return members.Select(m => m.Hero.Id).ToList();
        }

        /// <summary>Одна стычка: отряд бьётся, монстры атакуют. Возвращает добытое золото.</summary>
        private static int Encounter(GameWorld world, GameRules rules, List<Member> members, int number)
        {
            var c = rules.Config.Expedition;
            int day = world.Day;

            int teamPower = members.Sum(m => Combat.EffectivePower(rules, m.Hero, m.Conduct, m.InjuredHere));
            int jitter = world.Rng.Range(-c.LootJitterPercent, c.LootJitterPercent);
            int loot = teamPower * c.LootPerHundredPower / 100 * (100 + jitter) / 100;

            for (int attack = 0; attack < c.AttacksPerEncounter; attack++)
            {
                var target = members[world.Rng.Next(members.Count)];
                var hero = target.Hero;

                // Бойкот бойкотом, а жить хочется.
                if (!Combat.IsFighting(target.Conduct))
                {
                    target.Conduct = Conduct.SelfDefense;
                    world.Log.Append(day, DayPhase.Expedition, "self_defense",
                        $"Стычка {number}: {hero.Name} {hero.G("оказался", "оказалась")} под ударом — и {hero.G("начал", "начала")} отбиваться. Другого способа выжить нет.",
                        importance: 4, emotion: -1, actors: new[] { hero.Id });
                }

                // Кто бросил позицию или отсиживается, оголяет фланг остальным.
                int flank = members.Where(m => m != target).Sum(m =>
                    m.Conduct == Conduct.Abandoned ? c.BoycottDangerPercent :
                    m.Conduct == Conduct.Passive ? c.RefuseDangerPercent : 0);

                int enemy = world.Rng.Range(c.EnemyPowerMin, c.EnemyPowerMax);
                int gap = System.Math.Max(0, enemy - Combat.Power(rules, hero, target.InjuredHere));
                int injuryChance = c.InjuryChancePercent + gap * c.InjuryPowerGapPercent / 100
                                   + hero.State.Fatigue / 10 * c.InjuryChancePerTenFatigue + flank;

                if (!target.InjuredHere && world.Rng.Chance(injuryChance))
                    Injure(world, rules, members, target, number);
            }

            return loot;
        }

        private static void Injure(GameWorld world, GameRules rules, List<Member> members, Member target, int number)
        {
            var c = rules.Config.Expedition;
            var hero = target.Hero;
            int day = world.Day;

            // Рана действует до конца вылазки; на базе герой полностью восстановится, но пережитое останется.
            target.InjuredHere = true;
            hero.State.Resolve = GameWorld.Clamp(hero.State.Resolve - rules.Config.Resolve.InjuryLoss);
            hero.State.Stress = GameWorld.Clamp(hero.State.Stress + c.InjuryStress);
            TrustRules.Change(world, hero.Id, -c.InjuredTrustLoss);
            world.Log.Append(day, DayPhase.Expedition, "injured",
                $"Стычка {number}: {hero.Name} {hero.G("ранен", "ранена")} и до конца вылазки сражается вполсилы.",
                importance: 7, emotion: -3, actors: new[] { hero.Id });

            // Остальные видят это — и кто-то падает духом прямо в бою.
            foreach (var witness in members.Where(m => m != target))
            {
                var before = witness.Conduct;
                witness.Conduct = Combat.WitnessInjury(world, rules, witness.Hero, hero, before);
                if (witness.Conduct == before)
                    continue;

                var w = witness.Hero;
                string text = witness.Conduct == Conduct.Passive
                    ? $"Увидев ранение {hero.NameGenitive}, {w.Name} {w.G("оцепенел", "оцепенела")} от страха и перестал{w.G("", "а")} сражаться."
                    : $"Увидев ранение {hero.NameGenitive}, {w.Name} {w.G("пал", "пала")} духом и сражается хуже.";
                world.Log.Append(day, DayPhase.Expedition, "morale_broken", text,
                    importance: 5, emotion: -2, actors: new[] { w.Id, hero.Id });
            }
        }

        private static void Finish(GameWorld world, GameRules rules, List<Member> members, int loot)
        {
            var c = rules.Config.Expedition;
            int day = world.Day;

            foreach (var m in members)
            {
                var hero = m.Hero;
                hero.State.Fatigue = GameWorld.Clamp(hero.State.Fatigue + c.FatigueGain);
                hero.State.Stress = GameWorld.Clamp(hero.State.Stress + c.StressGainMax * (100 - hero.Traits.Courage) / 100);

                // Опыт — за то, что сражался; отбивавшийся ради выживания получает половину.
                int xp = c.ExperienceGain * rules.Config.Tier(hero.Stars).LearningSpeedPercent / 100;
                if (m.Conduct == Conduct.SelfDefense)
                    hero.Experience += xp / 2;
                else if (Combat.IsFighting(m.Conduct))
                    hero.Experience += xp;

                if (!m.InjuredHere)
                {
                    hero.State.Resolve = GameWorld.Clamp(hero.State.Resolve + rules.Config.Resolve.ExpeditionSurvivedGain);
                    if (Combat.IsFighting(m.Conduct) && m.Conduct != Conduct.SelfDefense)
                        TrustRules.Change(world, hero.Id, c.SuccessTrustGain);
                }
            }

            var ids = members.Select(m => m.Hero.Id).ToList();
            string names = string.Join(", ", members.Select(m => m.Hero.Name));
            if (loot > 0)
            {
                world.Master.PendingLoot = new LootPool { Amount = loot, FromDay = day, Participants = ids };
                world.Log.Append(day, DayPhase.Expedition, "expedition_result",
                    $"Вылазка: {names} вернулись с добычей — {loot} золота. Её ещё предстоит поделить.",
                    importance: 5, emotion: 2, actors: ids.Append(Ids.Master).ToArray());
            }
            else
            {
                world.Log.Append(day, DayPhase.Expedition, "expedition_result",
                    $"Вылазка: {names} вернулись ни с чем.",
                    importance: 6, emotion: -2, actors: ids.Append(Ids.Master).ToArray());
            }
        }

        private static string StartText(Hero hero, OrderResponse response, bool tired)
        {
            string tiredNote = tired ? " Хотя еле держится на ногах." : "";
            switch (response)
            {
                case OrderResponse.Enthusiastic:
                    return $"{hero.Name} рвётся в бой.{tiredNote}";
                case OrderResponse.Complies:
                    return $"{hero.Name} {hero.G("готов", "готова")} честно сражаться.{tiredNote}";
                case OrderResponse.Grudging:
                    return $"{hero.Name} идёт в рейд с явной неохотой.{tiredNote}";
                case OrderResponse.Refuses:
                    return $"{hero.Name} {hero.G("заявил", "заявила")}, что драться не станет, и держится позади.";
                default:
                    return $"{hero.Name} демонстративно бросает позицию — бойкот. Остальные остаются без прикрытия.";
            }
        }

        private static int EmotionOf(OrderResponse response)
        {
            switch (response)
            {
                case OrderResponse.Enthusiastic: return 2;
                case OrderResponse.Complies: return 0;
                case OrderResponse.Grudging: return -1;
                case OrderResponse.Refuses: return -3;
                default: return -4;
            }
        }
    }
}
