using System.Collections.Generic;
using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M5-T2/T10: both fuel routes exist in the catalog and every landable body
    /// can actually open a landing region with body-correct deposits.</summary>
    public sealed class BodiesAndFuelTests
    {
        [Test]
        public void BothFuelRoutes_ExistInCatalog()
        {
            var world = TestUtil.NewColonyWorld(141UL, 96);
            Assert.IsTrue(world.Crafting.TryGetRecipe("synth_rocket_fuel", out var methane),
                "LOX+LCH4 fuel route missing");
            Assert.IsTrue(world.Crafting.TryGetRecipe("synth_rocket_fuel_h2", out var hydrogen),
                "LOX+LH2 fuel route missing (M5-T2: both selectable)");
            Assert.IsTrue(HasInput(methane, "liquid_methane"));
            Assert.IsTrue(HasInput(hydrogen, "liquid_hydrogen"));
            Assert.AreEqual("rocket_fuel", methane.Outputs[0].ItemId);
            Assert.AreEqual("rocket_fuel", hydrogen.Outputs[0].ItemId);
        }

        private static bool HasInput(RecipeM1 recipe, string itemId)
        {
            foreach (var input in recipe.Inputs)
            {
                if (input.ItemId == itemId)
                {
                    return true;
                }
            }
            return false;
        }

        [Test]
        public void EveryLandableBody_OpensALandingRegion()
        {
            var universe = TestUtil.NewUniverse(142UL, 96);
            int landable = 0;
            foreach (var pair in universe.Bodies.All)
            {
                var body = pair.Value;
                if (!body.Landable)
                {
                    Assert.AreEqual("azurecolossus", body.Id,
                        "the gas giant is the only non-landable body (orbital platform special-case)");
                    continue;
                }
                landable++;
                var world = World.CreateLandingRegion(
                    Fnv1a64.HashString("survey:" + body.Id), 96, body, 2,
                    new List<Ingredient> { new Ingredient { ItemId = ItemIds.Water, Count = 8 } });
                Assert.IsNotNull(world.Buildings.FindFirstOfKind(BuildingKind.CrashPod),
                    body.Id + ": lander pod must deploy");
                Assert.Greater(world.Nodes.All.Count, 0, body.Id + ": landing region must have deposits");
                foreach (var node in world.Nodes.All.Values)
                {
                    Assert.Contains(node.ItemId, body.Resources,
                        body.Id + ": node " + node.ItemId + " is not in the body's resource list");
                }
            }
            Assert.AreEqual(11, landable, "11 of 12 bodies are landable (docs/plan/02)");
        }

        [Test]
        public void PadChecklist_BlocksLaunch_UntilAllFiveGreen()
        {
            var universe = TestUtil.NewUniverse(143UL, 96);
            var world = universe.ActiveWorld;
            world.Tech.UnlockAll();
            int padId = world.Buildings.Place(BuildingDefs.LaunchPadId, world.StartX + 6, world.StartY + 2, 0, out _);
            world.Buildings.TryGet(padId, out var pad);
            pad.Pad = new PadOrder { TargetBodyId = "palewatch", Payload = Universe.ColonistPodPayload, Crew = 1, Active = true };

            var checklist = universe.PadChecklist(pad);
            Assert.IsFalse(checklist.parts, "no parts yet");
            Assert.IsTrue(checklist.target && checklist.landing, "target body is valid");

            // Run past several windows: nothing may launch on an incomplete checklist.
            for (int i = 0; i < GameConstants.TicksPerHour * 13; i++)
            {
                universe.Step();
                foreach (var evt in world.Events)
                {
                    Assert.IsFalse(evt is RocketLaunchedEvent, "launch fired with an incomplete checklist (M5-T3)");
                }
            }

            foreach (var part in Universe.RocketParts)
            {
                pad.Stock.Add(part.ItemId, part.Count);
            }
            pad.Stock.Add("colonist_pod", 1);
            checklist = universe.PadChecklist(pad);
            Assert.IsTrue(checklist.parts && checklist.fuel && checklist.payload, "all five must now be green");

            bool launched = false;
            for (int i = 0; i < GameConstants.TicksPerHour * 7 && !launched; i++)
            {
                universe.Step();
                foreach (var evt in universe.ActiveWorld.Events)
                {
                    if (evt is RocketLaunchedEvent)
                    {
                        launched = true;
                    }
                }
            }
            Assert.IsTrue(launched, "green checklist must launch at the next window");
        }
    }
}
