using System;
using System.Linq;
using HeroMaster.Core.Config;
using HeroMaster.Core.Content;
using HeroMaster.Core.Events;
using HeroMaster.Core.Model;
using HeroMaster.Core.Random;

namespace HeroMaster.Core.World
{
    /// <summary>Создаёт новую партию из каталога героев и конфига.</summary>
    public static class WorldFactory
    {
        public static GameWorld Create(HeroCatalog catalog, BalanceConfig config, ulong seed)
        {
            var problems = catalog.Validate().Concat(config.Validate()).ToList();
            if (problems.Count > 0)
                throw new InvalidOperationException("Данные с ошибками:\n" + string.Join("\n", problems));

            var world = new GameWorld
            {
                Seed = seed,
                Day = 0,
                Rng = new Pcg32(seed)
            };

            foreach (var def in catalog.Heroes)
            {
                world.Heroes.Add(new Hero
                {
                    Id = def.Id,
                    Name = def.Name,
                    Stars = def.Stars,
                    MaxStars = def.MaxStars,
                    Profession = def.Profession,
                    Bio = def.Bio,
                    Dream = def.Dream,
                    Traits = def.Traits.Clone(),
                    Values = def.Values.ToList()
                });
            }

            int jitter = config.World.RelationshipJitter;
            foreach (var from in catalog.Heroes)
            {
                foreach (var to in catalog.Heroes)
                {
                    if (from.Id == to.Id)
                        continue;

                    var o = catalog.RelationshipOverrides.FirstOrDefault(x => x.From == from.Id && x.To == to.Id);
                    var d = catalog.DefaultRelationship;
                    var relationship = new Relationship
                    {
                        From = from.Id,
                        To = to.Id,
                        Trust = Pick(o?.Trust, d.Trust, world.Rng, jitter),
                        Respect = Pick(o?.Respect, d.Respect, world.Rng, jitter),
                        Affection = Pick(o?.Affection, d.Affection, world.Rng, jitter),
                        Fear = Pick(o?.Fear, d.Fear, world.Rng, 0),
                        Rivalry = Pick(o?.Rivalry, d.Rivalry, world.Rng, 0)
                    };

                    // Противоположные ценности (разбойник и жрец) — недоверие и соперничество с первого дня.
                    int conflicts = catalog.ValueConflicts(from.Values, to.Values);
                    if (conflicts > 0)
                    {
                        relationship.Trust = GameWorld.Clamp(relationship.Trust - conflicts * config.World.OpposingValuesTrustPenalty);
                        relationship.Rivalry = GameWorld.Clamp(relationship.Rivalry + conflicts * config.World.OpposingValuesRivalry);
                    }

                    world.Relationships.Add(relationship);
                }

                var m = catalog.DefaultMasterRelationship;
                world.Relationships.Add(new Relationship
                {
                    From = from.Id,
                    To = Ids.Master,
                    Trust = from.TrustInMaster,
                    Respect = m.Respect ?? 40,
                    Affection = m.Affection ?? 20,
                    Fear = m.Fear ?? 0,
                    Rivalry = m.Rivalry ?? 0
                });
            }

            world.Log.Append(
                day: 0,
                phase: DayPhase.MorningReport,
                type: "squad_formed",
                summary: "Мастер заключил контракт с отрядом: " + string.Join(", ", world.Heroes.Select(h => h.Name)) + ".",
                importance: 6,
                emotion: 1,
                actors: world.Heroes.Select(h => h.Id).Append(Ids.Master).ToArray());

            return world;
        }

        /// <summary>
        /// Значение, заданное вручную, берётся как есть. Значение по умолчанию получает
        /// небольшой случайный разброс, чтобы каждая партия начиналась чуть иначе.
        /// </summary>
        private static int Pick(int? explicitValue, int? defaultValue, Pcg32 rng, int jitter)
        {
            if (explicitValue.HasValue)
                return explicitValue.Value;
            int value = defaultValue ?? 0;
            if (jitter > 0)
                value += rng.Range(-jitter, jitter);
            return GameWorld.Clamp(value);
        }
    }
}
