using System.Collections.Generic;
using System.Linq;

namespace HeroMaster.Core.Memory
{
    /// <summary>Правило рефлексии: какое событие к какому выводу подталкивает героя.</summary>
    public sealed class BeliefRule
    {
        public string Id { get; set; } = "";
        public string Event { get; set; } = "";
        /// <summary>positive / negative / neutral — по эмоции события; пусто — любое.</summary>
        public string When { get; set; } = "";
        /// <summary>first / second / all — кто из участников события делает вывод.</summary>
        public string Subject { get; set; } = "first";
        /// <summary>master / self / other / data:&lt;ключ&gt; — о ком вывод.</summary>
        public string About { get; set; } = "";
        public string Text { get; set; } = "";
        public int Gain { get; set; }
        public int Emotion { get; set; }
        public List<string> Opposes { get; set; } = new();
        public bool Key { get; set; }

        public bool MatchesEmotion(int emotion)
        {
            switch (When)
            {
                case "positive": return emotion > 0;
                case "negative": return emotion < 0;
                case "neutral": return emotion == 0;
                default: return true;
            }
        }
    }

    /// <summary>Все правила рефлексии из data/beliefs.json.</summary>
    public sealed class BeliefCatalog
    {
        public const string AboutMaster = "master";
        public const string AboutSelf = "self";
        public const string AboutOther = "other";
        public const string AboutDataPrefix = "data:";

        public List<BeliefRule> Rules { get; set; } = new();

        public IEnumerable<BeliefRule> For(string eventType) => Rules.Where(r => r.Event == eventType);

        public List<string> Validate()
        {
            var errors = new List<string>();
            foreach (var r in Rules)
            {
                string where = string.IsNullOrEmpty(r.Id) ? "правило без id" : $"правило «{r.Id}»";
                if (string.IsNullOrWhiteSpace(r.Id))
                    errors.Add("rules: у каждого правила должен быть id.");
                if (string.IsNullOrWhiteSpace(r.Event))
                    errors.Add($"{where}: не указано событие (event).");
                if (string.IsNullOrWhiteSpace(r.Text))
                    errors.Add($"{where}: нет текста убеждения (text).");
                if (r.When != "" && r.When != "positive" && r.When != "negative" && r.When != "neutral")
                    errors.Add($"{where}: when — positive, negative, neutral или пусто.");
                if (r.Subject != "first" && r.Subject != "second" && r.Subject != "all")
                    errors.Add($"{where}: subject — first, second или all.");
                bool aboutOk = r.About == AboutMaster || r.About == AboutSelf || r.About == AboutOther
                               || (r.About.StartsWith(AboutDataPrefix) && r.About.Length > AboutDataPrefix.Length);
                if (!aboutOk)
                    errors.Add($"{where}: about — master, self, other или data:<ключ>.");
                bool aboutHero = r.About == AboutOther || r.About.StartsWith(AboutDataPrefix);
                if (!aboutHero && r.Text.Contains("{other"))
                    errors.Add($"{where}: в тексте есть {{other…}}, но вывод не о другом герое.");
                if (r.Gain < 1 || r.Gain > 100)
                    errors.Add($"{where}: gain должен быть 1–100.");
                if (r.Emotion < -5 || r.Emotion > 5)
                    errors.Add($"{where}: emotion должна быть от -5 до 5.");
                if (r.Text.Contains("{g:") && !r.Text.Contains("|"))
                    errors.Add($"{where}: форма по полу пишется как {{g:он|она}}.");
            }

            // Одно убеждение может подкрепляться разными событиями, но о ком оно — должно совпадать.
            foreach (var group in Rules.GroupBy(r => r.Id))
                if (group.Select(r => r.About).Distinct().Count() > 1)
                    errors.Add($"убеждение «{group.Key}»: у правил разные about.");

            var known = new HashSet<string>(Rules.Select(r => r.Id));
            foreach (var r in Rules)
                foreach (var o in r.Opposes)
                    if (!known.Contains(o))
                        errors.Add($"правило «{r.Id}»: opposes ссылается на неизвестное убеждение «{o}».");
            return errors;
        }
    }
}
