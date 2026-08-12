using NUnit.Framework;
using Starsoil.BalanceSim.Scenarios;

namespace Starsoil.BalanceSim.Tests
{
    /// <summary>M3-T7: the scripted progression from T0 to "launch pad unlocked and a
    /// first rocket's parts built" fits in ≤40 game days; bottleneck materials reported.</summary>
    public sealed class E3ProgressionTests
    {
        [Test]
        public void E3_LaunchPadWithin40Days_BottlenecksReported()
        {
            var result = E3Progression.Run();
            var lines = new System.Text.StringBuilder();
            lines.AppendLine("E3 progression estimate: total " + result.TotalDays.ToString("F1") + " days" +
                             " (gather " + result.GatherDays.ToString("F1") +
                             ", process " + result.ProcessDays.ToString("F1") +
                             ", research " + result.ResearchDays.ToString("F1") + ")");
            lines.AppendLine("Top bottleneck materials:");
            foreach (var (item, days) in result.Bottlenecks)
            {
                lines.AppendLine("  " + item + ": " + days.ToString("F2") + " miner-days");
            }
            TestContext.Out.WriteLine(lines.ToString());

            Assert.Greater(result.TotalDays, 1.0, "estimate suspiciously fast; the graph walk likely broke");
            Assert.LessOrEqual(result.TotalDays, 40.0,
                "progression exceeds the 40-day budget (M3-T7).\n" + lines);
            Assert.AreEqual(5, result.Bottlenecks.Count, "bottleneck report must list top 5 materials");
        }
    }
}
