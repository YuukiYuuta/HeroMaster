using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Model;
using HeroMaster.Core.Simulation;
using HeroMaster.Core.World;

namespace HeroMaster.Core.Battle
{
    /// <summary>
    /// Бой в реальном времени, тик за тиком. Детерминирован: тот же мир и то же зерно дают тот же бой.
    /// Каждый тик: появляются волны → все движутся → в каждой зоне враги и герои бьют друг друга.
    /// Узкая зона не даёт врагам навалиться толпой, укрытие снижает урон.
    /// </summary>
    public static class BattleEngine
    {
        public static BattleState Start(GameWorld world, GameRules rules, MissionDefinition mission, IReadOnlyList<string> team)
        {
            var s = new BattleState { Mission = mission, MonsterCatalog = rules.Monsters };
            var e = rules.Config.Expedition;

            foreach (var id in team)
            {
                var hero = world.GetHero(id);
                var start = Obedience.Evaluate(world, rules, hero, team);
                int maxHp = rules.Config.Battle.HeroBaseHp + Combat.Power(rules, hero);
                var bh = new BattleHero
                {
                    Hero = hero,
                    Zone = mission.StartZone,
                    TargetZone = mission.StartZone,
                    Hp = maxHp,
                    MaxHp = maxHp,
                    StartResponse = start.Response,
                    StartReadiness = start.Readiness,
                    Conduct = Combat.StartingConduct(start.Response),
                    MoveCooldown = rules.Config.Battle.HeroMoveTicks
                };
                s.Heroes.Add(bh);

                bool tired = hero.State.Fatigue >= e.SentTiredThreshold;
                if (tired)
                    TrustRules.Change(world, hero.Id, -e.SentTiredTrustLoss);
                s.Say("conduct", StartText(hero, start.Response, tired), Combat.IsFighting(bh.Conduct) ? 2 : 6, EmotionOf(start.Response), hero.Id);
            }

            s.Say("mission", $"{mission.Name}. {mission.Description}", 4, 0);
            Positioning.ChooseAll(world, rules, s);
            return s;
        }

        public static void RunToEnd(GameWorld world, GameRules rules, BattleState s)
        {
            while (!s.IsOver)
                Step(world, rules, s);
        }

        public static void Step(GameWorld world, GameRules rules, BattleState s)
        {
            if (s.IsOver)
                return;

            s.Tick++;
            SpawnWaves(s);
            Regroup(rules, s);
            MoveHeroes(rules, s);
            MoveMonsters(s);
            foreach (var zone in s.Mission.Zones)
                MonstersAttack(world, rules, s, zone);
            HeroesAct(world, rules, s);
            Breather(rules, s);
            CheckOutcome(rules, s);
        }

        /// <summary>Когда врагов рядом нет, люди переводят дух и чуть приходят в себя — но не полностью.</summary>
        private static void Breather(GameRules rules, BattleState s)
        {
            var c = rules.Config.Battle;
            foreach (var h in s.Heroes.Where(h => h.Alive && h.HpPercent < c.BreatherMaxPercent))
            {
                var near = s.Mission.Zone(h.Zone).Links;
                bool calm = !s.Monsters.Any(m => m.Alive && (m.Zone == h.Zone || near.Contains(m.Zone)));
                if (calm)
                    h.Hp = System.Math.Min(h.Hp + c.BreatherHpPerTick, h.MaxHp * c.BreatherMaxPercent / 100);
            }
        }

        private static void SpawnWaves(BattleState s)
        {
            var waves = s.Mission.Waves;
            while (s.WavesSpawned < waves.Count && waves[s.WavesSpawned].AtTick <= s.Tick)
            {
                var wave = waves[s.WavesSpawned];
                s.WavesSpawned++;
                var parts = new List<string>();
                foreach (var spawn in wave.Spawns)
                {
                    for (int i = 0; i < spawn.Count; i++)
                    {
                        var monsterDef = MonsterOf(s, spawn.Monster);
                        s.Monsters.Add(new BattleMonster
                        {
                            Uid = s.NextMonsterUid++,
                            Def = monsterDef,
                            Zone = spawn.Zone,
                            Hp = monsterDef.Hp,
                            MoveCooldown = monsterDef.MoveTicks,
                            AttackCooldown = 1
                        });
                    }
                    parts.Add($"{spawn.Count} {MonsterOf(s, spawn.Monster).NamePlural} — «{s.Mission.Zone(spawn.Zone).Name}»");
                }
                s.Say("wave", $"Волна {s.WavesSpawned} из {waves.Count}: {string.Join("; ", parts)}.", 5, -1);
            }
        }

        private static MonsterDefinition MonsterOf(BattleState s, string id) => s.MonsterCatalog!.Get(id);

        /// <summary>
        /// Инстинкт самосохранения: оставшись один под ударом и ослабев, человек бежит к своим —
        /// туда, где больше всего товарищей. Бойкотирующий к своим не идёт.
        /// </summary>
        private static void Regroup(GameRules rules, BattleState s)
        {
            var c = rules.Config.Battle;
            foreach (var h in s.Heroes.Where(h => h.Alive && h.Conduct != Conduct.Abandoned && h.Zone == h.TargetZone))
            {
                bool alone = s.Heroes.All(o => o == h || o.Dead || o.Zone != h.Zone);
                bool underAttack = s.Monsters.Any(m => m.Alive && m.Zone == h.Zone);
                if (!alone || !underAttack || h.HpPercent >= c.RegroupBelowPercent)
                    continue;

                var allies = s.Heroes.Where(o => o != h && o.Alive && o.Conduct != Conduct.Abandoned).ToList();
                if (allies.Count == 0)
                    continue;
                var target = allies.GroupBy(o => o.TargetZone)
                    .OrderByDescending(g => g.Count())
                    .ThenBy(g => s.Mission.Distance(h.Zone, g.Key))
                    .First().Key;
                if (target == h.Zone)
                    continue;

                h.TargetZone = target;
                h.MoveCooldown = c.HeroMoveTicks;
                s.Say("regroup", $"{h.Hero.Name}, {h.Hero.G("оставшись", "оставшись")} в одиночку под ударом, отступает к своим — к позиции «{s.Mission.Zone(target).Name}».", 4, -1, h.Hero.Id);
            }
        }

        private static void MoveHeroes(GameRules rules, BattleState s)
        {
            foreach (var h in s.Heroes.Where(h => h.Alive && h.Zone != h.TargetZone))
            {
                if (--h.MoveCooldown > 0)
                    continue;
                h.Zone = s.Mission.NextStep(h.Zone, h.TargetZone);
                h.MoveCooldown = rules.Config.Battle.HeroMoveTicks;
                if (h.Zone == h.TargetZone)
                    s.Say("arrived", $"{h.Hero.Name} на позиции «{s.Mission.Zone(h.Zone).Name}».", 1, 0, h.Hero.Id);
            }
        }

        private static void MoveMonsters(BattleState s)
        {
            var heroZones = s.Heroes.Where(h => h.Alive).Select(h => h.Zone).Distinct().ToList();
            if (heroZones.Count == 0)
                return;

            foreach (var m in s.Monsters.Where(m => m.Alive))
            {
                if (heroZones.Contains(m.Zone))
                    continue;
                if (--m.MoveCooldown > 0)
                    continue;
                // Идут к ближайшим героям; при равенстве — к первым по списку зон.
                string target = heroZones.OrderBy(z => s.Mission.Distance(m.Zone, z)).ThenBy(z => s.Mission.Zones.FindIndex(x => x.Id == z)).First();
                m.Zone = s.Mission.NextStep(m.Zone, target);
                m.MoveCooldown = m.Def.MoveTicks;
            }
        }

        private static void MonstersAttack(GameWorld world, GameRules rules, BattleState s, ZoneDefinition zone)
        {
            var c = rules.Config.Battle;
            var heroesHere = s.Heroes.Where(h => h.Alive && h.Zone == zone.Id).ToList();
            if (heroesHere.Count == 0)
                return;

            // В узком месте враги не могут навалиться толпой: бьют только первые Width.
            var engaged = s.Monsters.Where(m => m.Alive && m.Zone == zone.Id).Take(zone.Width).ToList();
            foreach (var m in engaged)
            {
                if (--m.AttackCooldown > 0)
                    continue;
                m.AttackCooldown = c.MonsterAttackTicks;

                heroesHere = heroesHere.Where(h => h.Alive).ToList();
                if (heroesHere.Count == 0)
                    return;

                var target = PickTarget(world, c, heroesHere);
                int damage = Jitter(world, m.Def.Attack, c.DamageJitterPercent) * (100 - zone.Cover) / 100;
                if (damage < 1) damage = 1;

                if (!Combat.IsFighting(target.Conduct))
                {
                    target.Conduct = Conduct.SelfDefense;
                    var h = target.Hero;
                    s.Say("self_defense", $"{h.Name} {h.G("оказался", "оказалась")} под ударом — и {h.G("начал", "начала")} отбиваться. Другого способа выжить нет.", 4, -1, h.Id);
                }
                target.WasAttacked = true;
                Hit(world, rules, s, target, damage, m.Def.Name);
            }
        }

        /// <summary>Монстр чаще бьёт тех, кто стоит в первом ряду.</summary>
        private static BattleHero PickTarget(GameWorld world, Config.BattleConfig c, List<BattleHero> heroes)
        {
            var weights = heroes.Select(h => h.Hero.CombatRole == CombatRole.Melee && Combat.IsFighting(h.Conduct) ? c.FrontlineTargetWeight : 1).ToList();
            int roll = world.Rng.Next(weights.Sum());
            for (int i = 0; i < heroes.Count; i++)
            {
                if (roll < weights[i]) return heroes[i];
                roll -= weights[i];
            }
            return heroes[0];
        }

        private static void Hit(GameWorld world, GameRules rules, BattleState s, BattleHero target, int damage, string attacker)
        {
            var c = rules.Config.Battle;
            var h = target.Hero;
            int dealt = System.Math.Min(damage, target.Hp);
            target.Hp -= dealt;
            target.DamageTaken += dealt;

            if (target.Hp <= 0)
            {
                target.Dead = true;
                target.KilledBy = attacker;
                s.Say("death", $"{h.Name} {h.G("пал", "пала")} в бою. Убийца — {attacker.ToLowerInvariant()}.", 10, -5, h.Id);
                // Гибель на глазах бьёт по духу сильнее ранения: проверяем дважды.
                foreach (var w in s.Heroes.Where(x => x.Alive))
                    for (int i = 0; i < 2; i++)
                        BreakMorale(world, rules, s, w, target, "гибель");
                return;
            }

            if (!target.Injured && target.Hp * 100 < target.MaxHp * c.InjuredBelowPercent)
            {
                target.Injured = true;
                s.Say("injured", $"{h.Name} {h.G("ранен", "ранена")}: держится из последних сил.", 6, -3, h.Id);
                foreach (var w in s.Heroes.Where(x => x.Alive && x != target && SeesEachOther(s, x, target)))
                    BreakMorale(world, rules, s, w, target, "ранение");
            }
        }

        private static bool SeesEachOther(BattleState s, BattleHero a, BattleHero b) =>
            a.Zone == b.Zone || s.Mission.Zone(a.Zone).Links.Contains(b.Zone);

        private static void BreakMorale(GameWorld world, GameRules rules, BattleState s, BattleHero witness, BattleHero victim, string what)
        {
            var before = witness.Conduct;
            witness.Conduct = Combat.WitnessInjury(world, rules, witness.Hero, victim.Hero, before);
            if (witness.Conduct == before)
                return;

            var w = witness.Hero;
            string text = witness.Conduct == Conduct.Passive
                ? $"Увидев {what} {victim.Hero.NameGenitive}, {w.Name} {w.G("оцепенел", "оцепенела")} от страха и перестал{w.G("", "а")} сражаться."
                : $"Увидев {what} {victim.Hero.NameGenitive}, {w.Name} {w.G("пал", "пала")} духом и сражается хуже.";
            s.Say("morale_broken", text, 5, -2, w.Id, victim.Hero.Id);
        }

        private static void HeroesAct(GameWorld world, GameRules rules, BattleState s)
        {
            var c = rules.Config.Battle;
            foreach (var bh in s.Heroes.Where(h => h.Alive))
            {
                if (--bh.AttackCooldown > 0)
                    continue;
                if (!Combat.IsFighting(bh.Conduct))
                    continue;

                if (bh.Hero.CombatRole == CombatRole.Healer && TryHeal(rules, s, bh))
                {
                    bh.AttackCooldown = c.HealTicks;
                    continue;
                }

                var target = PickMonster(s, bh);
                if (target == null)
                    continue;

                int damage = Combat.EffectivePower(rules, bh.Hero, bh.Conduct) * c.HeroDamagePercent / 100;
                if (bh.Hero.CombatRole == CombatRole.Healer)
                    damage = damage * c.HealerAttackPercent / 100;
                damage = System.Math.Max(1, Jitter(world, damage, c.DamageJitterPercent));

                int dealt = System.Math.Min(damage, target.Hp);
                target.Hp -= dealt;
                bh.DamageDealt += dealt;
                bh.AttackCooldown = c.HeroAttackTicks;

                if (target.Hp <= 0)
                {
                    target.Dead = true;
                    bh.Kills++;
                    s.Loot += target.Def.Loot;
                    s.Say("kill", $"{bh.Hero.Name} {bh.Hero.G("сразил", "сразила")} врага ({target.Def.Name.ToLowerInvariant()}).", 2, 1, bh.Hero.Id);
                }
            }
        }

        /// <summary>Лекарь лечит самого израненного союзника в своей зоне.</summary>
        private static bool TryHeal(GameRules rules, BattleState s, BattleHero healer)
        {
            var c = rules.Config.Battle;
            var patient = s.Heroes
                .Where(h => h.Alive && h.Zone == healer.Zone && h.HpPercent < c.HealBelowPercent)
                .OrderBy(h => h.HpPercent)
                .FirstOrDefault();
            if (patient == null)
                return false;

            int healed = System.Math.Min(c.HealAmount, patient.MaxHp - patient.Hp);
            patient.Hp += healed;
            healer.Healed += healed;
            return healed > 0;
        }

        /// <summary>Сначала добить самого слабого врага рядом. Стрелок достаёт и до соседней зоны.</summary>
        private static BattleMonster? PickMonster(BattleState s, BattleHero bh)
        {
            var here = s.Monsters.Where(m => m.Alive && m.Zone == bh.Zone).OrderBy(m => m.Hp).ThenBy(m => m.Uid).FirstOrDefault();
            if (here != null || bh.Hero.CombatRole != CombatRole.Ranged)
                return here;

            var links = s.Mission.Zone(bh.Zone).Links;
            return s.Monsters.Where(m => m.Alive && links.Contains(m.Zone)).OrderBy(m => m.Hp).ThenBy(m => m.Uid).FirstOrDefault();
        }

        private static void CheckOutcome(GameRules rules, BattleState s)
        {
            if (s.Heroes.All(h => h.Dead))
            {
                s.Outcome = BattleOutcome.Defeat;
                s.Say("defeat", "Отряд погиб. Никто не вернётся на базу.", 10, -5);
                return;
            }

            bool allWaves = s.WavesSpawned >= s.TotalWaves;
            bool noMonsters = s.Monsters.All(m => m.Dead);
            if ((allWaves && noMonsters) || s.Tick >= rules.Config.Battle.MaxTicks)
            {
                s.Outcome = BattleOutcome.Victory;
                int fallen = s.Heroes.Count(h => h.Dead);
                s.Say("victory", fallen == 0
                    ? $"Оборона выдержала все {s.TotalWaves} волн. Все живы."
                    : $"Оборона выдержала все {s.TotalWaves} волн, но не все вернутся: павших — {fallen}.",
                    7, fallen == 0 ? 3 : -2);
            }
        }

        private static int Jitter(GameWorld world, int value, int percent)
        {
            if (percent <= 0 || value <= 0)
                return value;
            return value * (100 + world.Rng.Range(-percent, percent)) / 100;
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
                    return $"{hero.Name} {hero.G("заявил", "заявила")}, что драться не станет.";
                default:
                    return $"{hero.Name} демонстративно бросает отряд — бойкот. Остальные остаются без прикрытия.";
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
