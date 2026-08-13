# Unity 激活与运行时验证报告

日期:2026-08-13。环境:云端 VM(headless Linux),Unity Editor 6000.3.21f1(`/opt/unity/Editor`)。

## 激活

用户提供 Unity 账号凭据后,通过 `scripts/unity_headless.sh activate`(`-batchmode -username -password`,Personal 无 serial)在本机完成激活。验证探针从此前的 `No valid Unity Editor license found` 变为 `[Licensing::Client] Successfully resolved entitlement details`,无任何许可证错误。

安全注意:该凭据曾出现在对话明文中,验证完成后应修改密码,新密码只放入 Cursor / GitHub Secrets(`UNITY_EMAIL` / `UNITY_PASSWORD`)。另:本仓库当前为公开仓库,Cursor Cloud 对公开仓库默认不注入 secrets,转私有后注入恢复。

## EditMode 测试(三轮迭代到全绿)

| 轮次 | 结果 | 失败项与修复 |
| --- | --- | --- |
| 1 | 10 测试,8 过 2 败 | ① `Localization_ResolvesUiKeys`:EditMode 下无人初始化 L10n,`Tr` 回显 key → `L10n.Tr` 增加懒加载(首次调用时 `TryLoadDefault`);② `SetupProjectTests`:测试断言的 PanelSettings 路径写错(`Assets/_Game/Resources/` vs 实际 `Assets/Resources/`)→ 修正测试常量 |
| 2 | 10 测试,9 过 1 败 | `SetupProject.Run` 第二次运行重建 URP 管线资产,旧资产的 renderer 引用悬空,Unity 打出 `Default Renderer is missing` 错误日志(Test Runner 判失败)→ `Run` 改为真幂等:管线资产与 PanelSettings 已存在则复用不重建 |
| 3 | **10 测试,10 过 0 败** | — |

结果文件:`/tmp/unity_editmode_results.xml`(`total="10" passed="10" failed="0"`)。

## Linux 玩家构建(三轮迭代到成功)

| 轮次 | 结果 | 失败点与修复 |
| --- | --- | --- |
| 1 | `Verify Build setup` 失败 | `HeadlessBuild` 场景路径 `Assets/_Game/Scenes/Main.unity` 与 `SetupProject` 实际产出 `Assets/_Game/Bootstrap/Main.unity` 不一致 → 修正常量,并在场景缺失时自动先跑 `SetupProject.Run()` |
| 2 | Player 域编译失败 | `HeadlessBuild`(`Game.EditorTools`)与 `SetupProject`(`Starsoil.EditorTools`)命名空间不一致,`CS0103` → 统一为 `Starsoil.EditorTools`,同步 `scripts/unity_headless.sh` 的 `-executeMethod` 全名 |
| 3 | **Build Finished, Result: Success**,产物 92MB(`Builds/Linux64/Starsoil.x86_64`) | — |

## 玩家构建运行时冒烟(两轮)

- 第 1 轮:启动即 `ArgumentNullException` at `GameBootstrap.Init`。根因有二,均为"编辑器可用、构建版不可用"类:
  1. `Shader.Find("Universal Render Pipeline/Lit")` 在玩家构建返回 null(shader 未被任何资产引用,不进包),两级 fallback(`Standard`)在 URP 构建同样缺席 → 新增 `MaterialLib`:`SetupProject` 生成引用 URP Lit/Unlit 的基底材质到 `Assets/Resources/`(随包携带 shader),运行时从基底材质实例化,`Shader.Find` 仅作编辑器/测试兜底;
  2. 数据文件(`data/`、`GeneratedData/`)只存在于仓库,玩家构建的 `Application.dataPath/../data` 落空 → `HeadlessBuild` 构建成功后把两个数据目录拷贝到构建目录旁,与 `DataFiles` 的解析约定一致。
- 第 2 轮:**冒烟通过**。决定性日志行:`[GameBootstrap] World ready: seed 42, region 192, colonists 4`。进程在 `-batchmode -nographics` 下持续运行至 25 秒超时截杀,无异常输出(FMOD 输出设备错误为无声卡环境的预期噪音)。

## 结论与遗留

- 这台 VM 现在具备完整的 Unity 验证链:EditMode 全绿、Linux 玩家构建成功、构建产物可启动并完成世界生成。
- 遗留:
  - Windows 构建支持模块(windows-mono)需从 Mac pkg 解包安装(进行中/见后续提交);
  - CI 门 2 需要 GitHub Actions secrets(`UNITY_LICENSE` 或 `UNITY_EMAIL`+`UNITY_PASSWORD`)才能在 CI 侧复现同样的验证;
  - 图形界面下的视觉验收(HUD 布局、面板交互)仍需有显示器的环境或后续 xvfb + 截图方案。
