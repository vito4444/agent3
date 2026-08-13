# Unity 许可证激活(headless 环境 / CI 门 2)

云端 VM 已安装 Unity Editor **6000.3.21f1**(`/opt/unity/Editor/Unity`,changeset `c02631ffc030`),
批处理模式探针确认唯一拦截点是许可证:`No valid Unity Editor license found`。
提供许可证后,EditMode 测试与 Linux/Windows 构建即可在无头环境与 CI 门 2 直接运行。

## 方式 A:账号凭据(推荐,机器无关)

在 Cursor Dashboard → Cloud Agents → Secrets 配置:

| Secret | 内容 |
| --- | --- |
| `UNITY_EMAIL` | Unity 账号邮箱 |
| `UNITY_PASSWORD` | Unity 账号密码 |
| `UNITY_SERIAL` | 序列号(Personal 版可留空,Pro/Plus 填 `SC-` 开头序列号) |

激活命令(agent/CI 自动执行):

```bash
/opt/unity/Editor/Unity -batchmode -nographics -quit \
  -username "$UNITY_EMAIL" -password "$UNITY_PASSWORD" \
  ${UNITY_SERIAL:+-serial "$UNITY_SERIAL"} -logFile -
```

账号凭据激活不绑定机器指纹,VM 重建后依然有效,是云环境最稳的路径。

## 方式 B:手动激活文件(.alf → .ulf)

本目录的 `Unity_v6000.3.21f1.alf` 是当前 VM 生成的激活请求文件。

1. 下载 `Unity_v6000.3.21f1.alf`;
2. 打开 <https://license.unity3d.com/manual>,登录 Unity 账号,上传 .alf,选择 Personal(或输入序列号),下载得到 `Unity_v6000.3.21f1.ulf`;
3. 把 **.ulf 文件的完整内容**配成 Secret `UNITY_LICENSE`(GitHub 仓库 Secrets 供 CI 门 2 使用;Cursor Cloud Secrets 供云端 agent 使用);
4. 激活命令:

```bash
/opt/unity/Editor/Unity -batchmode -nographics -quit \
  -manualLicenseFile /path/to/Unity_v6000.3.21f1.ulf -logFile -
```

注意:.ulf 含机器绑定字段。同一快照克隆出的 VM 通常可复用;若 VM 指纹变化导致失效,
需重新生成 .alf(`-createManualActivationFile`)再走一遍,或改用方式 A。

## 激活后解锁的验证项

- `scripts/unity_headless.sh test`:EditMode 测试(Assets/_Game 全部脚本以 Unity 真编译器编译 + 场景冒烟);
- `scripts/unity_headless.sh build-linux` / `build-win`:玩家构建产物;
- CI 门 2(`.github/workflows/ci.yml`)自动从跳过转为执行。

未激活时的替代验证:CI 门 1.5(`tools/UnityCompileCheck`)用 stub 程序集对全部
Unity 侧脚本做类型级编译检查,已常态运行。
