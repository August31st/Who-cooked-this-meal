using System.Collections.Generic;
using RimWorld;
using Verse;

namespace WhoCookThisMeal;

public sealed class CompProperties_CookInfo : CompProperties
{
    public CompProperties_CookInfo()
    {
        compClass = typeof(CompCookInfo);
    }
}

public sealed class CompCookInfo : ThingComp
{
    private Pawn cook;
    private int cookingLevel;

    public bool HasRecord => cook != null;
    public Pawn Cook => cook;
    public int CookingLevel => cookingLevel;

    public void SetCook(Pawn worker)
    {
        cook = worker;
        cookingLevel = worker.skills?.GetSkill(SkillDefOf.Cooking)?.Level ?? 0;
    }

    public override void PostExposeData()
    {
        base.PostExposeData();
        Scribe_References.Look(ref cook, "cook");
        Scribe_Values.Look(ref cookingLevel, "cookingLevel");
    }

    public override void PostSplitOff(Thing piece)
    {
        base.PostSplitOff(piece);
        if (piece.TryGetComp<CompCookInfo>() is CompCookInfo splitComp)
        {
            splitComp.cook = cook;
            splitComp.cookingLevel = cookingLevel;
        }
    }

    public override bool AllowStackWith(Thing otherStack)
    {
        if (otherStack.TryGetComp<CompCookInfo>() is not CompCookInfo otherComp)
        {
            return false;
        }

        return cook == otherComp.cook && cookingLevel == otherComp.cookingLevel;
    }

    public override string CompInspectStringExtra()
    {
        if (cook == null)
        {
            return null;
        }

        return "WCTM_CookedBy".Translate(cook.LabelShortCap, cookingLevel);
    }

    public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
    {
        if (cook != null)
        {
            yield return new StatDrawEntry(
                StatCategoryDefOf.BasicsNonPawnImportant,
                "WCTM_CookedByLabel".Translate(),
                "WCTM_CookedBy".Translate(cook.LabelShortCap, cookingLevel),
                "WCTM_CookedByDesc".Translate(),
                994);
        }
    }
}
