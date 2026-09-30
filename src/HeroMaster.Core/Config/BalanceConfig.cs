using System.Collections.Generic;
using System.Linq;

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
        public MasterConfig Master { get; set; } = new();
        /// <summary>Параметры для каждой звёздности, 1★…6★.</summary>
        public List<StarTier> Stars { get; set; } = new();
        public MemoryConfig Memory { get; set; } = new();
        public TrustConfig Trust { get; set; } = new();
        public ResolveConfig Resolve { get; set; } = new();
        public ObedienceConfig Obedience { get; set; } = new();
        public CombatConfig Combat { get; set; } = new();
        public ExpeditionConfig Expedition { get; set; } = new();
        public LootConfig Loot { get; set; } = new();
        public GiftConfig Gifts { get; set; } = new();
        public ActivityConfig Activities { get; set; } = new();
        public AdviceConfig Advice { get; set; } = new();
        public LearningConfig Learning { get; set; } = new();

        public StarTier Tier(int stars) =>
            Stars.FirstOrDefault(t => t.Stars == stars)
            ?? throw new KeyNotFoundException($"В конфиге нет записи для {stars}★.");

        public List<string> Validate()
        {
            var errors = new List<string>();

            if (Team.MinSize < 1 || Team.MaxSize < Team.MinSize)
                errors.Add("team: размер команды задан неверно.");

            if (World.RelationshipJitter < 0 || World.RelationshipJitter > 20)
                errors.Add("world.relationshipJitter: ожидается 0–20.");
            if (World.OpposingValuesTrustPenalty < 0 || World.OpposingValuesRivalry < 0)
                errors.Add("world: штрафы за противоположные ценности не могут быть отрицательными.");

            if (Master.StartGold < 0)
                errors.Add("master.startGold: не может быть отрицательным.");

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
            if (Trust.RecoverMargin < 0)
                errors.Add("trust.recoverMargin: не может быть отрицательным.");

            if (!(Obedience.EnthusiasticFrom > Obedience.CompliesFrom && Obedience.CompliesFrom > Obedience.GrudgingFrom))
                errors.Add("obedience: пороги должны убывать: enthusiasticFrom > compliesFrom > grudgingFrom.");

            if (Expedition.EnemyPowerMin < 0 || Expedition.EnemyPowerMax < Expedition.EnemyPowerMin)
                errors.Add("expedition: диапазон силы монстров задан неверно.");
            if (Expedition.Encounters < 1 || Expedition.AttacksPerEncounter < 1)
                errors.Add("expedition: нужна хотя бы одна стычка и одна атака.");
            if (Expedition.LootPerHundredPower < 0 || Expedition.LootJitterPercent < 0 || Expedition.LootJitterPercent > 100)
                errors.Add("expedition: добыча задана неверно.");
            if (Combat.PowerPerStar < 1 || Combat.ExperiencePerPower < 1)
                errors.Add("combat: powerPerStar и experiencePerPower должны быть ≥ 1.");
            if (Expedition.MinMembers < 1 || Expedition.MinMembers > Team.MaxSize)
                errors.Add("expedition.minMembers: от 1 до team.maxSize.");

            if (Loot.FairTolerancePercent < 0 || Loot.MaxTrustChange < 0)
                errors.Add("loot: допуск и максимум изменения доверия не могут быть отрицательными.");

            if (Gifts.Kinds.Count == 0)
                errors.Add("gifts.kinds: нужен хотя бы один вид подарка.");
            var traitIds = new HashSet<string> { "courage", "pride", "empathy", "discipline", "ambition", "pragmatism" };
            foreach (var kind in Gifts.Kinds)
            {
                if (string.IsNullOrWhiteSpace(kind.Id) || string.IsNullOrWhiteSpace(kind.Name))
                    errors.Add("gifts.kinds: у подарка должны быть id и name.");
                if (kind.Cost < 0)
                    errors.Add($"gifts.kinds {kind.Id}: цена не может быть отрицательной.");
                if (!string.IsNullOrEmpty(kind.Trait) && !traitIds.Contains(kind.Trait))
                    errors.Add($"gifts.kinds {kind.Id}: неизвестная черта «{kind.Trait}».");
            }
            if (Gifts.KindsById().Count != Gifts.Kinds.Count)
                errors.Add("gifts.kinds: id подарков повторяются.");

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

    public sealed class MasterConfig
    {
        public int StartGold { get; set; } = 50;
    }

    public sealed class StarTier
    {
        public int Stars { get; set; }
        /// <summary>
        /// Шанс при призыве на миллион (60000 = 6%). Миллион, а не проценты, потому что
        /// 5★ выпадает примерно 1 раз на 100 000 призывов. 0 = призвать нельзя.
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
        /// <summary>Сколько ночей подряд герой должен пробыть «на грани», прежде чем начнёт бойкот.</summary>
        public int DaysOnEdgeBeforeBoycott { get; set; } = 2;
        /// <summary>Запас доверия сверх порога, нужный, чтобы выйти из плохого статуса (без «дребезга»).</summary>
        public int RecoverMargin { get; set; } = 5;
        /// <summary>Сколько доверия теряет обиженный герой, который весь вечер «копит обиду».</summary>
        public int BroodTrustLoss { get; set; } = 1;
    }

    /// <summary>
    /// Решимость: призванный в Башню человек сам находит мотивацию.
    /// Старт зависит от характера (прагматики и честолюбцы осваиваются быстрее),
    /// дальше решимость растёт каждый день, от пережитых вылазок и падает от ранений.
    /// </summary>
    public sealed class ResolveConfig
    {
        public int StartCouragePercent { get; set; } = 10;
        public int StartAmbitionPercent { get; set; } = 20;
        public int StartPragmatismPercent { get; set; } = 20;
        public int DailyGain { get; set; } = 1;
        public int ExpeditionSurvivedGain { get; set; } = 3;
        public int InjuryLoss { get; set; } = 5;
        /// <summary>Порог «осознания»: герой понял, что выхода нет, кроме как становиться сильнее.</summary>
        public int PurposeThreshold { get; set; } = 50;
    }

    /// <summary>
    /// Готовность сражаться = доверие + поправки. На вылазку герой идёт всегда (контракт),
    /// а готовность решает, как он ведёт себя в бою: охотно / честно / вполсилы / отказывается драться.
    /// Всё в пунктах шкалы 0–100.
    /// </summary>
    public sealed class ObedienceConfig
    {
        /// <summary>Решимость выше 50 добавляет готовности, ниже — отнимает.</summary>
        public int ResolvePercent { get; set; } = 20;
        public int DisciplinePercent { get; set; } = 40;
        public int FatiguePercent { get; set; } = 50;
        public int StressPercent { get; set; } = 20;
        public int RivalInTeamPenalty { get; set; } = 10;
        public int RivalryThreshold { get; set; } = 50;
        public int FriendInTeamBonus { get; set; } = 5;
        public int FriendAffectionThreshold { get; set; } = 60;
        /// <summary>Штраф за приказ героям с ценностью «Приказ без объяснений — неуважение».</summary>
        public int NoExplanationPenalty { get; set; } = 10;
        public int Jitter { get; set; } = 5;
        public int EnthusiasticFrom { get; set; } = 70;
        public int CompliesFrom { get; set; } = 45;
        public int GrudgingFrom { get; set; } = 25;
    }

    /// <summary>
    /// Сила героя в бою. Главное — звёзды и опыт; настрой даёт лишь небольшую поправку:
    /// слабый герой, выкладываясь по полной, всё равно слабее опытного бойца.
    /// </summary>
    public sealed class CombatConfig
    {
        public int PowerPerStar { get; set; } = 20;
        /// <summary>Сколько опыта даёт +1 к силе.</summary>
        public int ExperiencePerPower { get; set; } = 10;
        /// <summary>Потолок прибавки от опыта — на каждую звезду (1★ может добавить опытом не больше 10).</summary>
        public int ExperienceCapPerStar { get; set; } = 10;
        /// <summary>Усталость 100 снижает силу на столько процентов.</summary>
        public int FatiguePowerLossPercent { get; set; } = 40;
        /// <summary>Раненый дерётся на столько процентов своей силы.</summary>
        public int InjuredPowerPercent { get; set; } = 50;

        public int AllInEffortPercent { get; set; } = 110;
        public int HalfHeartedEffortPercent { get; set; } = 90;
        /// <summary>Отказавшийся драться, на которого напали, отбивается — но только чтобы выжить.</summary>
        public int SelfDefenseEffortPercent { get; set; } = 50;

        /// <summary>Шанс пасть духом, увидев ранение товарища: база минус смелость, плюс привязанность к раненому.</summary>
        public int WitnessMoraleChance { get; set; } = 50;
        public int WitnessCouragePercent { get; set; } = 50;
        public int WitnessAffectionPercent { get; set; } = 25;
        /// <summary>Трус (смелость ниже этого), уже сражающийся вполсилы, от страха перестаёт сражаться.</summary>
        public int PanicCourageBelow { get; set; } = 30;
    }

    /// <summary>Упрощённая вылазка из нескольких стычек (до появления настоящего боя в группе 3).</summary>
    public sealed class ExpeditionConfig
    {
        public int MinMembers { get; set; } = 3;
        public int Encounters { get; set; } = 3;
        public int AttacksPerEncounter { get; set; } = 2;
        /// <summary>Сила одной атаки монстров.</summary>
        public int EnemyPowerMin { get; set; } = 15;
        public int EnemyPowerMax { get; set; } = 35;
        /// <summary>Добыча за стычку: столько золота на каждые 100 единиц общей силы отряда.</summary>
        public int LootPerHundredPower { get; set; } = 15;
        public int LootJitterPercent { get; set; } = 20;
        public int FatigueGain { get; set; } = 35;
        /// <summary>Максимальный прирост стресса — у самого трусливого; у смелых меньше.</summary>
        public int StressGainMax { get; set; } = 20;
        /// <summary>Базовый шанс ранения для героя, на которого напали.</summary>
        public int InjuryChancePercent { get; set; } = 2;
        /// <summary>Сколько процентов от разницы «сила монстра − сила героя» добавляется к шансу ранения.</summary>
        public int InjuryPowerGapPercent { get; set; } = 25;
        /// <summary>Сколько процентов шанса ранения добавляет каждые 10 пунктов усталости.</summary>
        public int InjuryChancePerTenFatigue { get; set; } = 2;
        /// <summary>Стресс от тяжёлого ранения: тело на базе заживёт, а пережитое останется.</summary>
        public int InjuryStress { get; set; } = 15;
        public int ExperienceGain { get; set; } = 12;
        public int SuccessTrustGain { get; set; } = 1;
        public int InjuredTrustLoss { get; set; } = 5;
        /// <summary>Усталость, начиная с которой отправка на вылазку воспринимается как неуважение.</summary>
        public int SentTiredThreshold { get; set; } = 60;
        public int SentTiredTrustLoss { get; set; } = 4;
        /// <summary>На сколько % растёт риск ранения остальных за каждого, кто отказался драться.</summary>
        public int RefuseDangerPercent { get; set; } = 5;
        /// <summary>То же за каждого бойкотирующего: он бросает позицию и подставляет команду.</summary>
        public int BoycottDangerPercent { get; set; } = 15;
    }

    public sealed class LootConfig
    {
        /// <summary>На сколько процентов доля может быть меньше ожидаемой без обиды.</summary>
        public int FairTolerancePercent { get; set; } = 15;
        public int FairShareTrustGain { get; set; } = 1;
        /// <summary>Потеря доверия за каждые 10% недодачи сверх допуска.</summary>
        public int TrustLossPerTenPercent { get; set; } = 2;
        public int MaxTrustChange { get; set; } = 8;
        /// <summary>Потеря доверия, если мастер оставил всю добычу себе.</summary>
        public int KeptAllTrustLoss { get; set; } = 6;
        /// <summary>Гордый герой (гордость 100) ждёт долю больше равной на этот процент.</summary>
        public int PrideExpectationPercent { get; set; } = 20;
    }

    public sealed class GiftConfig
    {
        public List<GiftKind> Kinds { get; set; } = new();
        /// <summary>Подарки чаще, чем раз в столько дней, ценятся меньше.</summary>
        public int RepeatWindowDays { get; set; } = 3;
        /// <summary>Какая доля ценности остаётся у каждого следующего подарка в окне.</summary>
        public int RepeatValuePercent { get; set; } = 50;

        public Dictionary<string, GiftKind> KindsById()
        {
            var map = new Dictionary<string, GiftKind>();
            foreach (var k in Kinds)
                map[k.Id] = k;
            return map;
        }
    }

    public sealed class GiftKind
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Cost { get; set; }
        public int BaseTrust { get; set; }
        /// <summary>Черта, от которой зависит, насколько подарок понравится (пусто — нравится всем одинаково).</summary>
        public string Trait { get; set; } = "";
        /// <summary>Доверие сверх базового при черте 100.</summary>
        public int TraitBonusMax { get; set; }
    }

    /// <summary>Веса занятий на базе (utility AI) и их эффекты.</summary>
    public sealed class ActivityConfig
    {
        public int Jitter { get; set; } = 10;
        public int HintBonusMax { get; set; } = 30;
        /// <summary>При доверии ниже этого намёк игнорируется.</summary>
        public int HintIgnoredBelowTrust { get; set; } = 30;
        /// <summary>Гордый (гордость ≥ этого) и недоверчивый герой делает наоборот.</summary>
        public int HintSpitePride { get; set; } = 70;
        public int HintSpitePenalty { get; set; } = 15;

        public int RestBase { get; set; } = 10;
        public int RestFatiguePercent { get; set; } = 70;
        public int RestStressPercent { get; set; } = 20;

        public int TrainBase { get; set; } = 5;
        public int TrainAmbitionPercent { get; set; } = 15;
        /// <summary>Главный двигатель тренировок — решимость, а не врождённое честолюбие.</summary>
        public int TrainResolvePercent { get; set; } = 40;
        /// <summary>Тоска по дому: чем меньше решимость, тем чаще герой сидит один.</summary>
        public int BroodDespairPercent { get; set; } = 15;
        public int TrainDisciplinePercent { get; set; } = 20;
        public int TrainFatiguePercent { get; set; } = 60;

        public int SocializeBase { get; set; } = 20;
        public int SocializeEmpathyPercent { get; set; } = 25;
        public int SocializeStressPercent { get; set; } = 20;

        public int WorkBase { get; set; } = 15;
        public int WorkDisciplinePercent { get; set; } = 25;

        public int BroodStressPercent { get; set; } = 40;
        public int BroodDiscontentedBonus { get; set; } = 15;
        public int BroodOnEdgeBonus { get; set; } = 30;
        public int BroodBoycottBonus { get; set; } = 50;

        public int RestFatigueRecovery { get; set; } = 40;
        public int RestStressRecovery { get; set; } = 15;
        public int TrainFatigue { get; set; } = 15;
        public int TrainExperience { get; set; } = 10;
        public int WorkFatigue { get; set; } = 10;
        public int WorkGold { get; set; } = 3;
        public int SocializeAffection { get; set; } = 4;
        public int SocializeTrust { get; set; } = 2;
        public int SocializeStressRecovery { get; set; } = 8;

        /// <summary>Шанс ссоры за каждую пару противоположных ценностей, %.</summary>
        public int QuarrelChancePerConflict { get; set; } = 25;
        /// <summary>Шанс ссоры за каждые 10 пунктов соперничества, %.</summary>
        public int QuarrelChancePerTenRivalry { get; set; } = 3;
        public int QuarrelRivalry { get; set; } = 8;
        public int QuarrelTrustLoss { get; set; } = 6;
        public int QuarrelAffectionLoss { get; set; } = 4;
        public int QuarrelStress { get; set; } = 10;

        /// <summary>
        /// «Общий ужин»: каждый вечер пара героев с противоположными ценностями или соперничеством
        /// может сцепиться, даже если они не общались. Шанс в % за пару ценностей / за 10 соперничества.
        /// </summary>
        public int TensionChancePerConflict { get; set; } = 4;
        public int TensionChancePerTenRivalry { get; set; } = 1;
        public int TensionChanceMax { get; set; } = 50;
        /// <summary>Обиды остывают: соперничество между героями снижается каждую ночь.</summary>
        public int RivalryDecayPerNight { get; set; } = 1;

        /// <summary>Скука: за каждый повтор того же занятия подряд его оценка падает, но не больше максимума.</summary>
        public int RepeatPenalty { get; set; } = 6;
        public int RepeatPenaltyMax { get; set; } = 18;

        public int NightFatigueRecovery { get; set; } = 10;
        public int NightStressRecovery { get; set; } = 5;
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
