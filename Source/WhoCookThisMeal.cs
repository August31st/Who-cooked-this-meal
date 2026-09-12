using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace WhoCookThisMeal;

public sealed class WhoCookThisMealMod : Mod
{
    public static WhoCookThisMealSettings Settings { get; private set; }
    private static readonly Dictionary<Thing, CookRecord> fallbackRecords = new Dictionary<Thing, CookRecord>();
    private static readonly HashSet<IngestionRecord> blockedPoisoning = new HashSet<IngestionRecord>();

    public WhoCookThisMealMod(ModContentPack content) : base(content)
    {
        Settings = GetSettings<WhoCookThisMealSettings>();
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
            AccessTools.Method(typeof(LordJob_Ritual_Duel), "StartMoving"),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(ConfigureDuelMovingStage)));
        harmony.Patch(
            AccessTools.Method(typeof(LordJob_Ritual_Duel), "StartAttacking"),
            postfix: new HarmonyMethod(typeof(WhoCookThisMealMod), nameof(ConfigureDuelAttackStage)));
    }

    public override string SettingsCategory() => Content.Name;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);
        bool peaceful = Settings.PeacefulSingleCombat;
        listing.CheckboxLabeled("WCTM_PeacefulSingleCombat".Translate(), ref Settings.PeacefulSingleCombat, "WCTM_PeacefulSingleCombatDescription".Translate());
        bool bareFistedCombat = Settings.BareFistedCombat;
        listing.CheckboxLabeled("WCTM_BareFistedCombat".Translate(), ref Settings.BareFistedCombat, "WCTM_BareFistedCombatDescription".Translate());
        int oldAttacksPerStage = Settings.AttacksPerStage;
        int oldMovingTicksPerStage = Settings.MovingTicksPerStage;
        float attacksPerStage = Settings.AttacksPerStage;
        listing.Label("WCTM_AttacksPerStage".Translate(Mathf.RoundToInt(attacksPerStage), 1, 20));
        attacksPerStage = listing.Slider(attacksPerStage, 1f, 20f);
        Settings.AttacksPerStage = Mathf.RoundToInt(attacksPerStage);
        float movingTicksPerStage = Settings.MovingTicksPerStage;
        listing.Label("WCTM_MovingTicksPerStage".Translate(Mathf.RoundToInt(movingTicksPerStage), 20, 600));
        movingTicksPerStage = listing.Slider(movingTicksPerStage, 20f, 600f);
        Settings.MovingTicksPerStage = Mathf.RoundToInt(movingTicksPerStage / 20f) * 20;
        if (peaceful != Settings.PeacefulSingleCombat || bareFistedCombat != Settings.BareFistedCombat ||
            oldAttacksPerStage != Settings.AttacksPerStage || oldMovingTicksPerStage != Settings.MovingTicksPerStage)
        {
            Settings.Write();
        }
        listing.End();
    }

    private static void ConfigureDuelMovingStage(LordJob_Ritual_Duel __instance)
    {
        if (__instance is LordJob_WCTMSingleCombatDuel)
        {
            AccessTools.Field(typeof(LordJob_Ritual_Duel), "movingTicks").SetValue(__instance, Mathf.Clamp(Settings.MovingTicksPerStage, 20, 600));
        }
    }

    private static void ConfigureDuelAttackStage(LordJob_Ritual_Duel __instance)
    {
        if (__instance is LordJob_WCTMSingleCombatDuel)
        {
            AccessTools.Field(typeof(LordJob_Ritual_Duel), "attacksThisStage").SetValue(__instance, Mathf.Clamp(Settings.AttacksPerStage, 1, 20));
        }
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

        fallbackRecords[__instance] = new CookRecord(pawn, CompCookInfo.GetCookingLevel(pawn));
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
        if (TryGetMealCook(ingestible, pawn, out _) && blockedPoisoning.Remove(new IngestionRecord(pawn, ingestible)))
        {
            return;
        }

        if (TryGetMealCook(ingestible, pawn, out _))
        {
            RecordPoisoningOpinion(pawn, ingestible);
        }
    }

    private static bool BlockPoisoningForCarefulCook(Pawn pawn, Thing ingestible)
    {
        Pawn cook = ingestible?.TryGetComp<CompCookInfo>()?.Cook;
        if (!HasSafeCookingHediff(cook))
        {
            return true;
        }

        if (TryGetMealCook(ingestible, pawn, out _))
        {
            blockedPoisoning.Add(new IngestionRecord(pawn, ingestible));
        }

        return false;
    }

    internal static void AddCarefulCooking(Pawn cook)
    {
        HediffDef carefulCooking = DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_CarefulCooking");
        if (carefulCooking != null && cook?.health?.hediffSet != null && !cook.health.hediffSet.HasHediff(carefulCooking))
        {
            Hediff buff = HediffMaker.MakeHediff(carefulCooking, cook);
            buff.TryGetComp<HediffComp_Disappears>()?.SetDuration(180000);
            cook.health.AddHediff(buff);
        }
    }

    private static void ResolveIngestion(Thing __instance, Pawn ingester)
    {
        if (!TryGetMealCook(__instance, ingester, out Pawn cook))
        {
            return;
        }

        if (cook.IsColonyMech)
        {
            return;
        }

        ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_MadeDeliciousFood");
        if (thoughtDef != null)
        {
            ingester.needs?.mood?.thoughts?.memories.TryGainMemory(thoughtDef, cook);
        }
    }

    private static void RecordPoisoningOpinion(Pawn ingester, Thing meal)
    {
        if (!TryGetMealCook(meal, ingester, out Pawn cook))
        {
            return;
        }

        if (cook.IsColonyMech)
        {
            ResolveMechPoisoning(cook, ingester);
            return;
        }

        ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_UnqualifiedCook");
        if (thoughtDef != null)
        {
            ingester.needs?.mood?.thoughts?.memories.TryGainMemory(thoughtDef, cook);
        }
    }

    private static void ResolveMechPoisoning(Pawn mech, Pawn ingester)
    {
        Pawn mechanitor = mech.GetOverseer();
        if (mechanitor == null)
        {
            return;
        }

        if (ingester != mechanitor)
        {
            ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail("WCTM_MechMealPoisoning");
            ingester.needs?.mood?.thoughts?.memories.TryGainMemory(thoughtDef, mechanitor);
            return;
        }

        HediffDef upgrade = DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_MechCookingUpgrade");
        if (upgrade != null && !mech.health.hediffSet.HasHediff(upgrade))
        {
            mech.health.AddHediff(HediffMaker.MakeHediff(upgrade, mech));
        }
    }

    private static bool HasSafeCookingHediff(Pawn cook)
    {
        if (cook?.health?.hediffSet == null)
        {
            return false;
        }

        return cook.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_CarefulCooking")) != null ||
            cook.health.hediffSet.GetFirstHediffOfDef(DefDatabase<HediffDef>.GetNamedSilentFail("WCTM_MechCookingUpgrade")) != null;
    }

    private static bool TryGetMealCook(Thing meal, Pawn ingester, out Pawn cook)
    {
        cook = meal?.TryGetComp<CompCookInfo>()?.Cook;
        return meal?.def.ingestible?.IsMeal == true &&
            ingester != null && ingester.Faction == Faction.OfPlayer && ingester.IsColonist &&
            cook != ingester && IsColonyMaker(cook);
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
