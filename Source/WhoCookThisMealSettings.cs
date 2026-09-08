using Verse;

namespace WhoCookThisMeal;

public sealed class WhoCookThisMealSettings : ModSettings
{
    public bool PeacefulSingleCombat = true;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref PeacefulSingleCombat, "peacefulSingleCombat", true);
    }
}