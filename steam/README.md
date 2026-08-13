# Steam 构建与上传(CI 门 3)

对应 `docs/plan/10-steam-release.md` 构建与上传管线一节。门 3 是手动触发的 GitHub Actions workflow(`.github/workflows/release.yml`):Unity 批处理构建 win64 + linux64 → 版本号 `major.minor.patch+sha` → steamcmd 按本目录 `app_build.vdf` 上传到指定分支。

## 分支策略

`internal`(每次门 3 默认)→ `beta`(播测轮)→ `default`(发售,Steamworks 后台手动 setlive,workflow 不直接推 default)。

## 需要的 Secrets(GitHub 仓库 Settings → Secrets → Actions)

| Secret | 内容 | 缺失时行为 |
| --- | --- | --- |
| `UNITY_LICENSE`(或 `UNITY_EMAIL`+`UNITY_PASSWORD`) | Unity 许可证 | 构建 job 显式失败,日志标注"阻塞待用户" |
| `STEAM_APP_ID` | 正式版 AppID | 上传 job 显式失败,构建产物仍留存为 artifact |
| `STEAM_DEMO_APP_ID` | Demo AppID(可选,Demo 管线复用) | 仅 Demo 上传跳过 |
| `STEAM_BUILD_USER` | 合作方构建账号用户名 | 上传 job 显式失败 |
| `STEAM_CONFIG_VDF` | 该账号已完成 Steam Guard 的 `config.vdf` 内容(base64) | 上传 job 显式失败 |

登录态制备:本地 `steamcmd +login <构建账号>` 完成一次 Steam Guard 验证后,把 `~/Steam/config/config.vdf` base64 编码存入 `STEAM_CONFIG_VDF`。

## 文件

- `app_build.vdf`:主构建描述。`__APP_ID__`、`__BUILD_DESC__`、`__LIVE_BRANCH__` 由 workflow 注入。
- `depot_windows.vdf` / `depot_linux.vdf`:两个 Depot 的文件映射,Depot ID 约定为 AppID+1(Windows)、AppID+2(Linux),与 Steamworks 后台配置保持一致后再改动。
- Demo 复用同一组模板,注入 `STEAM_DEMO_APP_ID`。

## 本地演练(不上传)

VM/本地有 Unity 许可证时:`scripts/unity_headless.sh build-win && scripts/unity_headless.sh build-linux`,产物在 `Builds/Win64`、`Builds/Linux64`,与 depot vdf 的 ContentRoot 一致。
