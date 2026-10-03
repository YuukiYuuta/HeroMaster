using System.Collections.Generic;
using System.Linq;
using HeroMaster.Core.Model;

namespace HeroMaster.Core.Content
{
    /// <summary>Описание героев и стартовых отношений из data/heroes.json.</summary>
    public sealed class HeroCatalog
    {
        public List<ValueDefinition> ValueCatalog { get; set; } = new();
        public List<HeroDefinition> Heroes { get; set; } = new();
        public RelationshipValues DefaultRelationship { get; set; } = new();
        public RelationshipValues DefaultMasterRelationship { get; set; } = new();
        public List<RelationshipOverride> RelationshipOverrides { get; set; } = new();

        public List<string> Validate()
        {
            var errors = new List<string>();
            var heroIds = new HashSet<string>();
            var valueIds = new HashSet<string>(ValueCatalog.Select(v => v.Id));

            foreach (var hero in Heroes)
            {
                if (string.IsNullOrWhiteSpace(hero.Id))
                    errors.Add($"Герой «{hero.Name}» без id.");
                else if (!heroIds.Add(hero.Id))
                    errors.Add($"Повторяется id героя «{hero.Id}».");
                if (hero.Id == Ids.Master)
                    errors.Add($"id «{Ids.Master}» зарезервирован за мастером.");

                foreach (var trait in hero.Traits.All())
                    if (trait.Value < 0 || trait.Value > 100)
                        errors.Add($"{hero.Id}: черта {trait.Key} = {trait.Value}, ожидается 0–100.");

                foreach (var value in hero.Values)
                    if (!valueIds.Contains(value))
                        errors.Add($"{hero.Id}: ценность «{value}» не описана в valueCatalog.");

                if (hero.TrustInMaster < 0 || hero.TrustInMaster > 100)
                    errors.Add($"{hero.Id}: trustInMaster = {hero.TrustInMaster}, ожидается 0–100.");

                if (hero.Stars < 1 || hero.Stars > 6)
                    errors.Add($"{hero.Id}: stars = {hero.Stars}, ожидается 1–6.");
                if (hero.MaxStars < hero.Stars || hero.MaxStars > 6)
                    errors.Add($"{hero.Id}: maxStars = {hero.MaxStars}, ожидается от stars ({hero.Stars}) до 6.");
                if (string.IsNullOrWhiteSpace(hero.Profession))
                    errors.Add($"{hero.Id}: не указана профессия.");
                if (string.IsNullOrWhiteSpace(hero.NameGenitive) || string.IsNullOrWhiteSpace(hero.NameInstrumental))
                    errors.Add($"{hero.Id}: нужны падежные формы имени (nameGenitive, nameInstrumental).");
            }

            foreach (var value in ValueCatalog)
                foreach (var opposite in value.Opposes)
                    if (!valueIds.Contains(opposite))
                        errors.Add($"Ценность «{value.Id}» противопоставлена неизвестной «{opposite}».");

            foreach (var o in RelationshipOverrides)
            {
                if (!heroIds.Contains(o.From))
                    errors.Add($"Отношение: неизвестный герой «{o.From}».");
                if (!heroIds.Contains(o.To))
                    errors.Add($"Отношение: неизвестный герой «{o.To}».");
                if (o.From == o.To)
                    errors.Add($"Отношение героя «{o.From}» к самому себе.");
                errors.AddRange(o.RangeErrors($"{o.From}→{o.To}"));
            }

            errors.AddRange(DefaultRelationship.RangeErrors("defaultRelationship"));
            errors.AddRange(DefaultMasterRelationship.RangeErrors("defaultMasterRelationship"));
            return errors;
        }

        /// <summary>
        /// Сколько пар противоположных ценностей у двух героев
        /// (например, «Цель оправдывает средства» против «Честь превыше выгоды»).
        /// Противопоставление считается в обе стороны.
        /// </summary>
        public int ValueConflicts(IEnumerable<string> valuesA, IEnumerable<string> valuesB)
        {
            var b = valuesB.ToList();
            int conflicts = 0;
            foreach (var a in valuesA)
                foreach (var other in b)
                    if (Opposes(a, other) || Opposes(other, a))
                        conflicts++;
            return conflicts;
        }

        private bool Opposes(string valueId, string otherId) =>
            ValueCatalog.Any(v => v.Id == valueId && v.Opposes.Contains(otherId));
    }

    public sealed class ValueDefinition
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        /// <summary>Ценности, с которыми эта несовместима.</summary>
        public List<string> Opposes { get; set; } = new();
    }

    public sealed class HeroDefinition
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string NameGenitive { get; set; } = "";
        public string NameInstrumental { get; set; } = "";
        public Gender Gender { get; set; }
        public int Stars { get; set; } = 1;
        public int MaxStars { get; set; } = 1;
        public string Profession { get; set; } = "";
        public CombatRole CombatRole { get; set; }
        public string Bio { get; set; } = "";
        public string Dream { get; set; } = "";
        public Traits Traits { get; set; } = new();
        public List<string> Values { get; set; } = new();
        public int TrustInMaster { get; set; } = 50;
    }

    /// <summary>Набор осей отношения; незаданная ось берётся из значения по умолчанию.</summary>
    public class RelationshipValues
    {
        public int? Trust { get; set; }
        public int? Respect { get; set; }
        public int? Affection { get; set; }
        public int? Fear { get; set; }
        public int? Rivalry { get; set; }

        public IEnumerable<string> RangeErrors(string where)
        {
            foreach (var (name, value) in new[] { ("trust", Trust), ("respect", Respect), ("affection", Affection), ("fear", Fear), ("rivalry", Rivalry) })
                if (value.HasValue && (value < 0 || value > 100))
                    yield return $"{where}: {name} = {value}, ожидается 0–100.";
        }
    }

    public sealed class RelationshipOverride : RelationshipValues
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }
}
