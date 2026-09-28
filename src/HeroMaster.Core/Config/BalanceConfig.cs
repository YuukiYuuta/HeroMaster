using System.Collections.Generic;

namespace HeroMaster.Core.Config
{
    /// <summary>
    /// Все числа баланса. Живут в data/balance.json, чтобы менять их без перекомпиляции.
    /// Проценты — целые числа, чтобы расчёты были одинаковыми на всех платформах.
    /// </summary>
    public sealed class BalanceConfig
    {
        public TeamConfig Team { get; set; } = new();
        public WorldConfig World { get; set; } = new();
        /// <summary>Параметры для каждой звёздности, 1★…6★.</summary>
        public List<StarTier> Stars { get; set; } = new();
        public MemoryConfig Memory { get; set; } = new();
        public TrustConfig Trust { get; set; } = new();
        public AdviceConfig Advice { get; set; } = new();
        public LearningConfig Learning { get; set; } = new();

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (Team.MinSize < 1 || Team.MaxSize < Team.MinSize)
                errors.Add("team: размер команды задан неверно.");

            if (World.RelationshipJitter < 0 || World.RelationshipJitter > 20)
                errors.Add("world.relationshipJitter: ожидается 0–20.");
            if (World.OpposingValuesTrustPenalty < 0 || World.OpposingValuesRivalry < 0)
                errors.Add("world: штрафы за противоположные ценности не могут быть отрицательными.");

            var tiersPerStar = new int[7];
            int totalRate = 0;
            foreach (var t in Stars)
            {
                if (t.Stars >= 1 && t.Stars <= 6)
                    tiersPerStar[t.Stars]++;
                else
                    errors.Add($"stars: звёздность {t.Stars} вне диапазона 1–6.");
                totalRate += t.SummonRatePerMillion;
                if (t.SummonRatePerMillion < 0)
                    errors.Add($"stars {t.Stars}★: шанс призыва не может быть отрицательным.");
                if (t.SkillSlots < 1 || t.LearningSpeedPercent < 1)
                    errors.Add($"stars {t.Stars}★: skillSlots и learningSpeedPercent должны быть ≥ 1.");
            }
            for (int s = 1; s <= 6; s++)
                if (tiersPerStar[s] != 1)
                    errors.Add($"stars: должна быть ровно одна запись для {s}★.");
            if (totalRate != 1000000)
                errors.Add("stars: сумма summonRatePerMillion должна быть 1000000 (= 100%).");

            if (Memory.MaxBeliefs < 1 || Memory.MaxKeyMemories < 0)
                errors.Add("memory: лимиты памяти заданы неверно.");
            if (Memory.KeyMemoryImportance < 1 || Memory.KeyMemoryImportance > 10)
                errors.Add("memory.keyMemoryImportance: ожидается 1–10.");

            if (!(Trust.DiscontentedBelow > Trust.OnEdgeBelow && Trust.OnEdgeBelow > Trust.BoycottBelow && Trust.BoycottBelow >= 0))
                errors.Add("trust: пороги должны убывать: discontentedBelow > onEdgeBelow > boycottBelow ≥ 0.");
            if (Trust.DaysOnEdgeBeforeBoycott < 0)
                errors.Add("trust.daysOnEdgeBeforeBoycott: не может быть отрицательным.");

            if (Advice.SpamWindowTurns < 1 || Advice.MaxAdviceInWindow < 1)
                errors.Add("advice: окно спама и лимит советов должны быть ≥ 1.");

            if (Learning.StrengthPerSuccess <= 0)
                errors.Add("learning.strengthPerSuccess: должно быть больше нуля.");
            if (Learning.LearnedThreshold < 1 || Learning.LearnedThreshold > 100)
                errors.Add("learning.learnedThreshold: ожидается 1–100.");

            return errors;
        }
    }

    public sealed class TeamConfig
    {
        public int MinSize { get; set; } = 4;
        public int MaxSize { get; set; } = 5;
    }

    public sealed class WorldConfig
    {
        /// <summary>Разброс стартовых отношений (±), чтобы партии начинались чуть по-разному.</summary>
        public int RelationshipJitter { get; set; } = 5;
        /// <summary>На сколько падает стартовое доверие за каждую пару противоположных ценностей.</summary>
        public int OpposingValuesTrustPenalty { get; set; } = 20;
        /// <summary>Сколько соперничества добавляет каждая пара противоположных ценностей.</summary>
        public int OpposingValuesRivalry { get; set; } = 30;
    }

    public sealed class StarTier
    {
        public int Stars { get; set; }
        /// <summary>
        /// Шанс при призыве на миллион (60000 = 6%). Миллион, а не проценты, потому что
        /// 5★ выпадает примерно 1 раз на 20 000 призывов. 0 = призвать нельзя.
        /// </summary>
        public int SummonRatePerMillion { get; set; }
        public int SkillSlots { get; set; }
        /// <summary>Скорость обучения относительно 3★ (100%).</summary>
        public int LearningSpeedPercent { get; set; }
    }

    public sealed class MemoryConfig
    {
        public int MaxBeliefs { get; set; } = 30;
        public int MaxKeyMemories { get; set; } = 10;
        public int KeyMemoryImportance { get; set; } = 9;
        public int ForgetMinorEventsAfterDays { get; set; } = 14;
    }

    public sealed class TrustConfig
    {
        public int DiscontentedBelow { get; set; } = 35;
        public int OnEdgeBelow { get; set; } = 20;
        public int BoycottBelow { get; set; } = 10;
        public int DaysOnEdgeBeforeBoycott { get; set; } = 2;
    }

    public sealed class AdviceConfig
    {
        public int SpamWindowTurns { get; set; } = 3;
        public int MaxAdviceInWindow { get; set; } = 2;
        public int SpamTrustPenalty { get; set; } = 3;
        public int ContradictionTrustPenalty { get; set; } = 4;
        public int SuccessTrustGain { get; set; } = 2;
        public int FailureTrustLoss { get; set; } = 3;
    }

    public sealed class LearningConfig
    {
        public int StrengthPerSuccess { get; set; } = 15;
        public int StrengthLossPerFailure { get; set; } = 10;
        public int LearnedThreshold { get; set; } = 60;
        public int DecayPerIdleDay { get; set; } = 1;
        public int TeachingStrength { get; set; } = 8;
        public int RespectedTeacherBonusPercent { get; set; } = 50;
        public int RivalRefusesTeachingAbove { get; set; } = 50;
    }
}
