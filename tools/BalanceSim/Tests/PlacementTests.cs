using NUnit.Framework;
using Starsoil.Core;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M0-T4 placement rules: bounds, flatness, overlap, removal frees cells.</summary>
    public sealed class PlacementTests
    {
        private const int GridSize = 16;

        private static BuildingSystem NewFlatSystem(out TerrainGrid grid)
        {
            grid = new TerrainGrid(GridSize);
            return new BuildingSystem(grid);
        }

        [Test]
        public void Place_OnFlatGround_Succeeds()
        {
            var sys = NewFlatSystem(out _);
            int id = sys.Place(BuildingDefs.TestBlockId, 4, 4, 0, out var error);
            Assert.AreEqual(PlacementError.None, error);
            Assert.Greater(id, 0);
            Assert.AreEqual(id, sys.GetBuildingAt(5, 5));
        }

        [Test]
        public void Place_Overlapping_ReturnsOccupied()
        {
            var sys = NewFlatSystem(out _);
            sys.Place(BuildingDefs.TestBlockId, 4, 4, 0, out _);
            sys.Place(BuildingDefs.TestBlockId, 5, 5, 0, out var error);
            Assert.AreEqual(PlacementError.Occupied, error);
        }

        [Test]
        public void Place_OnSlope_ReturnsNotFlat()
        {
            var sys = NewFlatSystem(out var grid);
            grid.SetHeight(5, 4, 1);
            sys.Place(BuildingDefs.TestBlockId, 4, 4, 0, out var error);
            Assert.AreEqual(PlacementError.NotFlat, error);
        }

        [Test]
        public void Place_OutOfBounds_ReturnsOutOfBounds()
        {
            var sys = NewFlatSystem(out _);
            sys.Place(BuildingDefs.TestBlockId, GridSize - 1, GridSize - 1, 0, out var error);
            Assert.AreEqual(PlacementError.OutOfBounds, error);
        }

        [Test]
        public void Place_UnknownDef_ReturnsUnknownDef()
        {
            var sys = NewFlatSystem(out _);
            sys.Place("no_such_building", 4, 4, 0, out var error);
            Assert.AreEqual(PlacementError.UnknownDef, error);
        }

        [Test]
        public void Remove_FreesCells_ForReplacement()
        {
            var sys = NewFlatSystem(out _);
            int id = sys.Place(BuildingDefs.TestBlockId, 4, 4, 0, out _);
            Assert.IsTrue(sys.Remove(id));
            Assert.AreEqual(0, sys.GetBuildingAt(4, 4));

            sys.Place(BuildingDefs.TestBlockId, 4, 4, 0, out var error);
            Assert.AreEqual(PlacementError.None, error);
        }

        [Test]
        public void Rotation_SwapsFootprint()
        {
            BuildingSystem.FootprintSize(new BuildingDef("t", 3, 1), 1, out int w, out int h);
            Assert.AreEqual(1, w);
            Assert.AreEqual(3, h);
        }
    }
}
