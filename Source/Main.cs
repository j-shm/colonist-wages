using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using Verse.Sound;
using Verse.Noise;
using Verse.Grammar;
using RimWorld;
using RimWorld.Planet;

// *Uncomment for Harmony*
// using System.Reflection;
// using HarmonyLib;

namespace ColonistWages
{
    [DefOf]
    public class ColonistWagesDefOf
    {
        public static ThoughtDef Payday;
        public static ThoughtDef PaydayFail;
        public static ThoughtDef Paycut;
        public static ThoughtDef Payrise;
        public static HediffDef f_colonist_wage;
        public static LetterDef payday_letter;
        public static LetterDef payday_missed_letter;
    }
    public class WealthExpectationSettings : ModSettings
    {
        public float wealthPercentage = 100f;
        private string bufferPercentage = "100";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref wealthPercentage, "wealthPercentage", 100f);
            base.ExposeData();
        }
    }

    public class WealthExpectationMod : Mod
    {
        private WealthExpectationSettings settings;
        private string bufferPercentage = "1";

        public WealthExpectationMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<WealthExpectationSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.Label($"Expected Wealth to be Paid to Pawns: {settings.wealthPercentage:F2}%");

            listing.Label("Enter percentage (0.01% to 100%):");
            Rect textRect = listing.GetRect(Text.LineHeight);
            bufferPercentage = Widgets.TextField(textRect, bufferPercentage);

            if (listing.ButtonText("Apply"))
            {
                if (float.TryParse(bufferPercentage, out float newValue))
                {
                    settings.wealthPercentage = Mathf.Clamp(newValue, 0.01f, 100f);
                    bufferPercentage = settings.wealthPercentage.ToString("F2");
                }
            }

            settings.wealthPercentage = listing.Slider(settings.wealthPercentage, 0.01f, 100f);

            listing.GapLine();
            listing.Label("Quick Presets (recommended values):");
            Rect presetsRect = listing.GetRect(28f);
            float buttonWidth = presetsRect.width / 4f;

            if (Widgets.ButtonText(new Rect(presetsRect.x, presetsRect.y, buttonWidth, 28f), "0.25%"))
            {
                settings.wealthPercentage = 0.25f;
                bufferPercentage = "0.25";
            }
            if (Widgets.ButtonText(new Rect(presetsRect.x + buttonWidth, presetsRect.y, buttonWidth, 28f), "0.5%"))
            {
                settings.wealthPercentage = 0.25f;
                bufferPercentage = "0.50";
            }
            if (Widgets.ButtonText(new Rect(presetsRect.x + buttonWidth * 2f, presetsRect.y, buttonWidth, 28f), "0.5%"))
            {
                settings.wealthPercentage = 0.25f;
                bufferPercentage = "0.75";
            }
            if (Widgets.ButtonText(new Rect(presetsRect.x + buttonWidth * 3f, presetsRect.y, buttonWidth, 28f), "1%"))
            {
                settings.wealthPercentage = 1f;
                bufferPercentage = "1";
            }

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        public override string SettingsCategory()
        {
            return "Wealth Expectation";
        }
    }

    [StaticConstructorOnStartup]
    public static class Start
    {
        static Start()
        {
            Log.Message("Colonist Wages loaded successfully!");
        }
    }
    public static class ColonistWagesUtils
    {
        public static List<Pawn> GetAllPlayerFactionPawns()
        {
            List<Map> maps = Find.Maps;

            List<Pawn> playerFactionPawns = new List<Pawn>();

            foreach (var map in maps)
            {
                var pawns = map.mapPawns.AllPawns
                    .Where(pawn => pawn.Faction == Faction.OfPlayer && pawn.IsColonist && pawn.IsSlave == false)
                    .ToList();
                playerFactionPawns.AddRange(pawns);
            }

            return playerFactionPawns;
        }
        public static bool IsFactionColonist(Pawn p)
        {
            return p.Faction == Faction.OfPlayer && p.IsColonist && p.IsSlave == false;
        }
        public static int GetTotalSilverOwnedByFaction(Map map, Faction faction)
        {
            if (map == null)
            {
                return 0;
            }

            if (faction == null)
            {
                return 0;
            }

            List<Thing> silverThings = map.listerThings.ThingsOfDef(ThingDefOf.Silver);

            int totalSilver = silverThings
                .Where(silver => silver.Faction == faction || silver.IsInAnyStorage())
                .Sum(silver => silver.stackCount);

            return totalSilver;
        }
        public static float GetWage(Pawn p)
        {
            if (p.Faction != Faction.OfPlayer)
            {
                return 0;
            }
            var wageHediff = p?.health?.hediffSet?.GetFirstHediffOfDef(ColonistWagesDefOf.f_colonist_wage);
            if (wageHediff == null)
                return 0;
            var comp = wageHediff.TryGetComp<HediffComp_ColonistWageAmount>();

            if (comp == null)
                return 0;
            return comp.wage;
        }
        public static float GetExpectedWage()
        {
            WealthExpectationSettings settings = LoadedModManager.GetMod<WealthExpectationMod>().GetSettings<WealthExpectationSettings>();
            return Find.CurrentMap.wealthWatcher.WealthTotal * settings.wealthPercentage / 100;
        }

        public static void ApplyThought(Pawn pawn, ThoughtDef thoughtDef)
        {
            if (pawn == null || pawn.needs == null || pawn.needs.mood == null) return;

            ThoughtDef thought = thoughtDef;

            if (thought != null)
            {

                Thought_Memory memory = ThoughtMaker.MakeThought(thought) as Thought_Memory;
                pawn.needs.mood.thoughts.memories.TryGainMemory(memory);
            }
        }
    }
    public static class ColonistWagesFunctions
    {
        public static void ApplyWages(List<Pawn> colonists, Dictionary<string, float> pawnValues)
        {
            foreach (Pawn pawn in colonists)
            {
                string pawnId = pawn.ThingID;
                if (pawnValues.ContainsKey(pawnId))
                {
                    float wage = pawnValues[pawnId];
                    var wageHediff = pawn?.health?.hediffSet?.GetFirstHediffOfDef(ColonistWagesDefOf.f_colonist_wage);
                    if (wageHediff == null)
                    {
                        wageHediff = pawn?.health?.AddHediff(ColonistWagesDefOf.f_colonist_wage);
                    }
                    var comp = wageHediff.TryGetComp<HediffComp_ColonistWageAmount>();

                    if (comp != null)
                    {
                        if (comp.wage > wage)
                        {
                            ColonistWagesUtils.ApplyThought(pawn, thoughtDef: ColonistWagesDefOf.Paycut);
                        }
                        else if (comp.wage < wage)
                        {
                            ColonistWagesUtils.ApplyThought(pawn, thoughtDef: ColonistWagesDefOf.Payrise);
                        }
                        comp.wage = (int)wage;
                    }
                }
            }
        }

        public static void ApplyPayday()
        {
            var pawns = ColonistWagesUtils.GetAllPlayerFactionPawns();
            float totalWages = 0;
            foreach (var p in pawns)
            {
                if (p == null)
                    continue;
                var wage = ColonistWagesUtils.GetWage(p);
                totalWages += wage;
            }

            if (totalWages > ColonistWagesUtils.GetTotalSilverOwnedByFaction(Find.CurrentMap, Faction.OfPlayer))
            {
                Find.LetterStack.ReceiveLetter(new TaggedString("Payday"), new TaggedString("It's payday - not enough silver to pay all pawns."), ColonistWagesDefOf.payday_missed_letter, "", 0);
                foreach (var p in pawns)
                {
                    if (ColonistWagesUtils.GetWage(p) > 0)
                        ColonistWagesUtils.ApplyThought(p, thoughtDef: ColonistWagesDefOf.PaydayFail);
                }
            }
            else
            {

                Find.LetterStack.ReceiveLetter(new TaggedString("Payday"), new TaggedString("It's payday - all pawns have been paid."), ColonistWagesDefOf.payday_letter, "", 0);
                foreach (var p in pawns)
                {
                    if (ColonistWagesUtils.GetWage(p) > 0)
                        ColonistWagesUtils.ApplyThought(p, thoughtDef: ColonistWagesDefOf.Payday);
                }
                var silver = Find.CurrentMap.listerThings.ThingsOfDef(ThingDefOf.Silver).Where(s => s.Faction == Faction.OfPlayer || s.IsInAnyStorage()).ToList();
                var silverRemoved = 0;
                foreach (var thing in silver)
                {
                    if (silverRemoved >= totalWages)
                        break;

                    int removeAmount = (int)Math.Min(thing.stackCount, totalWages - silverRemoved);
                    thing.stackCount -= removeAmount;
                    silverRemoved += removeAmount;

                    if (thing.stackCount <= 0)
                        thing.Destroy(DestroyMode.Vanish);
                }
            }

        }
    }
    public static class QuadrumCalculator
    {
        private static Quadrum lastQuadram = Quadrum.Undefined;

        public static void ExposeData()
        {
            Scribe_Values.Look<Quadrum>(ref lastQuadram, "lastQuadram", Quadrum.Undefined, false);
        }

        private static Map FindPlayerHomeWithMinTimezone()
        {
            List<Map> maps = Find.Maps;
            Map map = null;
            int num = -1;
            for (int i = 0; i < maps.Count; i++)
            {
                if (maps[i].IsPlayerHome)
                {
                    int num2 = GenDate.TimeZoneAt(Find.WorldGrid.LongLatOf(maps[i].Tile).x);
                    if (map == null || num2 < num)
                    {
                        map = maps[i];
                        num = num2;
                    }
                }
            }
            return map;
        }

        public static bool IsNewQuadrum()
        {
            Map map = FindPlayerHomeWithMinTimezone();
            float latitude = (map != null) ? Find.WorldGrid.LongLatOf(map.Tile).y : 0f;
            float longitude = (map != null) ? Find.WorldGrid.LongLatOf(map.Tile).x : 0f;
            Quadrum quadrum = GenDate.Quadrum((long)Find.TickManager.TicksAbs, longitude);

            if (quadrum != lastQuadram)
            {
                if (lastQuadram != Quadrum.Undefined)
                {
                    lastQuadram = quadrum;
                    return true;
                }
                lastQuadram = quadrum;
            }
            return false;
        }
    }
    public class GlobalTickComponent : GameComponent
    {
        public GlobalTickComponent(Game game)
        {
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();

            if (QuadrumCalculator.IsNewQuadrum())
            {
                ColonistWagesFunctions.ApplyPayday();
            }
        }


    }

    public class MainTabWindow_ColonistWage : MainTabWindow
    {
        private const float RowHeight = 30f;
        private const float ScreenHeightPercent = 0.5f;
        private static Vector2 scrollPosition;

        // Dictionaries to store values for each pawn
        private Dictionary<string, float> pawnValues = new Dictionary<string, float>();
        private Dictionary<string, string> pawnBuffers = new Dictionary<string, string>();
        private bool buttonPressed = false;
        private int buttonPressedTimestamp = 0;

        public override Vector2 InitialSize => new Vector2(500f, UI.screenHeight * ScreenHeightPercent);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Rect titleRect = new Rect(inRect.x, inRect.y, inRect.width, 35f);
            Rect expectedWageRect = new Rect(inRect.x, inRect.y + 30, inRect.width, 35f);
            Widgets.Label(titleRect, "Colonist Wages - Quadrum");
            Widgets.Label(expectedWageRect, "Pawn Expected Wage: " + ColonistWagesUtils.GetExpectedWage().ToString("F2"));

            Text.Font = GameFont.Small;

            Rect contentRect = new Rect(
                inRect.x,
                expectedWageRect.yMax + 10f,
                inRect.width,
                inRect.height - expectedWageRect.yMax - 50f
            );

            List<Pawn> colonists = ColonistWagesUtils.GetAllPlayerFactionPawns();
            float viewHeight = (colonists.Count * RowHeight) + RowHeight; 
            Rect viewRect = new Rect(0f, 0f, contentRect.width - 16f, viewHeight);

            Widgets.BeginScrollView(contentRect, ref scrollPosition, viewRect);

            float curY = 0f;

            DrawHeader(new Rect(0f, curY, viewRect.width, RowHeight));
            curY += RowHeight;

            foreach (Pawn pawn in colonists)
            {
                string pawnId = pawn.ThingID;
                if (!pawnValues.ContainsKey(pawnId))
                {
                    pawnValues[pawnId] = ColonistWagesUtils.GetWage(pawn);
                    pawnBuffers[pawnId] = ColonistWagesUtils.GetWage(pawn).ToString();
                }

                Rect rowRect = new Rect(0f, curY, viewRect.width, RowHeight);

                if (Mouse.IsOver(rowRect))
                    Widgets.DrawHighlight(rowRect);

                Rect nameRect = new Rect(rowRect.x + 5f, rowRect.y, rowRect.width * 0.6f, rowRect.height);
                GUI.color = Color.white;
                Widgets.Label(nameRect, pawn.Name.ToStringFull);

                Rect inputRect = new Rect(nameRect.xMax + 10f, rowRect.y + 3f, 100f, rowRect.height - 6f);
                float value = pawnValues[pawnId];
                string buffer = pawnBuffers[pawnId];
                Widgets.TextFieldNumeric(inputRect, ref value, ref buffer, 0f, 999999f);
                pawnValues[pawnId] = value;
                pawnBuffers[pawnId] = buffer;

                Widgets.DrawLineHorizontal(0f, curY + RowHeight - 1f, viewRect.width);
                curY += RowHeight;
            }

            Widgets.EndScrollView();


            if (buttonPressed && Find.TickManager.TicksGame - buttonPressedTimestamp > 30)
            {
                buttonPressed = false;
            }
            Rect submitButtonRect = new Rect(inRect.x + inRect.width / 2 - 50f, contentRect.yMax + 10f, 100f, 30f);
            if (Widgets.ButtonText(submitButtonRect, buttonPressed ? "Submitted" : "Submit"))
            {
                if (!buttonPressed)
                {
                    ColonistWagesFunctions.ApplyWages(colonists, pawnValues);
                    buttonPressed = true;
                    buttonPressedTimestamp = Find.TickManager.TicksGame;
                }
            }
        }
        private void DrawHeader(Rect rect)
        {
            Rect nameHeader = new Rect(rect.x + 5f, rect.y, rect.width * 0.6f, rect.height);
            Rect inputHeader = new Rect(nameHeader.xMax + 10f, rect.y, 100f, rect.height);

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.LowerLeft;
            Widgets.Label(nameHeader, "Colonist");
            Widgets.Label(inputHeader, "Wage");
            Text.Anchor = TextAnchor.UpperLeft;

            Widgets.DrawLineHorizontal(0f, rect.yMax - 1f, rect.width);
        }
    }



    public class Hediff_ColonistWage : HediffWithComps
    {
        public override bool Visible
        {
            get
            {
                return false;
            }
        }
        public override void ExposeData()
        {
            base.ExposeData();
            var comp = this.TryGetComp<HediffComp_ColonistWageAmount>();
            if (comp != null)
            {
                Scribe_Values.Look(ref comp.wage, "wage", 0);
            }
        }
    }
    public class HediffComp_ColonistWageAmount : HediffComp
    {
        public int wage = 0;

    }
    public class FRG_ThoughtWorker_Underpaid : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!ColonistWagesUtils.IsFactionColonist(p))
            {
                return ThoughtState.Inactive;
            }
            var wage = ColonistWagesUtils.GetWage(p);
            var expectedWage = ColonistWagesUtils.GetExpectedWage();
            if (wage > expectedWage)
            {
                return ThoughtState.Inactive;
            }
            var wagePercent = wage / expectedWage;
            if (wagePercent < 0.25)
            {
                return ThoughtState.ActiveAtStage(3);
            }
            else if (wagePercent < 0.5)
            {
                return ThoughtState.ActiveAtStage(2);
            }
            else if (wagePercent < 0.75)
            {
                return ThoughtState.ActiveAtStage(1);
            }
            else if (wagePercent < 1)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive;
        }
    }
    public class FRG_ThoughtWorker_NoWage : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!ColonistWagesUtils.IsFactionColonist(p))
            {
                return ThoughtState.Inactive;
            }
            var wage = ColonistWagesUtils.GetWage(p);
            if (wage == 0)
            {
                return ThoughtState.ActiveAtStage(0);
            }
            return ThoughtState.Inactive;
        }
    }
}




