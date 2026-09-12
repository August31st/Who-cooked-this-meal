using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;

namespace WhoCookThisMeal;

public sealed class RitualRoleWCTMCook : RitualRoleColonist
{
    public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
    {
        if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason)) return false;
        bool valid = Find.CurrentMap?.mapPawns?.FreeColonistsSpawned.Any(eater =>
            eater != pawn && eater.needs?.mood?.thoughts?.memories?.Memories.Any(memory =>
                WCTMSingleCombatThoughts.IsNegativeCookingMemory(memory, pawn)) == true) == true;
        if (!valid && !skipReason) reason = "WCTM_EaterNeedsUnqualifiedOpinion".Translate();
        return valid;
    }
}

public sealed class RitualRoleWCTMEater : RitualRoleColonist
{
    public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
    {
        if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason)) return false;
        Pawn cook = assignments?.FirstAssignedPawn(assignments.GetRole("cook"));
        return cook == null || pawn != cook && pawn.needs?.mood?.thoughts?.memories?.Memories.Any(memory => WCTMSingleCombatThoughts.IsNegativeCookingMemory(memory, cook)) == true;
    }

}

internal static class WCTMSingleCombatThoughts
{
    public static bool IsNegativeCookingMemory(Thought_Memory memory, Pawn cook)
    {
        return (memory.def.defName == "WCTM_UnqualifiedCook" || memory.def.defName == "WCTM_MechMealPoisoning") && memory.otherPawn == cook;
    }
}

public sealed class RitualBehaviorWorkerWCTMSingleCombat : RitualBehaviorWorker
{
    private Sustainer sound;

    public RitualBehaviorWorkerWCTMSingleCombat() { }
    public RitualBehaviorWorkerWCTMSingleCombat(RitualBehaviorDef def) : base(def) { }
    public override Sustainer SoundPlaying => sound;

    protected override LordJob CreateLordJob(TargetInfo target, Pawn organizer, Precept_Ritual ritual, RitualObligation obligation, RitualRoleAssignments assignments)
    {
        if (!WhoCookThisMealMod.Settings.PeacefulSingleCombat)
        {
            RitualBehaviorDef duelBehavior = DefDatabase<RitualBehaviorDef>.GetNamedSilentFail("WCTM_SingleCombatDuel");
            List<RitualStage> duelStages = duelBehavior?.stages ?? def.stages;
            return new LordJob_WCTMSingleCombatDuel(target, ritual, obligation, duelStages, assignments, organizer);
        }

        return base.CreateLordJob(target, organizer, ritual, obligation, assignments);
    }

    public override void Tick(LordJob_Ritual ritual)
    {
        if (sound == null || sound.Ended)
        {
            string soundName = Rand.Bool ? "WCTM_SingleCombatMusic" : "WCTM_SingleCombatMusicAlt";
            SoundDef soundDef = DefDatabase<SoundDef>.GetNamedSilentFail(soundName);
            sound = soundDef?.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(ritual.Spot, ritual.Map), MaintenanceType.PerTick));
        }
        sound?.Maintain();
    }

    public override void Cleanup(LordJob_Ritual ritual) => sound = null;
}

public sealed class RitualOutcomeCompWCTMSkillDifference : RitualOutcomeComp_Quality
{
    public override bool DataRequired => false;

    public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
    {
        Pawn cook = GetAssignedPawn(ritual, "cook");
        Pawn eater = GetAssignedPawn(ritual, "eater");
        if (cook?.skills == null || eater?.skills == null)
        {
            return 0f;
        }

        return Mathf.Abs(cook.skills.GetSkill(SkillDefOf.Melee).Level - eater.skills.GetSkill(SkillDefOf.Melee).Level);
    }

    public override float QualityOffset(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
    {
        return FixedQualityOffset(Count(ritual, data));
    }

    public override QualityFactor GetQualityFactor(Precept_Ritual ritual, TargetInfo ritualTarget, RitualObligation obligation, RitualRoleAssignments assignments, RitualOutcomeComp_Data data)
    {
        Pawn cook = assignments?.FirstAssignedPawn(assignments.GetRole("cook"));
        Pawn eater = assignments?.FirstAssignedPawn(assignments.GetRole("eater"));
        if (cook?.skills == null || eater?.skills == null)
        {
            return null;
        }

        int difference = Mathf.Abs(cook.skills.GetSkill(SkillDefOf.Melee).Level - eater.skills.GetSkill(SkillDefOf.Melee).Level);
        float quality = FixedQualityOffset(difference);
        return new QualityFactor
        {
            label = label,
            count = difference.ToString(),
            qualityChange = Mathf.Abs(quality) > float.Epsilon
                ? "OutcomeBonusDesc_QualitySingleOffset".Translate(quality.ToStringWithSign("0.#%")).Resolve()
                : " - ",
            positive = quality >= 0f,
            quality = quality,
            priority = 0f
        };
    }

    public override string GetDesc(LordJob_Ritual ritual = null, RitualOutcomeComp_Data data = null)
    {
        if (ritual == null)
        {
            return label;
        }

        return Count(ritual, data).ToString() + " " + label + ": " +
            "OutcomeBonusDesc_QualitySingleOffset".Translate(FixedQualityOffset(Count(ritual, data)).ToStringWithSign("0.#%")) + ".";
    }

    private static float FixedQualityOffset(float difference)
    {
        return difference <= 1f ? 0.4f : difference <= 4f ? 0.3f : difference <= 8f ? 0.1f : 0f;
    }

    internal static Pawn GetAssignedPawn(LordJob_Ritual ritual, string roleId)
    {
        RitualRole role = ritual?.assignments?.GetRole(roleId);
        return role == null ? null : ritual.assignments.FirstAssignedPawn(role);
    }
}

public sealed class RitualOutcomeEffectWorkerWCTMSingleCombat : RitualOutcomeEffectWorker_FromQuality
{
    public RitualOutcomeEffectWorkerWCTMSingleCombat() { }
    public RitualOutcomeEffectWorkerWCTMSingleCombat(RitualOutcomeEffectDef def) : base(def) { }

    public override string OutcomeQualityBreakdownDesc(float quality, float progress, LordJob_Ritual jobRitual)
    {
        string result = base.OutcomeQualityBreakdownDesc(quality, progress, jobRitual);
        Pawn cook = RitualOutcomeCompWCTMSkillDifference.GetAssignedPawn(jobRitual, "cook");
        Pawn eater = RitualOutcomeCompWCTMSkillDifference.GetAssignedPawn(jobRitual, "eater");
        if (cook?.skills != null && eater?.skills != null &&
            !result.Contains("Melee skill difference") && !result.Contains("格斗能力差值"))
        {
            int difference = Mathf.Abs(cook.skills.GetSkill(SkillDefOf.Melee).Level - eater.skills.GetSkill(SkillDefOf.Melee).Level);
            float offset = difference <= 1 ? 0.4f : difference <= 4 ? 0.3f : difference <= 8 ? 0.1f : 0f;
            result += "\n  - " + "WCTM_MeleeSkillDifferenceImpact".Translate(difference, offset.ToStringPercent()).Resolve();
        }

        return result;
    }

    public override RitualOutcomePossibility GetOutcome(float quality, LordJob_Ritual ritual)
    {
        Pawn cook = RitualOutcomeCompWCTMSkillDifference.GetAssignedPawn(ritual, "cook");
        Pawn eater = RitualOutcomeCompWCTMSkillDifference.GetAssignedPawn(ritual, "eater");
        if (ritual is LordJob_WCTMSingleCombatDuel && cook != null && eater != null && !cook.DeadOrDowned && !eater.DeadOrDowned)
        {
            return def.outcomeChances.First(outcome => outcome.positivityIndex == 3);
        }

        return base.GetOutcome(quality, ritual);
    }

    protected override void ApplyExtraOutcome(Dictionary<Pawn, int> presence, LordJob_Ritual ritual, RitualOutcomePossibility outcome, out string extraOutcomeDesc, ref LookTargets letterLookTargets)
    {
        extraOutcomeDesc = null;
        Pawn cook = RitualOutcomeCompWCTMSkillDifference.GetAssignedPawn(ritual, "cook");
        Pawn eater = RitualOutcomeCompWCTMSkillDifference.GetAssignedPawn(ritual, "eater");
        if (cook?.skills == null || eater?.skills == null)
        {
            return;
        }
        int cookMelee = cook.skills.GetSkill(SkillDefOf.Melee).Level;
        int eaterMelee = eater.skills.GetSkill(SkillDefOf.Melee).Level;
        float cookWinChance = cookMelee + eaterMelee == 0 ? 0.5f : (float)cookMelee / (cookMelee + eaterMelee);
        bool draw = !cook.DeadOrDowned && !eater.DeadOrDowned;
        Pawn loser = GetLoser(cook, eater, cookWinChance, ritual);
        Pawn winner = draw ? null : loser == cook ? eater : cook;
        extraOutcomeDesc = draw
            ? "WCTM_SingleCombatDraw".Translate()
            : "WCTM_SingleCombatWinner".Translate(winner.LabelShortCap);

        foreach (Pawn pawn in presence.Keys)
        {
            if (outcome.positivityIndex == 2) pawn.skills?.Learn(SkillDefOf.Melee, pawn == cook || pawn == eater ? 3500f : 1500f);
            if (outcome.positivityIndex == 3) pawn.skills?.Learn(SkillDefOf.Melee, pawn == cook || pawn == eater ? 5000f : 2000f);
        }

        if (!draw)
        {
            if (loser == cook)
            {
                WhoCookThisMealMod.AddCarefulCooking(cook);
            }
            else if (!(ritual is LordJob_WCTMSingleCombatDuel))
            {
                Hediff abasia = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Abasia"), eater);
                abasia.TryGetComp<HediffComp_Disappears>()?.SetDuration(60000);
                eater.health.AddHediff(abasia);
            }
        }
        eater.needs?.mood?.thoughts?.memories.RemoveMemoriesOfDefWhereOtherPawnIs(
            DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_UnqualifiedCook"), cook);
        eater.needs?.mood?.thoughts?.memories.RemoveMemoriesOfDefWhereOtherPawnIs(
            DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_MechMealPoisoning"), cook);
        if (draw)
        {
            ThoughtDef fullMeal = DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_SingleCombatFullMeal");
            cook.needs?.mood?.thoughts?.memories.TryGainMemory(fullMeal);
            eater.needs?.mood?.thoughts?.memories.TryGainMemory(fullMeal);
            return;
        }

        if (winner == eater)
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_ChefLost");
            foreach (Pawn pawn in presence.Keys) pawn.needs?.mood?.thoughts?.memories.TryGainMemory(thought);
        }
        else
        {
            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_EaterLost");
            eater.needs?.mood?.thoughts?.memories.TryGainMemory(thought, cook);
        }
    }

    private static Pawn GetLoser(Pawn cook, Pawn eater, float cookWinChance, LordJob_Ritual ritual)
    {
        if (ritual is LordJob_WCTMSingleCombatDuel && cook.DeadOrDowned != eater.DeadOrDowned)
        {
            return cook.DeadOrDowned ? cook : eater;
        }

        return Rand.Chance(cookWinChance) ? eater : cook;
    }
}

public sealed class JobGiver_WCTMSafeDuel : JobGiver_Duel
{
    protected override Job TryGiveJob(Pawn pawn)
    {
        LordJob_WCTMSingleCombatDuel duel = pawn.GetLord()?.LordJob as LordJob_WCTMSingleCombatDuel;
        if (duel == null || duel.IsDuelEnded || duel.Opponent(pawn)?.DeadOrDowned == true)
        {
            return null;
        }

        return base.TryGiveJob(pawn);
    }

    protected override Job MeleeAttackJob(Pawn pawn, Thing enemyTarget)
    {
        Job job = base.MeleeAttackJob(pawn, enemyTarget);
        job.killIncappedTarget = false;
        return job;
    }
}

public sealed class LordJob_WCTMSingleCombatDuel : LordJob_Ritual_Duel
{
    public LordJob_WCTMSingleCombatDuel() { }

    public bool IsDuelEnded => ended;

    public LordJob_WCTMSingleCombatDuel(TargetInfo selectedTarget, Precept_Ritual ritual, RitualObligation obligation, List<RitualStage> allStages, RitualRoleAssignments assignments, Pawn organizer = null)
        : base(selectedTarget, ritual, obligation, allStages, assignments, null)
    {
        AddDuelist(assignments, "cook");
        AddDuelist(assignments, "eater");
    }

    private void AddDuelist(RitualRoleAssignments assignments, string roleId)
    {
        Pawn pawn = assignments.FirstAssignedPawn(assignments.GetRole(roleId));
        if (pawn == null)
        {
            return;
        }

        duelists.Add(pawn);
        pawnsDeathIgnored.Add(pawn);
    }

    protected override bool ShouldCallOffBecausePawnNoLongerOwned(Pawn pawn)
    {
        return !duelists.Contains(pawn) && base.ShouldCallOffBecausePawnNoLongerOwned(pawn);
    }

    public override bool ShouldRemovePawn(Pawn pawn, PawnLostCondition reason)
    {
        if (duelists.Contains(pawn) && reason == PawnLostCondition.Incapped)
        {
            return false;
        }

        return base.ShouldRemovePawn(pawn, reason);
    }

    public override bool DutyActiveWhenDown(Pawn pawn)
    {
        return duelists.Contains(pawn) || base.DutyActiveWhenDown(pawn);
    }

    protected override IEnumerable<Trigger> CallOffTriggers()
    {
        yield return new Trigger_TickCondition(() => ShouldBeCalledOff(), 1);
        yield return new Trigger_Signal(CancelSignal);
    }

    protected override bool RitualFinished(float progress, bool cancelled)
    {
        if (!cancelled && duelists.Any(pawn => pawn.DeadOrDowned))
        {
            return true;
        }

        return base.RitualFinished(progress, cancelled);
    }

    public override void ApplyOutcome(float progress, bool showFinishedMessage = true, bool showFailedMessage = true, bool cancelled = false)
    {
        base.ApplyOutcome(progress, showFinishedMessage, showFailedMessage, cancelled);
        if (!IsDuelEnded)
        {
            return;
        }

        foreach (Pawn pawn in duelists)
        {
            pawn.jobs?.CheckForJobOverride(0f, true);
        }
    }
}

public sealed class HediffCompPropertiesWCTMCookComa : HediffCompProperties_Disappears
{
    public HediffCompPropertiesWCTMCookComa() => compClass = typeof(HediffCompWCTMCookComa);
}

public sealed class HediffCompWCTMCookComa : HediffComp_Disappears
{
    public override void CompPostPostRemoved()
    {
        base.CompPostPostRemoved();
        HediffDef buff = DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_CarefulCooking");
        if (buff != null && Pawn.health != null) Pawn.health.AddHediff(HediffMaker.MakeHediff(buff, Pawn));
    }
}
