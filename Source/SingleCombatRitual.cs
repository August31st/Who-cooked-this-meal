using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace WhoCookThisMeal;

public sealed class RitualRoleWCTMCook : RitualRoleColonist
{
    public override bool AppliesToPawn(Pawn pawn, out string reason, TargetInfo target, LordJob_Ritual ritual = null, RitualRoleAssignments assignments = null, Precept_Ritual precept = null, bool skipReason = false)
    {
        if (!base.AppliesToPawn(pawn, out reason, target, ritual, assignments, precept, skipReason)) return false;
        bool valid = Find.CurrentMap?.mapPawns?.FreeColonistsSpawned.Any(eater =>
            eater != pawn && eater.needs?.mood?.thoughts?.memories?.Memories.Any(memory =>
                memory.def.defName == "WCTM_UnqualifiedCook" && memory.otherPawn == pawn) == true) == true;
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
        return cook == null || pawn != cook && pawn.needs?.mood?.thoughts?.memories?.Memories.Any(memory => memory.def.defName == "WCTM_UnqualifiedCook" && memory.otherPawn == cook) == true;
    }
}

public sealed class RitualBehaviorWorkerWCTMSingleCombat : RitualBehaviorWorker
{
    private Sustainer sound;

    public RitualBehaviorWorkerWCTMSingleCombat() { }
    public RitualBehaviorWorkerWCTMSingleCombat(RitualBehaviorDef def) : base(def) { }
    public override Sustainer SoundPlaying => sound;

    public override void Tick(LordJob_Ritual ritual)
    {
        SoundDef soundDef = DefDatabase<SoundDef>.GetNamedSilentFail("WCTM_SingleCombatMusic");
        if (sound == null || sound.Ended) sound = soundDef?.TrySpawnSustainer(SoundInfo.InMap(new TargetInfo(ritual.Spot, ritual.Map), MaintenanceType.PerTick));
        sound?.Maintain();
    }

    public override void Cleanup(LordJob_Ritual ritual) => sound = null;
}

public sealed class RitualOutcomeCompWCTMSkillDifference : RitualOutcomeComp_Quality
{
    public override float Count(LordJob_Ritual ritual, RitualOutcomeComp_Data data) => 0f;

    public override float QualityOffset(LordJob_Ritual ritual, RitualOutcomeComp_Data data)
    {
        Pawn cook = GetAssignedPawn(ritual, "cook");
        Pawn eater = GetAssignedPawn(ritual, "eater");
        if (cook?.skills == null || eater?.skills == null)
        {
            return 0f;
        }

        int difference = Mathf.Abs(cook.skills.GetSkill(SkillDefOf.Melee).Level - eater.skills.GetSkill(SkillDefOf.Melee).Level);
        return difference <= 1 ? 0.4f : difference <= 4 ? 0.3f : difference <= 8 ? 0.2f : 0f;
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
        Pawn loser = Rand.Chance(cookWinChance) ? eater : cook;
        Pawn winner = loser == cook ? eater : cook;
        extraOutcomeDesc = "WCTM_SingleCombatWinner".Translate(winner.LabelShortCap);

        foreach (Pawn pawn in presence.Keys)
        {
            if (outcome.positivityIndex == 2) pawn.skills?.Learn(SkillDefOf.Melee, pawn == cook || pawn == eater ? 3500f : 1500f);
            if (outcome.positivityIndex == 3) pawn.skills?.Learn(SkillDefOf.Melee, pawn == cook || pawn == eater ? 5000f : 2000f);
        }

        if (loser == cook) WhoCookThisMealMod.AddCookAbasia(cook);
        else
        {
            Hediff abasia = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Abasia"), eater);
            abasia.TryGetComp<HediffComp_Disappears>()?.SetDuration(60000);
            eater.health.AddHediff(abasia);
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
