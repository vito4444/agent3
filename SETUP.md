# 本地开发环境搭建(M0)

本仓库是《星壤》(Starsoil) 的 Unity 工程 + 纯 C# 工具链。云端脚手架不含 Unity 编辑器生成物(`.meta`、`ProjectSettings` 全量、URP 资产、场景),首次在本地打开时按下述步骤补齐并提交。

## 前置

- Unity 6.3 LTS,推荐 6000.3.21f1(与 `ProjectSettings/ProjectVersion.txt` 一致;同为 6000.3.x 的更新补丁也可,Unity 会自动迁移)。
- .NET SDK 8.0(跑 `tools/` 下的测试与内容工具;Unity 之外的 CI 门 1 只依赖它)。

## 首次打开 Unity 工程

1. Unity Hub 添加本仓库根目录为工程,用 6000.3.x 打开。首次导入会解析 `Packages/manifest.json` 并生成 `.meta` 文件与 `ProjectSettings` 其余文件。
   - 若个别包版本解析失败(镜像差异),按 Package Manager 提示切换到它推荐的同分支版本,并提交更新后的 `manifest.json`。
2. 菜单执行 `Starsoil → Setup Project (Run Once)`:自动创建 URP 管线资产并指派、切换 Linear 色彩空间、设置产品名、创建空场景 `Assets/_Game/Bootstrap/Main.unity` 并加入 Build Settings。
3. 打开 `Main.unity` 进入 Play:`GameBootstrap` 会在运行时自建相机、灯光、192×192 地形与交互控制器(M0 无需任何场景内容)。
4. 首次打开后,把 Unity 生成的 `.meta` 文件与 `ProjectSettings/`、`Assets/Settings/` 新增文件一并提交。

## M1 运行时操作(冒烟自查)

- 相机:WASD / 鼠标中键拖拽平移;Q/E 45° 旋转;右键拖拽自由旋转;滚轮缩放(12–900m,300m 处建筑切换体块 LOD 并打印日志)。
- 建造:B 进入/退出建造模式;数字键 1–6 选择建筑(篝火炉/手工台/睡舱/小仓库/手摇发电机/砂石路);R 旋转;左键放置蓝图(殖民者会搬料并建造);Esc 退出;Delete 取消蓝图或拆除建筑(返还 50% 材料)。
- 圈选与交互(非建造模式):左键点资源点 = 开始/停止采集;点任意配方站(手工台/篝火炉/机器)= 打开合成订单面板(+1 或维持库存);点手摇发电机 = 安排/撤下摇柄手(顶栏电力表随之变化)。
- M2/M3 面板:F4 = 配方浏览器(搜索、按站过滤、上下游一键展开);T = 科技树(选定研究目标后,把数据核搬到研究台由研究员消耗);J = 职业配额;建造模式下 [ ] 翻页选择已解锁建筑(数字键选前 9 项)。锁定建筑不出现在菜单,蓝图命令对锁定项一律拒绝。
- 速度:空格暂停;F1/F2/F3 = 1×/2×/4×;危急警报默认自动暂停(设置 `settings.json` 的 `AutoPauseOnCritical` 可关)。
- 存读档:F5 存宇宙档到 `persistentDataPath/save_universe.json.gz`(多区域,路径见日志);F9 读档。F10 开发者叠层。M = 星图(区域切换、在途航班、天体资源)。发射:建发射台 → 部件与载荷入库 → 星图下单(SetPadOrder)→ 物流上台 → 6 时窗口自动点火。
- 首个存在感目标:按教学提示走完第一幕(采冰 → 融水 → 采生物质 → 做口粮 → 建睡舱 → 熬过第一夜)。

## 已知的 M1 阶段限制

- HUD 需要 `Starsoil → Setup Project (Run Once)` 生成的 `Assets/Resources/StarsoilPanelSettings.asset`;缺失时仅有 F10 开发者叠层。
- 本地化与配方表从仓库 `data/` 目录直读(编辑器与开发构建);M3 数据管线接管后改为打包资产。
- PanelSettings 未指定主题样式表时 Unity 会打一条警告,HUD 全部使用内联样式,不影响显示。

## 输入系统说明(阶段性接口)

M0 交互用 UnityEngine.Input(旧输入 API)实现,`com.unity.inputsystem` 包已安装但未启用动作表。里程碑 M8 的全键位重绑上线时整体迁移到 Input System 动作表(见 `docs/plan/05-zoom-and-presentation.md` 输入映射)。若把 Player Settings 的 Active Input Handling 切为 "Input System Package (New)" 会导致 M0 交互失效,请保持默认或 "Both"。

## 纯 C# 工具链(无需 Unity)

```bash
# 模拟核心测试(确定性、放置规则、存档往返、Rng)
dotnet test tools/BalanceSim

# 内容生成与校验(M0 阶段 data/ 为空表头,输出 0 recipes 0 errors)
dotnet run --project tools/RecipeGen -- --generate
dotnet run --project tools/RecipeGen -- --validate

# Core 约束检查(禁 UnityEngine 引用、禁系统随机数、数字字面量白名单)
bash scripts/check_core_constraints.sh
```

## CI

- 门 1(`.github/workflows/ci.yml` 的 `gate1`):上述三条命令,无需任何 Secret,每个 PR 必跑。
- 门 2(`gate2-*`):Unity EditMode 测试与 Win/Linux 构建,需要仓库 Secret `UNITY_LICENSE`(Unity 个人版可用 game-ci 文档的激活流程生成 `.ulf` 内容)。Secret 缺失时门 2 自动跳过并在 job 名旁标注,不阻断门 1。
