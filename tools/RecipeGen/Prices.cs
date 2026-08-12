using System;
using System.Collections.Generic;
using System.Linq;

namespace Starsoil.RecipeGen
{
    /// <summary>
    /// Baseline price solver (docs/plan/06 trade anchor, deferred from M3): raw materials
    /// price by tier; every recipe output costs (inputs + energy) × margin ÷ yield, the
    /// cheapest producer wins, relaxed to a fixed point. Prices feed the M6 merchant
    /// quotes (base × scarcity factor).
    /// </summary>
    public static class Prices
    {
        private const double EnergyCostPerKwSecond = 0.002;
        private const double Margin = 1.15;
        private const int MaxRounds = 64;

        private static readonly double[] RawTierPrice = { 1, 2, 4, 8, 16 };

        public static Dictionary<string, double> Solve(Database db)
        {
            var prices = new Dictionary<string, double>();
            foreach (var item in db.Items.Values)
            {
                if (item.Category == "raw")
                {
                    int tier = Math.Max(0, Math.Min(RawTierPrice.Length - 1, item.Tier));
                    prices[item.Id] = RawTierPrice[tier];
                }
            }

            for (int round = 0; round < MaxRounds; round++)
            {
                bool changed = false;
                foreach (var recipe in db.Recipes)
                {
                    double inputCost = 0;
                    bool priced = true;
                    foreach (var input in recipe.Inputs)
                    {
                        if (!prices.TryGetValue(input.ItemId, out double p))
                        {
                            priced = false;
                            break;
                        }
                        inputCost += p * input.Count;
                    }
                    if (!priced)
                    {
                        continue;
                    }
                    double craftCost = (inputCost + recipe.Seconds * recipe.Kw * EnergyCostPerKwSecond) * Margin;
                    int totalOut = recipe.Outputs.Sum(o => o.Count);
                    if (totalOut <= 0)
                    {
                        continue;
                    }
                    double unit = craftCost / totalOut;
                    foreach (var output in recipe.Outputs)
                    {
                        if (!prices.TryGetValue(output.ItemId, out double existing) || unit < existing - 0.0001)
                        {
                            prices[output.ItemId] = unit;
                            changed = true;
                        }
                    }
                }
                if (!changed)
                {
                    break;
                }
            }
            return prices;
        }
    }
}
