# 园区巡检问题标注原型

Unity AR 放置标记并上报 → Go 保存 JSON → React 查看问题并更新状态。所有项目文件位于 `/home/skk/go_project/p1`。

## 当前完成情况

- Go API、校验、CORS、并发保护、JSON 临时文件替换持久化及关键测试已实现并通过检查。
- React 列表、优先级/状态/坐标/时间展示、状态保存、空列表、失败提示和重试已实现；构建及实际浏览器联调通过。
- Unity C# 源码、依赖版本、AR 场景/Prefab/设置生成器已提供。**当前机器没有 Unity Editor、adb 或 ARCore 真机，尚未完成 Unity 导入、编译、XR Simulation 运行、APK 构建和真机验收。** 场景和 Prefab 在 Unity 首次打开时生成，需将生成的资源及 `.meta` 一起纳入最终交付。
- 当前服务器部署配置已改为 `0.0.0.0:${PORT}`，Go 同时托管页面与 `/api`，运维见 `deploy/README.md`；用户已验证公网可访问，详细证据见 `docs/verification.md`。
- 两段 3～5 分钟演示视频尚未录制。见 [验收记录](docs/verification.md) 与 [演示脚本](docs/demo.md)。

## 版本

| 部分 | 版本及验证情况 |
| --- | --- |
| Go | 实际运行 1.22.2，使用标准库，无第三方 Go 依赖 |
| Node.js / npm | 实际运行 v24.21.0 / 11.19.0 |
| React / React DOM | 锁文件解析为 19.3.1 |
| TypeScript / Vite | 实际构建使用 5.9.3 / 7.3.7 |
| Unity | 目标 2022.3.62f1 LTS，尚未实际安装/打开 |
| AR Foundation / ARCore XR Plug-in | 固定 5.1.6 / 5.1.6 |
| XR Management / Input System | 固定 4.4.0 / 1.7.0 |
| Android Studio | 用户提供 Rabbit 2 \| 2026.2.2；本机未验证其 SDK/AVD |

`frontend/package-lock.json` 锁定实际前端依赖，使用 `npm ci` 安装。根目录原有 npm 配置保留，不用于启动管理端。Unity 会在首次解析包后生成 `Packages/packages-lock.json`，需保留。

## 目录

```text
backend/       Go 源码、测试；默认运行数据 backend/data/issues.json
frontend/      React + TypeScript 管理端
unity/         Unity 项目及场景/Prefab 生成器
scripts/       构建与浏览器检查脚本
deploy/        环境模板、Caddy、systemd、部署说明
docs/          验收证据与演示脚本
AI_LOG.md      当前可见会话的实际 Prompt 原文
```

## 当前服务器访问

下文 `${HOST}` 表示服务器的公网 IP 或域名，`${PORT}` 表示端口（以部署配置为准）。使用命令前先设置：

```bash
export HOST='你的服务器IP或域名'
export PORT='你的部署端口'
```

Markdown 中的占位符需自行替换；Unity 地址输入框和 `api-config.txt` 应填写替换后的完整地址，不会展开环境变量。

管理端：`http://${HOST}:${PORT}/`；Unity API：`http://${HOST}:${PORT}/api`。运行配置在 `deploy/.env`，使用 `python3 scripts/server.py start|stop|restart|status` 管理（每次选择一个动作）。这是临时 HTTP 后台进程，未配置开机自启。

## 本地开发：启动 Go 与 React

终端一：

```bash
cd /home/skk/go_project/p1/backend
go run .
```

终端二：

```bash
cd /home/skk/go_project/p1/frontend
npm ci
npm run dev
```

打开终端中显示的地址（默认 `http://localhost:5173`）。Vite 将 `/api` 代理到 `http://127.0.0.1:8080`，两端访问真实 Go 数据。页面不内置问题样例；需要从 Unity 或下面的 curl 创建数据。

Go 配置（进程环境变量）：

| 变量 | 默认值 | 说明 |
| --- | --- | --- |
| LISTEN_ADDR | 0.0.0.0:8080 | 本地开发监听；反向代理部署改为 127.0.0.1:8080 |
| DATA_FILE | data/issues.json | 相对于 Go 启动目录，重启务必使用同一路径；上线建议绝对路径 |
| FRONTEND_DIR | 空 | 设置后由 Go 托管 React dist，API 统一位于 /api；当前服务器使用部署环境文件配置 |
| CORS_ORIGINS | http://localhost:5173,http://127.0.0.1:5173 | 允许的浏览器来源，逗号分隔，不含末尾斜线 |

若端口被占用，不要停止其他服务。例如：

```bash
# Go 终端，在 backend/ 中
LISTEN_ADDR=0.0.0.0:18080 go run .
# 前端终端，在 frontend/ 中
DEV_API_TARGET=http://127.0.0.1:18080 npm run dev -- --port 15173 --strictPort
```

`DEV_API_TARGET` 仅用于开发代理。前端通过 `VITE_API_BASE_URL` 设置浏览器 API 前缀（默认 `/api`），见 `frontend/.env.example`。更改后重新启动开发服务/构建。直接跨域访问时同步修改 Go 的 CORS_ORIGINS。

后端测试与前端构建：

```bash
cd /home/skk/go_project/p1/backend
go test -race ./...
go vet ./...
cd ../frontend
npm run build
```

浏览器检查需要本机已安装 Chromium/Chrome：

```bash
cd /home/skk/go_project/p1
CHROME_PATH=/完整路径/chrome node scripts/browser-smoke.mjs
```

脚本自建独立数据与测试进程，检查空列表、状态修改、重启保留、失败恢复、移动布局和真实 CORS。默认端口 18080、15175；占用时通过 `SMOKE_API_PORT`、`SMOKE_WEB_PORT` 调整。截图生成在 `.cache/browser/`，只使用测试数据，不连接部署环境。

## Unity 打开、模拟与构建

1. 用 Unity Hub 安装 **2022.3.62f1 LTS**，勾选 Android Build Support、SDK/NDK Tools、OpenJDK。打开本仓库 `unity/` 目录，不要另建外层工程。先等待 Package Manager 解析包和脚本编译。
2. 首次打开，`ProjectSetup` 自动生成 `Assets/Scenes/Inspection.unity`、Marker/Plane Prefab、PriorityDropdown UI 资源及 XR 配置；也可执行菜单 **Inspection → Setup Project**。该操作保留已有场景，不重建用户修改的场景。设置 Input Handling 为 Both 后，若 Editor 提示重启，完成重启再继续；检查 Project Settings → Player → Active Input Handling 确为 Both。
3. 确认 Project Settings → XR Plug-in Management：Standalone 启用 XR Simulation，Android 启用 ARCore。生成器为 Android 配置 ARM64、IL2CPP、OpenGLES3、最低 API 24、竖屏、网络权限、AR Required，Depth Optional。运行 XR Project Validation 并处理实际 Editor/SDK 报错。
4. 打开 `Assets/Scenes/Inspection.unity`，Game 视图用竖屏比例如 720×1280。按 [Unity XR Simulation 5.1 文档](https://docs.unity3d.com/Packages/com.unity.xr.arfoundation@5.1/manual/xr-simulation/simulation.html) 选择包自带/导入的模拟环境；进入 Play，移动模拟相机观察水平面。鼠标右键配合 WASD 用于视角导航，在表单外左键放置标记。UI 显示“Editor 模拟验证”。
5. 上方显示 AR 状态；检测的平面显示为绿色，点击后放置 10 cm 橙色方块。表单填写标题、描述与优先级后提交。一次编辑一个标记，再点平面移动；点击 UI 不触发 AR 放置。成功清空并销毁当前标记；失败保留。坐标取标记根节点的世界坐标（米）。
6. **后端地址在运行中的表单首行直接填写**，将保存到 PlayerPrefs，覆盖默认值；也可修改 `Assets/Resources/api-config.txt` 后重新构建（按部署环境填写 `http://${HOST}:${PORT}/api`）。Editor 可用 `http://127.0.0.1:8080`；手机需用电脑可达的局域网 IP 或公网 `https://域名/api`，手机的 localhost 指手机自己。先用手机浏览器访问对应 `/health` 验证网络。
7. 获得官方支持的 ARCore 真机后，开启 USB 调试，安装/更新 Google Play Services for AR；切换 Build Target 到 Android，执行 **Inspection → Build Android Development APK**。构建产物 `unity/Builds/inspection-dev.apk`。也可批处理 `Unity -batchmode -quit -projectPath /完整路径/p1/unity -buildTarget Android -executeMethod ProjectSetup.BuildAndroid -logFile /日志路径`。打开相机权限并完成真机检查。
8. 开发 APK 允许 HTTP；非 Development 构建应使用 HTTPS。模拟器不能代替真机相机、传感器、权限和触摸验收。完成首次生成后，提交场景、Prefab、XR 配置及全部 `.meta`，忽略 Library/Temp/Logs/Builds。

[Google 的 Android Emulator 说明](https://developers.google.com/ar/develop/java/emulator) 要求 x86 系列模拟器。已下载检查本项目锁定的 ARCore 5.1.6 包：`UnityARCore.aar` 与 `ARPresto.aar` 原生库只包含 ARM，不能直接安装此 ARM64 构建在标准 x86 ARCore 模拟器上。当前开发路径因此选 Editor XR Simulation；它也尚需有 Unity Editor 的电脑实际运行。Android Studio 本身不替代 Unity。

## API

内部路径如下；公网同域代理时加 `/api` 前缀。

| 方法 | 路径 | 请求 | 成功响应 |
| --- | --- | --- | --- |
| GET | /health | 无 | 200 `{"status":"ok"}` |
| GET | /issues | 无 | 200 问题数组，初始为 `[]` |
| POST | /issues | title、description、priority、position | 201 完整问题 |
| PATCH | /issues/{id}/status | `{"status":"resolved"}` | 200 更新后的问题 |

从任意终端测试默认本地 Go：

```bash
curl http://127.0.0.1:8080/health
curl -X POST http://127.0.0.1:8080/issues \
  -H 'Content-Type: application/json' \
  -d '{"title":"入口墙面破损","description":"左侧墙体存在裂缝","priority":"high","position":{"x":0.42,"y":0.03,"z":1.26}}'
curl http://127.0.0.1:8080/issues
# 将下面的实际ID替换为 POST 返回的 id
curl -X PATCH http://127.0.0.1:8080/issues/实际ID/status \
  -H 'Content-Type: application/json' -d '{"status":"resolved"}'
```

返回的完整对象包含 `id/title/description/priority/status/position/createdAt/updatedAt`。ID 由 Go 生成；创建状态固定 `open`；时间为 UTC RFC3339Nano。更新状态保留其他字段，仅更新 status/updatedAt。

校验：title 去空白后非空，description 可选，priority 为 low/medium/high，status 为 open/in_progress/resolved。position 的 x/y/z 必须全部提供数值，零有效，缺失/null/非数值无效。创建不接受 id/status/时间等额外字段；请求上限 64 KiB。非法 JSON 或参数返回 400，问题不存在返回 404，保存失败返回 500。错误示例：

```json
{"error":{"code":"INVALID_ARGUMENT","message":"title 不能为空"}}
```

## 持久化、部署与限制

JSON 文件首次写入时创建，写入同目录临时文件后同步并重命名，成功后才替换内存。损坏数据在启动时明确报错并拒绝覆盖；写入失败不返回成功。设计为**单个 Go 进程**，不能让多个服务实例共享一个 JSON 文件。数据路径不是相对于可执行文件；重启及重新部署必须保持 DATA_FILE 不变。

`deploy/.env` 已配置当前服务器 ${PORT} 端口；修改后需重启本项目进程生效。部署和服务环境的具体用法见 [deploy/README.md](deploy/README.md)。

AR 坐标只在当前会话有意义，不提供跨设备、跨会话定位或历史标记恢复。未增加登录权限。创建请求没有幂等键或自动重试；网络响应丢失时有重复建单的可能，Unity 提示先查管理端再手动重试。中文字体使用系统动态字体，仍需在目标 Android 设备检查显示效果及安全区。当前没有实际 Unity 运行和真机视频的完成证据；HTTP 部署的实际验证结果见验收记录。
