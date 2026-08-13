using NUnit.Framework;
using Starsoil.Core;
using Starsoil.Data;

namespace Starsoil.Tests
{
    /// <summary>
    /// EditMode data-pipeline checks (CI gate 2): the generated catalog, tech tree,
    /// bodies, faction params and localization must load from the repo layout exactly
    /// as the runtime bootstrap does. Counts assert the plan-level content contract
    /// (docs/plan/04: ≥330 recipes, 90 common + 30 branch tech nodes).
    /// </summary>
    public sealed class DataPipelineTests
    {
        [Test]
        public void CatalogContent_LoadsRecipes_TechTree_Bodies_Factions()
        {
            var universe = Universe.NewGame(11UL, 96);
            Assert.IsTrue(CatalogContent.ApplyTo(universe), "repo 数据目录必须可用 (GeneratedData + data)");

            Assert.GreaterOrEqual(universe.ActiveWorld.Crafting.Recipes.Count, 330,
                "配方目录 ≥330 (docs/plan/04 M7 门槛)");
            int common = 0, branch = 0;
            foreach (var node in universe.ActiveWorld.Tech.Nodes.Values)
            {
                if (node.Id.StartsWith("branch_"))
                {
                    branch++;
                }
                else
                {
                    common++;
                }
            }
            Assert.AreEqual(90, common, "公共科技 90 节点 (docs/plan/04 字面)");
            Assert.AreEqual(30, branch, "分支科技 30 节点 (6 分支 × 5)");
            Assert.AreEqual(3, universe.FactionsSandbox.Factions.Count, "三个势力从 CSV 载入");
        }

        [Test]
        public void ItemCatalog_ResolvesDisplayNames()
        {
            Assert.IsTrue(ItemCatalog.TryLoadDefault(), "物品目录从 GeneratedData/items.json 载入");
            string name = ItemCatalog.NameOf("iron_ingot");
            Assert.IsFalse(string.IsNullOrEmpty(name), "iron_ingot 有显示名");
            Assert.AreNotEqual("iron_ingot", name, "显示名不是裸 id");
        }

        [Test]
        public void Localization_ResolvesUiKeys()
        {
            string tradeTitle = L10n.Tr("ui_trade_panel");
            Assert.IsFalse(string.IsNullOrEmpty(tradeTitle));
            Assert.AreNotEqual("ui_trade_panel", tradeTitle, "本地化键有译文而非回显 key");
        }
    }
}
