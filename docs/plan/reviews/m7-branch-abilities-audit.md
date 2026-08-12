# 分支能力与特种单位实装审计(M6/M7 缺口收敛)

本文记录六条封锁分支中"能力/单位/建筑"类节点在当前模拟架构下的实装口径,以及 B/C 族配方补格的内容决策。计划原文见 `docs/plan/07-factions-combat-occupation.md` 六条封锁分支一节;配方计数目标见 `docs/plan/04-recipes-items-tech.md`。

## 一、星际物流网(商盟)能力节点

| 节点 | 原文 | 实装 | 验证 |
| --- | --- | --- | --- |
| 自动报价单 `branch_orbital_logistics_2` | 与商盟残部以外的中立保税区自动交易 | 解锁后报价板不再要求商盟"非禁运且态度 ≥0":中立保税区顶上,板子照常按 12 小时刷新、照常成交;稀有条目仍挂商盟态度(商盟专属优待,残部时无)。中立区成交不改商盟态度、不进商盟金库。 | `DiplomacyExtrasTests.AutoQuotes_KeepBoardAlive_WhenMerchantIsRemnant` |
| 行情雷达 `branch_orbital_logistics_3` | 贸易面板显示未来 3 张报价单预告 | 报价种子改为对齐 12 小时刷新网格(`hash(seed:quotes:gridHour)`),未来板面完全可前瞻。`FactionSystem.PeekUpcomingQuotes(universe, 3)` 生成无副作用预告(Id=0),未解锁时返回空。贸易面板(`TradePanelController`,G 键)展示三张预告。 | `DiplomacyExtrasTests.MarketRadar_PreviewsNextThreeBoards_Deterministically`,含变异验证(实刷种子退化为非网格时测试红) |
| 保税仓 `branch_orbital_logistics_4` | 出售价 +10% | 先前已实装(`BondedSaleValue` 的 `BondedBonus`),本轮无改动。 | `LogisticsM5Tests` 既有覆盖 |
| 航线保险 `branch_orbital_logistics_5` | 货损全额星币补偿 | 先前已实装,本轮无改动。 | `DiplomacyExtrasTests.RouteInsurance_RefundsCargoLoss` |

## 二、集群战术(赤旗)单位与建筑

进攻方向的战斗解算目前是沙盘数值模型(M7 验收口径:`attack = bots × 2.2 vs EffectiveDefense`),防守方向是活跃区域的实体战斗层(`BattleSystem`)。四个节点按所属方向分别映射:

| 节点 | 原文 | 实装 | 验证 |
| --- | --- | --- | --- |
| 自爆蛛 `branch_swarm_tactics_2` | 单位配方 | 沙盘进攻参数 `breacherBots`:一次性爆破单位,单只攻击力 6.6(战斗蛛 2.2 的 3 倍),用后即耗。 | `BranchUnitsTests.BreacherBots_OneShotCharges_TipTheAssault`(98 蛛 215.6 < 216 败;+2 自爆蛛 228.8 > 216 胜) |
| 干扰无人机 `branch_swarm_tactics_3` | 使敌方炮塔索敌 -50% | 沙盘进攻参数 `jammerDrones`:防御分中炮塔占比按 50% 计,干扰使该部分效果减半,净防御 ×0.75。 | `BranchUnitsTests.JammerDrones_HalveTurretTargeting_QuarterOffDefense`(75 蛛 165:无干扰败于 216,有干扰胜于 162) |
| 前线装配巢 `branch_swarm_tactics_4` | 敌方区域边缘可展开的战地工坊 | 沙盘映射:携带装配巢的进攻失败后,敌方防御不回复 10%(战地工坊维持围攻压力);建筑配方 `b_forward_nest` 与 `BuildingDefs.ForwardNest` 入库。敌区实体建造玩法依赖进攻实体化,超出当前沙盘口径,如需实体化另立任务。 | `BranchUnitsTests.ForwardNest_FailedAssault_KeepsSiegePressure` |
| 震荡炮 `branch_swarm_tactics_5` | 范围击退炮塔 | 防守实体层实装:通电时每轮对半径 6 内全部敌单位击退 3 格并作废其路径,随后 5 轮(约 5 秒)冷却。冷却是秒级瞬态,刻意不入存档。建筑 `BuildingDefs.ShockCannon`(2×2、20kW,成本与 `make_b_shock_cannon` 一致)。 | `BranchUnitsTests.ShockCannon_KnocksHostilesBack_ThreeTiles`,含变异验证(击退距离改 0 时测试红) |

蜂群协议(编组 12→24)先前已实装(`BattleTests.GroupCap_Twelve_ThenTwentyFour_WithSwarmProtocol`)。

## 三、B/C 族配方补格(±15% 带回归)

M3 审计口径:各族条数相对 `docs/plan/04` 目标表偏差 ≤15%。本轮前 B 族 57/78(-27%)、C 族 41/50(-18%),均越带。

补格遵循掩码治理规则(新组合必须带下游消费者,校验 2/3 死端零容忍),11 个新 B 格及其消费端:

| 新格 | 语义 | 消费端(新增输入) |
| --- | --- | --- |
| 铁网 T1 | 分选筛网 | `b_recycler` +iron_mesh:2 |
| 铜网 T2 | 电磁屏蔽网 | `comms_array_module` +copper_mesh:1 |
| 铝管 T2 | 轻质输送管 | `fuel_tank_module` +aluminum_pipe:2 |
| 电工钢板 T2 | 电机定子叠片 | `motor` +electrical_steel_plate:1 |
| 钛丝 T3 | 驱动腱 | `combat_frame` +titanium_wire:2 |
| 钛网 T3 | 疫苗过滤网 | `spore_vaccine` +titanium_mesh:1 |
| 镍丝 T3 | 电热丝 | `b_isotope_heater` +nickel_wire:2 |
| 铂丝 T3 | 精密绕组 | `shield_emitter` +platinum_wire:1 |
| 青铜板 T3 | 耐蚀衬板 | `b_refinery` +bronze_plate:1 |
| 因瓦网 T3 | 低温筛网 | `b_cryo_liquefier` +invar_mesh:1 |
| 曜金钢齿轮 T4 | 炮塔回转齿 | `b_shock_cannon` +aurite_steel_gear:1(`BuildingDefs.ShockCannon` 同步) |

C 族补 2 条:`polymerize_adhesive`(聚合物 + 碳粉 → 粘合剂,消费端 `hull_panel`)、`synth_coolant`(水 + 盐 → 冷却液,消费端 `radiator_fin`)。粘合剂初版用树脂,因树脂(菌木蒸馏)在起始星不可达、连坐火箭链可达性校验而改为碳基;冷却液初版含氨,同因改为盐水基。因瓦网初版挂深冷泵,因因瓦在起始星不可达且深冷泵在火箭链上而改挂深冷液化塔。

新配方科技节点分配(公共节点总数维持计划字面 90):铁网入 `forming_basic`;铜网、铝管入 `powered_processing`;电工钢板、青铜板入 `metallurgy_2`;钛丝、镍丝、铂丝、钛网、因瓦网入 `machining_tech`;曜金钢齿轮留 `forming_exotic`;两条化反入 `synthesis_2`。所有节点负载 ≤14。

结果:B 68/78(-13%)、C 43/50(-14%),回到带内;总配方 343(≥330),RecipeGen 校验 0 错误。

## 四、遗留

- 贸易面板、震荡炮击退、预告板的视觉呈现依赖 Unity 运行时验证(许可证激活后经 `scripts/unity_headless.sh test` 与门 2 覆盖编译与冒烟)。
- 进攻实体化(在敌方区域地图上的登陆战)不在当前沙盘口径内;若立项,前线装配巢应改为实体部署物,自爆蛛/干扰无人机改为实体单位行为。
