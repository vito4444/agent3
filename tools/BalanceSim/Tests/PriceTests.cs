using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>Baseline prices (M3-T7 remainder, delivered with M5): every item priced,
    /// crafting adds value, tier gradient holds.</summary>
    public sealed class PriceTests
    {
        private static Dictionary<string, double> Load()
        {
            string path = Path.Combine(TestUtil.FindDataDir(), "..", "GeneratedData", "prices.json");
            var root = JObject.Parse(File.ReadAllText(path));
            var prices = new Dictionary<string, double>();
            foreach (var pair in (JObject)root["prices"])
            {
                prices[pair.Key] = pair.Value.Value<double>();
            }
            return prices;
        }

        [Test]
        public void EveryCatalogItem_HasAPrice()
        {
            var prices = Load();
            string itemsPath = Path.Combine(TestUtil.FindDataDir(), "..", "GeneratedData", "items.json");
            var items = (JArray)JObject.Parse(File.ReadAllText(itemsPath))["items"];
            foreach (var token in items)
            {
                string id = token.Value<string>("Id");
                Assert.IsTrue(prices.ContainsKey(id), "unpriced item: " + id);
                Assert.Greater(prices[id], 0, "non-positive price: " + id);
            }
        }

        [Test]
        public void Crafting_AddsValue_AndTiersGradeUp()
        {
            var prices = Load();
            // A craft's output value exceeds its main input value (margin + energy).
            Assert.Greater(prices["iron_ingot"], prices["iron_ore"] * 2, "smelting must add value over 2 ore");
            Assert.Greater(prices["iron_plate"] * 2, prices["iron_ingot"], "rolling must add value per ingot");
            Assert.Greater(prices["steel_plate"], prices["iron_plate"], "alloy plate above plain plate");
            // Tier gradient spot checks.
            Assert.Greater(prices["rocket_fuel"], prices["water"], "T3 fuel above T0 water");
            Assert.Greater(prices["quantum_circuit"], prices["basic_circuit"], "T4 above T1 electronics");
        }
    }
}
