using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WhoCookThisMeal;

public sealed class WhoCookThisMealMod : Mod
{
    private static readonly Dictionary<Thing, CookRecord> fallbackRecords = new Dictionary<Thing, CookRecord>();
    private static readonly HashSet<IngestionRecord> poisonedMeals = new HashSet<IngestionRecord>();
    private static readonly HashSet<IngestionRecord> blockedPoisoning = new HashSet<IngestionRecord>();
    private static readonly HashSet<Pawn> cooksToRewardAfterAnesthetic = new HashSet<Pawn>();

    public WhoCookThisMealMod(ModContentPack content) : base(content)
    {
        AddComponentToMealDefs();

        var harmony = new Harmony("whocookthismeal");
        harmony.Patch(
            AccessTools.Method(typeof(Thing), nameof(Thing.Notify_RecipeProduced)),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(RecordCook)));
        harmony.Patch(
            AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff)),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(MarkFoodPoisoning)));
        harmony.Patch(
            AccessTools.Method(typeof(Thing), nameof(Thing.Ingested)),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(ResolveIngestion)));
        harmony.Patch(
            AccessTools.Method(typeof(ThingWithComps), nameof(ThingWithComps.InitializeComps)),
            prefix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(EnsureMealComponent)));
        harmony.Patch(
            AccessTools.Method(typeof(CompIngredients), nameof(CompIngredients.CompInspectStringExtra)),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(AppendCookInfo)));
        harmony.Patch(
            AccessTools.Method(typeof(FoodUtility), nameof(FoodUtility.AddFoodPoisoningHediff)),
            prefix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(BlockPoisoningForCarefulCook)));
        harmony.Patch(
            AccessTools.Method(typeof(HediffComp_Disappears), nameof(HediffComp_Disappears.CompPostPostRemoved)),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(RewardCookAfterAnesthetic)));
    }

    private static void EnsureMealComponent(ThingWithComps __instance)
    {
        if (__instance.def.ingestible?.IsMeal == true)
        {
            AddComponent(__instance.def);
        }
    }

    private static void AddComponentToMealDefs()
    {
        foreach (ThingDef thingDef in DefDatabase<ThingDef>.AllDefsListForReading)
        {
            if (thingDef.ingestible?.IsMeal != true)
            {
                continue;
            }

            AddComponent(thingDef);
        }
    }

    private static void AddComponent(ThingDef thingDef)
    {
        thingDef.comps ??= new List<CompProperties>();
        if (!thingDef.comps.Any(properties => properties.compClass == typeof(CompCookInfo)))
        {
            thingDef.comps.Add(new CompProperties_CookInfo());
        }
    }

    private static void RecordCook(Thing __instance, Pawn pawn)
    {
        if (__instance.def.ingestible?.IsMeal != true || !IsColonyMaker(pawn))
        {
            return;
        }

        fallbackRecords[__instance] = new CookRecord(pawn, pawn.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0);
        __instance.TryGetComp<CompCookInfo>()?.SetCook(pawn);
    }

    private static void AppendCookInfo(CompIngredients __instance, ref string __result)
    {
        if (__instance.parent.TryGetComp<CompCookInfo>()?.HasRecord == true ||
            !fallbackRecords.TryGetValue(__instance.parent, out CookRecord record))
        {
            return;
        }

        string cookInfo = "WCTM_CookedBy".Translate(record.Cook.LabelShortCap, record.CookingLevel);
        __result = __result.NullOrEmpty() ? cookInfo : __result + "\n" + cookInfo;
    }

    private static void MarkFoodPoisoning(Pawn pawn, Thing ingestible)
    {
        if (TryGetEligibleCook(ingestible, pawn, out _) && blockedPoisoning.Remove(new IngestionRecord(pawn, ingestible)))
        {
            return;
        }

        if (TryGetEligibleCook(ingestible, pawn, out _))
        {
            poisonedMeals.Add(new IngestionRecord(pawn, ingestible));
        }
    }

    private static bool BlockPoisoningForCarefulCook(Pawn pawn, Thing ingestible)
    {
        HediffDef carefulCooking = DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_CarefulCooking");
        if (carefulCooking == null || ingestible?.TryGetComp<CompCookInfo>()?.Cook?.health?.hediffSet.GetFirstHediffOfDef(carefulCooking) == null)
        {
            return true;
        }

        if (TryGetEligibleCook(ingestible, pawn, out _))
        {
            blockedPoisoning.Add(new IngestionRecord(pawn, ingestible));
        }

        return false;
    }

    internal static void AddCookAbasia(Pawn cook)
    {
        Hediff abasia = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("Abasia"), cook);
        HediffComp_Disappears disappears = abasia.TryGetComp<HediffComp_Disappears>();
        disappears?.SetDuration(60000);
        cooksToRewardAfterAnesthetic.Add(cook);
        cook.health.AddHediff(abasia);
    }

    private static void RewardCookAfterAnesthetic(HediffComp_Disappears __instance)
    {
        if (__instance.parent.def.defName != "Abasia" || !cooksToRewardAfterAnesthetic.Remove(__instance.Pawn))
        {
            return;
        }

        HediffDef carefulCooking = DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_CarefulCooking");
        if (carefulCooking != null && !__instance.Pawn.health.hediffSet.HasHediff(carefulCooking))
        {
            __instance.Pawn.health.AddHediff(HediffMaker.MakeHediff(carefulCooking, __instance.Pawn));
        }
    }

    private static void ResolveIngestion(Thing __instance, Pawn ingester)
    {
        if (!TryGetEligibleCook(__instance, ingester, out Pawn cook))
        {
            return;
        }

        bool poisoned = poisonedMeals.Remove(new IngestionRecord(ingester, __instance));
        string thoughtName = poisoned ? "WCTM_UnqualifiedCook" : "WCTM_MadeDeliciousFood";
        ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtName);
        ingester.needs?.mood?.thoughts?.memories.TryGainMemory(thoughtDef, cook);
    }

    private static bool TryGetEligibleCook(Thing meal, Pawn ingester, out Pawn cook)
    {
        cook = meal?.TryGetComp<CompCookInfo>()?.Cook;
        return meal?.def.ingestible?.IsMeal == true &&
            ingester != null && ingester.Faction == Faction.OfPlayer && ingester.IsColonist &&
            cook != null && cook != ingester && cook.Faction == Faction.OfPlayer && cook.IsColonist;
    }

    private readonly struct CookRecord
    {
        public readonly Pawn Cook;
        public readonly int CookingLevel;

        public CookRecord(Pawn cook, int cookingLevel)
        {
            Cook = cook;
            CookingLevel = cookingLevel;
        }
    }

    private readonly struct IngestionRecord : System.IEquatable<IngestionRecord>
    {
        private readonly Pawn ingester;
        private readonly Thing meal;

        public IngestionRecord(Pawn ingester, Thing meal)
        {
            this.ingester = ingester;
            this.meal = meal;
        }

        public bool Equals(IngestionRecord other)
        {
            return ingester == other.ingester && meal == other.meal;
        }

        public override bool Equals(object obj)
        {
            return obj is IngestionRecord other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((ingester?.GetHashCode() ?? 0) * 397) ^ (meal?.GetHashCode() ?? 0);
            }
        }
    }

    private static bool IsColonyMaker(Pawn pawn)
    {
        return pawn != null && pawn.Faction == Faction.OfPlayer && (pawn.IsColonist || pawn.IsColonyMech);
    }
}
