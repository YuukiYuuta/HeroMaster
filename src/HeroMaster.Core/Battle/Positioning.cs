using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Battle
{
    /// <summary>
    /// Герои сами выбирают, где держать оборону. У каждой зоны есть настоящая ценность
    /// (укрытие, узость прохода, удалённость от врагов), но герой видит её сквозь своё чутьё:
    /// у новичка оно слабое, он ошибается и часто остаётся там, где высадили.
    /// Сверху — характер: смелые лезут вперёд, трусы цепляются за укрытие, все тянутся к тем, кому доверяют.
    /// Бойкотирующий уходит подальше от всех, отказавшийся драться ищет, где спрятаться.
    /// </summary>
    public static class Positioning
    {
        /// <summary>Чутьё героя на позиции, 0–100: звёзды, опыт и дисциплина.</summary>
        public static int Sense(GameRules rules, Hero hero)
        {
            var c = rules.Config.Battle;
            return GameWorld.Clamp(hero.Stars * c.SensePerStar
                                   + hero.Experience / c.SenseExperienceDivisor
                                   + hero.Traits.Discipline / c.SenseDisciplineDivisor);
        }

        /// <summary>Настоящая ценность зоны для обороны — то, что опытный боец видит сразу.</summary>
        public static int TrueValue(GameRules rules, MissionDefinition mission, ZoneDefinition zone)
        {
            var c = rules.Config.Battle;
            int maxWidth = mission.Zones.Max(z => z.Width);
            return zone.Cover * c.ValuePerCover
                   + (maxWidth - zone.Width) * c.ValuePerNarrowness
                   + mission.DistanceToSpawns(zone.Id) * c.ValuePerDistance;
        }

        public static void ChooseAll(GameWorld world, GameRules rules, BattleState s)
        {
            var mission = s.Mission;
            var candidates = mission.Zones.Where(z => !z.Spawn).ToList();

            // Сначала решают самые авторитетные: звёзды, потом уважение остальных.
            var order = s.Heroes
                .Select(h => new
                {
                    h,
                    respect = s.Heroes.Where(o => o != h).Select(o => world.GetRelationship(o.Hero.Id, h.Hero.Id).Respect).DefaultIfEmpty(0).Sum()
                })
                .OrderByDescending(x => x.h.Hero.Stars)
                .ThenByDescending(x => x.respect)
                .Select(x => x.h)
                .ToList();

            var decided = new List<BattleHero>();
            foreach (var bh in order)
            {
                bh.TargetZone = bh.Conduct == Conduct.Abandoned
                    ? Hideout(mission, candidates, decided)
                    : Choose(world, rules, s, bh, candidates, decided);
                decided.Add(bh);
                Announce(s, bh);
            }
        }

        private static string Choose(GameWorld world, GameRules rules, BattleState s, BattleHero bh, List<ZoneDefinition> candidates, List<BattleHero> decided)
        {
            var c = rules.Config.Battle;
            var hero = bh.Hero;
            var mission = s.Mission;
            int sense = Sense(rules, hero);
            int blur = (100 - sense) / c.SenseBlurDivisor;
            bool passive = bh.Conduct == Conduct.Passive;
            bool coward = hero.Traits.Courage < c.CowardCourageBelow || passive;

            string best = mission.StartZone;
            int bestScore = int.MinValue;
            foreach (var zone in candidates)
            {
                int score = TrueValue(rules, mission, zone) * sense / 100;
                if (blur > 0)
                    score += world.Rng.Range(-blur, blur);
                if (zone.Id == mission.StartZone)
                    score += c.StayBonus * (100 - sense) / 100;
                if (coward)
                    score += zone.Cover * c.CowardCoverBonusPercent / 100;
                if (hero.Traits.Courage > 50 && !passive && mission.DistanceToSpawns(zone.Id) == 1)
                    score += (hero.Traits.Courage - 50) * c.BraveFrontPercent / 100;

                // Стадный инстинкт: испуганные люди жмутся к своим и идут за авторитетом.
                // Одиночек (низкая эмпатия) тянет слабее. Отказавшийся драться думает только о себе.
                if (!passive)
                    foreach (var other in decided.Where(d => d.TargetZone == zone.Id && d.Conduct != Conduct.Abandoned))
                    {
                        score += c.CohesionBonus * (50 + hero.Traits.Empathy) / 100;
                        var r = world.GetRelationship(hero.Id, other.Hero.Id);
                        if (r.Affection >= c.FriendAffectionThreshold || r.Trust >= c.FriendAffectionThreshold)
                            score += c.FriendZoneBonus;
                    }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = zone.Id;
                }
            }
            return best;
        }

        /// <summary>Бойкотирующий уходит туда, где дальше всего от остальных.</summary>
        private static string Hideout(MissionDefinition mission, List<ZoneDefinition> candidates, List<BattleHero> decided)
        {
            var teamZones = decided.Where(d => d.Conduct != Conduct.Abandoned).Select(d => d.TargetZone).DefaultIfEmpty(mission.StartZone).ToList();
            return candidates
                .OrderByDescending(z => teamZones.Min(t => mission.Distance(z.Id, t)))
                .First().Id;
        }

        private static void Announce(BattleState s, BattleHero bh)
        {
            var h = bh.Hero;
            string zone = s.Mission.Zone(bh.TargetZone).Name;
            string text;
            if (bh.Conduct == Conduct.Abandoned)
                text = $"{h.Name} демонстративно {h.G("ушёл", "ушла")} подальше от всех — к позиции «{zone}».";
            else if (bh.Conduct == Conduct.Passive)
                text = $"{h.Name} {h.G("ищет", "ищет")}, где спрятаться: {h.G("направился", "направилась")} к позиции «{zone}».";
            else if (bh.TargetZone == s.Mission.StartZone)
                text = $"{h.Name} {h.G("остался", "осталась")} там, где высадили: «{zone}».";
            else
                text = $"{h.Name} {h.G("решил", "решила")} держать оборону у позиции «{zone}».";
            s.Say("position", text, 3, 0, h.Id);
        }
    }
}
