using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using UnityEngine;

namespace WhoCookThisMeal;

public sealed class JobGiver_WCTMSingleCombatMove : JobGiver_Wander
{
    private static readonly Dictionary<LordJob_Ritual, int> MotionStartTicks = new Dictionary<LordJob_Ritual, int>();

    public JobGiver_WCTMSingleCombatMove()
    {
        wanderRadius = 1f;
        ticksBetweenWandersRange = new IntRange(20, 30);
        locomotionUrgency = LocomotionUrgency.Walk;
        maxDanger = Danger.None;
    }

    protected override IntVec3 GetWanderRoot(Pawn pawn)
    {
        return pawn.mindState.duty?.focus.Cell ?? pawn.Position;
    }

    protected override Job TryGiveJob(Pawn pawn)
    {
        if (!WhoCookThisMealMod.Settings.PeacefulSingleCombat)
        {
            return JobGiver_WCTMOriginalDuel.GetJob(pawn);
        }

        LordJob_Ritual ritual = pawn.GetLord()?.LordJob as LordJob_Ritual;
        if (ritual == null)
        {
            return base.TryGiveJob(pawn);
        }

        Pawn cook = GetParticipant(ritual, "cook");
        Pawn eater = GetParticipant(ritual, "eater");
        if (cook == null || eater == null)
        {
            return base.TryGiveJob(pawn);
        }

        IntVec3 center = ritual.Spot;
        IntVec3 startCell = StartCell(center, pawn, ritual);
        if (!MotionStartTicks.ContainsKey(ritual))
        {
            if (cook.Position == StartCell(center, cook, ritual) && eater.Position == StartCell(center, eater, ritual))
            {
                MotionStartTicks[ritual] = GenTicks.TicksGame;
            }
            else
            {
                return MoveOrWait(pawn, startCell);
            }
        }

        int elapsed = GenTicks.TicksGame - MotionStartTicks[ritual];
        IntVec3 target = OrbitCell(center, elapsed, pawn == cook ? 0 : 1, pawn.Map);
        return MoveOrWait(pawn, target);
    }

    private static Job MoveOrWait(Pawn pawn, IntVec3 target)
    {
        if (pawn.Position == target)
        {
            Job waitJob = JobMaker.MakeJob(JobDefOf.Wait, 20);
            waitJob.checkOverrideOnExpire = false;
            return waitJob;
        }

        Job moveJob = JobMaker.MakeJob(JobDefOf.Goto, target);
        moveJob.expiryInterval = 60;
        moveJob.checkOverrideOnExpire = false;
        moveJob.locomotionUrgency = LocomotionUrgency.Walk;
        return moveJob;
    }

    private static Pawn GetParticipant(LordJob_Ritual ritual, string roleId)
    {
        RitualRole role = ritual.assignments?.GetRole(roleId);
        return role == null ? null : ritual.assignments.FirstAssignedPawn(role);
    }

    private static IntVec3 StartCell(IntVec3 center, Pawn pawn, LordJob_Ritual ritual)
    {
        bool cook = ritual.RoleFor(pawn)?.id == "cook";
        return center + new IntVec3(cook ? -1 : 1, 0, 0);
    }

    private static IntVec3 OrbitCell(IntVec3 center, int elapsed, int side, Map map)
    {
        float angle;
        float radius;
        if (elapsed < 360)
        {
            angle = (elapsed / 360f) * Mathf.PI * 2f + (side * Mathf.PI);
            radius = 2f;
        }
        else if (elapsed < 720)
        {
            float progress = (elapsed - 360) / 360f;
            angle = Mathf.PI * 2f + (progress * Mathf.PI * 2f) + (side * Mathf.PI);
            radius = Mathf.Lerp(2f, 0f, progress);
        }
        else
        {
            float progress = Mathf.Min((elapsed - 720) / 360f, 1f);
            angle = Mathf.PI * 2f - (progress * Mathf.PI * 2f) + (side * Mathf.PI);
            radius = Mathf.Lerp(0f, 3f, progress);
            if (elapsed >= 1080)
            {
                angle -= ((elapsed - 1080) / 360f) * Mathf.PI * 2f;
            }
        }

        IntVec3 cell = center + new IntVec3(Mathf.RoundToInt(Mathf.Cos(angle) * radius), 0, Mathf.RoundToInt(Mathf.Sin(angle) * radius));
        return cell.InBounds(map) && cell.Standable(map) ? cell : center;
    }

    private sealed class JobGiver_WCTMOriginalDuel : JobGiver_Duel
    {
        public static Job GetJob(Pawn pawn)
        {
            return new JobGiver_WCTMOriginalDuel().TryGiveJob(pawn);
        }
    }
}

public sealed class RitualPositionWCTMSide : RitualPosition
{
    public int side;

    public override PawnStagePosition GetCell(IntVec3 spot, Pawn pawn, LordJob_Ritual ritual)
    {
        IntVec3 cell = spot + new IntVec3(side == 0 ? -1 : 1, 0, 0);
        if (!cell.InBounds(pawn.Map) || !cell.Standable(pawn.Map))
        {
            cell = spot;
        }

        return new PawnStagePosition(cell, null, side == 0 ? Rot4.East : Rot4.West, highlight);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref side, "side", 0);
    }
}
