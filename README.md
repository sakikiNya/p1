# 园区巡检问题标注原型

Unity 放置标记并上报 → Go 保存 JSON → React 查看问题并更新状态。状态更新于 **2026-10-30**，需求依据见 [1.md](1.md)，Windows 负责 Unity/Android，Go 与 React 由服务器端处理。逐项证据见 [验收记录](docs/verification.md)，接手步骤见 [交接说明](docs/handoff.md)。

## 当前交付状态

| 部分 | 当前结果 |
| --- | --- |
| Go / React | 已实现；此前 Linux 测试、浏览器联调、HTTP 部署及重启持久化验证通过 |
| Unity 工程 | 已在本机打开、编译、生成场景与 Prefab；使用 Input System，修复 XR Origin 初始化顺序 |
| ARM64 手机 APK | 最新开发包构建、ABI、内置默认地址和签名检查通过；真机安装及真实 AR 验收待完成 |
| x86_64 模拟 APK | AVD 安装、启动、虚拟放置、空标题验证及此前上报流程通过；最新包的地址切换和重启记忆通过 |
| API 地址选择 | 首次使用选中“默认”并隐藏地址；“自定义”显示可编辑输入框，保存选择及自定义地址 |
| 最终题目验收 | 尚未完成：真实 AR 手机验证、仓库 Unity 副本首次导入、手机及电脑各 3～5 分钟视频 |

用户已报告 Android 真机安装兼容提示，尚未获得具体型号、系统版本和完整错误原因。ARM64 包检查通过不能视为该问题已解决。x86_64 模拟版使用普通虚拟平面，坐标只用于流程测试。

## 环境和目录

| 项目 | 实际版本 / 位置 |
| --- | --- |
| Windows 仓库 / 分支 | `G:\tmp\project1029\p1` / `unity-android-validation` |
| 实际 Unity 开发工程 | `D:\Unity\project\My project`；仓库 `unity/` 为同步的源码和资源副本 |
| Unity / Android Build Support | `2022.3.62f3c1`；Android SDK、NDK、OpenJDK 已安装 |
| AR Foundation / ARCore | `5.2.0` / `5.2.0` |
| XR Management / Input System / URP | `4.5.0` / `1.14.0` / `14.0.12` |
| Android Studio | Rabbit 2 `2026.2.2`，安装于 `D:\AndroidStudio\android-studio` |
| Android Emulator / AVD | `37.2.12`；`Medium_Phone_API_37.0`，Android 17 / API 37、x86_64 |
| Android 构建 | IL2CPP、OpenGLES3、最低 API 24；最新 APK target API 35，Development 构建 |
| 先前 Linux 验证环境 | Go `1.22.2`，Node `24.21.0`，npm `11.19.0` |
| 前端锁文件版本 | React / React DOM `19.3.1`，TypeScript `5.9.3`，Vite `7.3.7` |

```text
backend/       Go API 与现有测试，运行数据默认 data/issues.json
frontend/      React + TypeScript 管理端，依赖由 package-lock.json 锁定
unity/         同步的 C#、场景、Prefab、.meta 和包版本；独立导入仍待验证
deploy/        服务器运维说明、环境和后续 HTTPS/systemd 模板
scripts/       发布构建、服务器管理和浏览器检查脚本
docs/          验收记录、交接说明、演示脚本与历史截图
AI_LOG.md      会话指令与采用结果；服务器地址按要求使用占位符
```

Linux 服务器原部署目录为 `/home/skk/go_project/p1`，与 Windows 本地路径分开使用。仓库 Unity 副本没有经过全新导入验收，不能用本机实际工程成功代替该检查。

## 服务器端：启动 Go 与 React

**工作分工：Windows 只用于 Unity、Android 构建与 AVD 测试；Go 和 React 的依赖、构建、启动、部署与验收均在服务器端处理。Windows 接手不需要安装或运行前后端。** 以下命令在服务器的两个终端执行，先进入服务器项目目录 `/home/skk/go_project/p1`：

```text
cd backend
go run .
```

```text
cd frontend
npm ci
npm run dev
```

使用 Vite 输出的地址，默认 `http://localhost:5173`。开发代理把 `/api` 转发到 `http://127.0.0.1:8080` 并去掉前缀。根目录原有 npm 配置不用于启动管理端。

| 配置 | 默认值 | 作用 |
| --- | --- | --- |
| `LISTEN_ADDR` | `0.0.0.0:8080` | Go 监听地址 |
| `DATA_FILE` | `data/issues.json` | 相对 Go 启动目录；重启时必须保持同一文件 |
| `FRONTEND_DIR` | 空 | 非空时 Go 托管前端 `/`，API 位于 `/api/*` |
| `CORS_ORIGINS` | localhost / 127.0.0.1 的 5173 来源 | 逗号分隔的允许来源 |
| `DEV_API_TARGET` | `http://127.0.0.1:8080` | Vite 开发代理目标 |
| `VITE_API_BASE_URL` | `/api` | 前端构建时的 API 前缀，见 `frontend/.env.example` |

检查命令：后端目录执行 `go test -cover ./...`、`go vet ./...`；支持 race 工具链的环境执行 `go test -race ./...`。前端目录执行 `npm run build`。浏览器回归脚本为 `scripts/browser-smoke.mjs`，需 Chromium/Chrome，使用独立测试数据；历史执行结果及本轮复查结果以验收记录为准。

## Unity 打开与构建

1. 本机继续开发打开 `D:\Unity\project\My project`；从仓库接手时在 Unity Hub 打开 `unity/`。使用上述已验证 Editor，等待包解析与编译；不要混用实际工程和同步副本的 Library。
2. 执行 **Inspection → Setup Project**。生成器已改为手动调用，不会在首次打开时自动运行；已有场景会保留。打开 `Assets/Scenes/Inspection.unity`。
3. 输入模式使用 **Input System Package (New)**；Standalone 使用 XR Simulation，Android 使用 ARCore。Editor 已验证场景启动与表单，Editor 内 AR 平面命中及完整上报未记为通过。
4. 手机构建使用 **Inspection → Build Android Development APK**，输出 `Builds/inspection-dev.apk`，包名 `cn.inspection.prototype`，仅 ARM64。
5. 电脑 AVD 流程测试使用 **Inspection → Build x86_64 Simulation APK**，输出 `Builds/inspection-x86-simulation.apk`，独立包名 `cn.inspection.prototype.simulation`。该构建关闭 XR 自动初始化，用带网格虚拟平面测试业务流程，界面显示模拟标识。
6. Android Build Support 的 SDK/NDK/OpenJDK 必须完整；Gradle 模板包含阿里云镜像与官方源回退。此前依赖下载曾遇到 TLS 中断，最新两种 APK 构建成功；再次失败应查看真实日志再处理。

当前 ARCore Unity 桥接库仅有 ARM ABI。纯 x86_64 AR 诊断包启动缺少 `libUnityARCore.so`；ARM 转译尝试也未建立 AR 会话。独立模拟版是当前已通过的 AVD 开发路径。

## API 地址与安装包

- **默认**：从 `Assets/Resources/api-config.txt` 读取，界面仅显示选项名称，隐藏地址输入框。最新本机交付包已包含用户确认的服务器 `/api` 地址。
- **自定义**：显示完整地址输入框；选项与地址保存到 PlayerPrefs，旧版 `apiBaseUrl` 保留为自定义值。切回默认不会丢失自定义值。
- 仓库配置保留 `http://127.0.0.1:8080`，真实服务器不写入 Git 文档或默认配置。重新构建前，在实际工程本机配置中填写完整 `http://${HOST}:${PORT}/api`；占位符不会自动展开。
- 应用只在点击“提交问题”时发送请求。开发包允许 HTTP，非 Development 发布需重新核对 HTTPS/明文策略。手机中的 localhost 指手机自身。

最新产物在本次 Codex 工作区的 `outputs/`，完整绝对路径及 SHA-256 见 [交接说明](docs/handoff.md)：

| 文件 | 用途与已验证范围 |
| --- | --- |
| `inspection-phone-arm64-api-options.apk` | Android ARM64 真机开发包；构建和签名通过，待真机安装/AR 验收 |
| `inspection-x86-simulation-api-options.apk` | x86_64 AVD 虚拟流程测试；安装、启动、地址切换与重启记忆通过 |

旧 `inspection-x86-diagnostic.apk` 是已知 AR 初始化失败的诊断产物，交接时使用上表最新包。手机包沿用此前手机交付包的调试签名；模拟包维持原独立包名和签名。

## API 协议

服务器上裸 Go 开发服务使用下列路径；Go 静态托管或服务器同域部署时加 `/api` 前缀。

| 方法 | 路径 | 输入 / 成功结果 |
| --- | --- | --- |
| GET | `/health` | `200 {"status":"ok"}` |
| GET | `/issues` | `200` 问题数组，无数据为 `[]` |
| POST | `/issues` | `title/description/priority/position` → `201` 完整问题 |
| PATCH | `/issues/{id}/status` | `{"status":"resolved"}` → `200` 更新后的问题 |

```json
{"title":"入口墙面破损","description":"左侧裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}
```

完整问题包含 `id/title/description/priority/status/position/createdAt/updatedAt`。后端生成 ID、默认 `open` 和 UTC RFC3339Nano 时间。标题去空白后必填，描述可选；优先级 `low/medium/high`，状态 `open/in_progress/resolved`；坐标 x/y/z 必须全为数值，零有效。创建不接受额外字段，请求上限 64 KiB。参数错误 `400`、不存在 `404`、保存错误 `500`，错误体形如 `{"error":{"code":"INVALID_ARGUMENT","message":"title 不能为空"}}`。

## 部署、限制与下一步

服务器历史部署为 Go 同端口提供管理端和 `/api/*` 的临时 HTTP 服务，用户确认过公网可达；本轮文档交接不代表服务器当前在线检查。运维见 [deploy/README.md](deploy/README.md)，使用忽略文件 `deploy/.env` 与 `scripts/server.py`。尚无域名、HTTPS 或开机自启验收。

JSON 持久化按单 Go 进程设计，先写同目录临时文件、同步与替换，成功后再更新内存；不能多实例共享同一文件。数据损坏拒绝启动。创建没有幂等键或自动重试，响应丢失后先查管理端再手动重试。AR 世界坐标仅在当前会话有意义，无跨设备定位及历史标记恢复。未实现登录权限。

后续按 [plan.md](plan.md) 完成：确认真机型号和系统兼容提示 → 安装 ARM64 包 → 相机授权、真实平面与触摸验收 → 同一 ID 的手机/React/Go 重启闭环 → 仓库副本独立导入 → 两段视频。模拟器仍有两个 missing script 警告与图形兼容日志，详见验收记录。当前成果是可交接的开发阶段版本，尚未满足题目全部真机及视频要求。
