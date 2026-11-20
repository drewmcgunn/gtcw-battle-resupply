// Battle Resupply for Grand Tactician: The Civil War (1861-1865)
// Copyright (c) 2026 Drew McGunn. MIT License (see LICENSE).
//
// Ammunition resupply during the battle day, for both sides.
//
// The game refills regiments' ammunition only at the end of each battle day (BattleUnits.CheckTimeIssues ->
// LevelAmmo + ResupplyAllUnits), moving ammunition from each army's ammunition train (Regiment.supplyinstock on the
// top supply unit) into its regiments. During the day nothing refills, so high rate-of-fire weapons run dry long
// before nightfall.
//
// This plugin runs the same transfer in small doses during the day. Every StepMinutes of game time (a postfix on
// BattleUnits.CheckTimeIssues, battle map only), each regiment that is
//   - infantry, cavalry or artillery, not routed or surrendering;
//   - quiet: has not fired, been in melee, or been fired on for QuietMinutes;
//   - optionally within CommanderRange of its brigade headquarters (off by default);
// draws up to RefillPerHour of a full load per game hour from its train, using the game's own bookkeeping:
//   infantry/cavalry: supplyinstock[0] (men-loads), then Regiment.SetAmmoVariable(0);
//   artillery:        supplyinstock[1], then SetAmmoVariable(1, share).
// If a regiment's own train is empty (a train shrinks with its headquarters' losses), it can draw on the nearest
// friendly train of its nation that has stock (BorrowTrains, BorrowRange). The trains are finite, so a long fight can
// still exhaust them; the game's night refill is unchanged.
//
// Written for C# 5 so it builds with the csc.exe that ships with Windows.

using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("gtcw.battleresupply", "Battle Resupply", "1.0.0")]
public class BattleResupplyPlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    private static float RefillPerHour = 1f, QuietMinutes = 10f, StepMinutes = 5f, CommanderRange = 0f, BorrowRange = 0f;
    private static bool ShowText = true, BorrowTrains = true, LogHourly = true;
    private static float lastStep = -1f, lastLog;

    // per side since the last hourly line: men-loads delivered and refills; at the last step: regiments short, quiet,
    // and ammunition left in the trains
    private static readonly float[] delivered = new float[2], trainLeft = new float[2];
    private static readonly int[] served = new int[2], shortNow = new int[2], quietNow = new int[2];
    private static readonly HashSet<Regiment> beingSupplied = new HashSet<Regiment>();
    private static readonly List<Regiment>[] tops = { new List<Regiment>(), new List<Regiment>() };
    // per train at the last step: short, quiet, refilling, firing, under fire, in melee, no train to draw on
    private static readonly Dictionary<Regiment, int[]> stats = new Dictionary<Regiment, int[]>();
    private static readonly HashSet<string> borrowTold = new HashSet<string>();
    // BattleUnits.endbattle values once the battle is decided
    private static readonly HashSet<int> EndedStates = new HashSet<int> { 0, 1, 2, 4, 6, 7, 11, 13 };

    private void Awake()
    {
        Log = Logger;
        RefillPerHour = Config.Bind("Resupply", "RefillPerHour", 1f,
            "Share of a full ammunition load a quiet regiment can draw per game hour (1 = a full load in an hour).").Value;
        QuietMinutes = Config.Bind("Resupply", "QuietMinutes", 10f,
            "Game minutes without firing, melee or receiving fire before a regiment can be resupplied.").Value;
        StepMinutes = Config.Bind("Resupply", "StepMinutes", 5f, "How often (game minutes) resupply is applied.").Value;
        ShowText = Config.Bind("Resupply", "ShowText", true,
            "Show 'Resupplying ammunition' / 'Ammunition replenished' over your own regiments.").Value;
        CommanderRange = Config.Bind("Resupply", "CommanderRange", 0f,
            "If above 0, a regiment must be within this distance (map units) of its brigade headquarters. 0 = off.").Value;
        BorrowTrains = Config.Bind("Resupply", "BorrowTrains", true,
            "A regiment whose own ammunition train is empty draws on the nearest friendly train with stock.").Value;
        BorrowRange = Config.Bind("Resupply", "BorrowRange", 0f,
            "If above 0, a borrowed train must be within this distance (map units) of the regiment. 0 = any distance.").Value;
        LogHourly = Config.Bind("Log", "Hourly", true,
            "Each game hour, write each army's ammunition state, and each of your trains with regiments short, to the BepInEx log.").Value;
        new Harmony("gtcw.battleresupply").Patch(AccessTools.Method(typeof(BattleUnits), "CheckTimeIssues"), null,
            new HarmonyMethod(typeof(BattleResupplyPlugin).GetMethod("TimePostfix")));
        Log.LogInfo("Battle Resupply ready (" + (RefillPerHour * 100f) + "%/hour after " + QuietMinutes + " quiet minutes"
            + (CommanderRange > 0f ? ", within " + CommanderRange + " of brigade HQ" : "") + (BorrowTrains ? ", empty trains borrow" : "") + ").");
    }

    public static void TimePostfix(BattleUnits __instance)
    {
        try
        {
            if (__instance == null || __instance.IsCampaign || __instance.eodcycle != 0 || EndedStates.Contains(__instance.endbattle)) return;
            float now = GameVars.currenttimefromstart;   // battle hours
            if (lastStep < 0f || now < lastStep)   // a new battle, or the clock was reset
            {
                lastStep = now; lastLog = now; beingSupplied.Clear(); borrowTold.Clear(); stats.Clear();
                return;
            }
            float hours = now - lastStep;
            if (hours < StepMinutes / 60f) return;
            lastStep = now;
            if (hours > 1f) hours = 1f;   // after a pause or the night
            for (int side = 0; side < 2; side++)
            {
                shortNow[side] = 0; quietNow[side] = 0; trainLeft[side] = 0f;
                tops[side].Clear();
                if (__instance.allunitsofalliance == null || __instance.allunitsofalliance[side] == null) continue;
                foreach (Regiment top in __instance.allunitsofalliance[side].unitsobjectrefreg)
                    if (top != null && top.istopsupplyunit && top.supplyinstock != null) tops[side].Add(top);
            }
            stats.Clear();
            for (int side = 0; side < 2; side++)
                foreach (Regiment top in tops[side]) Supply(top, side, now, hours);
            if (now - lastLog >= 1f)
            {
                int mine = __instance.GetSideFromAlliance(GameVars.playeralliance);
                if (LogHourly)
                {
                    for (int side = 0; side < 2; side++)
                        Log.LogInfo(string.Format("{0} at {1:00}:{2:00}: {3} regiments short of ammunition, {4} of them quiet; "
                            + "{5} refills this hour ({6} men-loads); ammunition train left: {7} men-loads.",
                            side == mine ? "Your army" : "Enemy army",
                            Mathf.FloorToInt(now % 24f), Mathf.FloorToInt((now % 1f) * 60f), shortNow[side], quietNow[side],
                            served[side], Mathf.RoundToInt(delivered[side]), Mathf.RoundToInt(trainLeft[side])));
                    LogTrains(mine, now);
                }
                for (int side = 0; side < 2; side++) { delivered[side] = 0f; served[side] = 0; }
                lastLog = now;
            }
        }
        catch (System.Exception e) { Log.LogError("Battle resupply failed: " + e); }
    }

    private static void Supply(Regiment top, int side, float now, float hours)
    {
        float quiet = QuietMinutes / 60f;
        int[] st = new int[7];
        stats[top] = st;
        foreach (Regiment r in top.GetComponentsInChildren<Regiment>())
        {
            if (r == top || r.unittyp > 4 || r.isrouted || r.surrenderstate > 0 || r.strength <= 0 || r.supplyinstock == null) continue;
            int slot = r.unittyp == 2 ? 1 : 0;
            float need = Mathf.Max(0f, r.strength - r.supplyinstock[slot]);
            if (need < r.strength * 0.02f) { Finished(r); continue; }   // effectively full
            shortNow[side]++; st[0]++;
            int why = Busy(r, now, quiet);
            if (why > 0) { beingSupplied.Remove(r); st[2 + why]++; continue; }
            if (CommanderRange > 0f && !NearHeadquarters(r)) continue;
            quietNow[side]++; st[1]++;
            Regiment src = top.supplyinstock[slot] >= 1f ? top : (BorrowTrains ? Lender(r, top, side, slot) : null);
            if (src == null) { st[6]++; continue; }
            if (src != top && borrowTold.Add(top.gameObject.name + ">" + src.gameObject.name))
                Log.LogInfo((r.alliance == GameVars.playeralliance ? "Your " : "The enemy's ") + top.gameObject.name + " has no ammunition left in its train: its regiments draw on "
                    + src.gameObject.name + "'s (" + Mathf.RoundToInt(src.supplyinstock[slot]) + " " + (slot == 0 ? "men-loads" : "artillery loads") + ").");
            float give = Mathf.Min(need, Mathf.Min(r.strength * RefillPerHour * hours, src.supplyinstock[slot]));
            if (give <= 0f) continue;
            src.supplyinstock[slot] -= give;
            st[2]++;
            r.supplyinstock[slot] += give;
            if (slot == 0) r.SetAmmoVariable(0);
            else r.SetAmmoVariable(1, give / r.strength);
            delivered[side] += give;
            served[side]++;
            if (beingSupplied.Add(r)) Show(r, "Resupplying ammunition");
            if (need - give < r.strength * 0.02f) Finished(r);
        }
        trainLeft[side] += top.supplyinstock[0] + top.supplyinstock[1];
    }

    // 0 quiet, 1 firing, 2 under fire, 3 in melee
    private static int Busy(Regiment r, float now, float quiet)
    {
        if (r.inmelee > 0f || (r.timeoflastmelee > 0f && now - r.timeoflastmelee < quiet)) return 3;
        if (r.lastfiredshottime > 0f && now - r.lastfiredshottime < quiet) return 1;
        if (r.receivedfire != null)
            foreach (Regiment.ReceivedFire f in r.receivedfire)
                if (f != null && f.timeoffire > 0f && now - f.timeoffire < quiet) return 2;
        return 0;
    }

    // the nearest other train of the regiment's nation with stock in this slot
    private static Regiment Lender(Regiment r, Regiment own, int side, int slot)
    {
        Regiment best = null; float bd = float.MaxValue;
        foreach (Regiment t in tops[side])
        {
            if (t == null || t == own || t.alliance != r.alliance || t.supplyinstock == null || t.supplyinstock.Length <= slot || t.supplyinstock[slot] < 1f) continue;
            float d = Tools.GetXZDistance(t.GetPosition(), r.GetPosition());
            if (BorrowRange > 0f && d > BorrowRange) continue;
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }

    // each of the player's trains with regiments short, and why they aren't refilling
    private static void LogTrains(int side, float now)
    {
        if (side < 0 || side > 1) return;
        List<string> rows = new List<string>();
        foreach (Regiment top in tops[side])
        {
            int[] st;
            if (top == null || !stats.TryGetValue(top, out st) || st[0] == 0) continue;
            string why = "";
            if (st[3] > 0) why += ", " + st[3] + " firing";
            if (st[4] > 0) why += ", " + st[4] + " under fire";
            if (st[5] > 0) why += ", " + st[5] + " in melee";
            if (st[6] > 0) why += ", " + st[6] + " with no train to draw on";
            rows.Add(top.gameObject.name + ": train " + Mathf.RoundToInt(top.supplyinstock[0]) + " men-loads"
                + (top.supplyinstock.Length > 1 && top.supplyinstock[1] >= 1f ? " + " + Mathf.RoundToInt(top.supplyinstock[1]) + " artillery" : "")
                + "; " + st[0] + " short, " + st[1] + " quiet, " + st[2] + " refilling" + why);
        }
        if (rows.Count > 0)
            Log.LogInfo("Your trains at " + Mathf.FloorToInt(now % 24f).ToString("00") + ":" + Mathf.FloorToInt((now % 1f) * 60f).ToString("00") + ":\n    " + string.Join("\n    ", rows.ToArray()));
    }

    private static void Finished(Regiment r)
    {
        if (beingSupplied.Remove(r)) Show(r, "Ammunition replenished");
    }

    // floating text over the player's own regiments (the game's RollingText)
    private static void Show(Regiment r, string text)
    {
        if (!ShowText || r.alliance != GameVars.playeralliance) return;
        try { RollingText.Add(r.GetPosition(), text, RollingText.type_bprestige, r.gameObject, false, "Battle Resupply", text, 0f); }
        catch (System.Exception e) { Log.LogWarning("Floating text failed: " + e.Message); }
    }

    // the regiment's parent group (its brigade) stands in for the ammunition wagons
    private static bool NearHeadquarters(Regiment r)
    {
        Transform p = r.transform.parent;
        Regiment hq = p != null ? p.GetComponentInParent<Regiment>() : null;
        if (hq == null) return true;
        return Tools.GetXZDistance(r.GetPosition(), hq.GetPosition()) <= CommanderRange;
    }
}
