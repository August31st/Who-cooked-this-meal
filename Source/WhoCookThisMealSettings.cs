using Verse;

namespace WhoCookThisMeal;

public sealed class WhoCookThisMealSettings : ModSettings
{
    public bool PeacefulSingleCombat = true;
    public bool BareFistedCombat;
    public int AttacksPerStage = 6;
    public int MovingTicksPerStage = 360;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref PeacefulSingleCombat, "peacefulSingleCombat", true);
        Scribe_Values.Look(ref BareFistedCombat, "bareFistedCombat", false);
        Scribe_Values.Look(ref AttacksPerStage, "attacksPerStage", 6);
        Scribe_Values.Look(ref MovingTicksPerStage, "movingTicksPerStage", 360);
    }
}