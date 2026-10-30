# 园区巡检

基于 Unity AR、Go 和 React 的现场问题上报与管理应用。巡检人员在水平面上放置标记、填写问题并上报；管理人员通过网页查看记录和更新处理状态。问题、账号及登录会话保存到 SQLite。

## 功能

- AR 水平面检测、标记放置、问题表单、提交反馈与 UI 防误触。
- 问题列表、标题/描述/编号搜索、优先级和状态筛选。
- `open`、`in_progress`、`resolved` 三种处理状态。
- 用户登录、退出、修改密码；关闭浏览器或重启服务后，在会话有效期内保持登录。
- 初始账号 `Admin` 为超级管理员，可添加普通用户与管理员。
- 无域名 HTTP 部署：Caddy 托管页面，将 `/api/*` 转发到本机 Go 服务。

| 角色 | 网页操作 | 账号管理 |
| --- | --- | --- |
| 普通用户 `user` | 查看、搜索、筛选问题 | 修改自己的密码 |
| 管理员 `admin` | 普通用户操作及修改问题状态 | 修改自己的密码 |
| 超级管理员 `super_admin` | 管理员操作 | 添加普通用户和管理员、查看用户列表、修改自己的密码 |

**问题 API 按当前需求保持免认证，Unity 无需登录。** 用户管理 API 在后端校验超级管理员权限；网页对问题操作的角色限制不等同于问题 API 的访问控制。

## 目录与环境

```text
backend/       Go API、SQLite 数据层、登录和测试
frontend/      React + TypeScript 管理端
unity/         Unity AR 工程
scripts/       构建、进程管理和浏览器检查
deploy/       Caddy、环境模板与 systemd 示例
docs/         验收材料与演示说明
```

- Go 1.22 或以上；SQLite 使用纯 Go 驱动，无需安装数据库服务或启用 CGO。
- Node.js 22.12 或以上、npm。
- 部署需 Caddy 2；本地前端开发可使用 Vite 代理。
- Unity 目标版本见 `unity/ProjectSettings/ProjectVersion.txt`，AR 包版本见 `unity/Packages/manifest.json`；Android 需对应的 Build Support、SDK、NDK 和 OpenJDK。

## 本地开发

在项目根目录打开两个终端。首次启动须设置初始 Admin 密码（12～72 字节）：

```bash
cd backend
read -rsp 'Initial Admin password: ' ADMIN_PASSWORD
export ADMIN_PASSWORD
go run .
```

```bash
cd frontend
npm ci
npm run dev
```

打开 Vite 输出的地址，使用 `Admin` 和刚设置的密码登录。开发页面默认通过 `/api` 代理访问 `http://127.0.0.1:8080`。`ADMIN_PASSWORD` 只在初始化账号时使用，后续修改环境变量不会重置已有密码。

首次进入“用户管理”可加载用户列表并添加账号。用户首次登录后可在“修改密码”中更换初始密码。修改密码会使该账号的所有登录会话失效，需要重新登录。

## 无域名 HTTP 部署

对外页面为 `http://${HOST}:${PORT}/`，Unity 后端地址为 `http://${HOST}:${PORT}/api`。`${HOST}`、`${PORT}` 是文档占位符，替换为自己的服务器地址与端口；Unity 输入框不会展开环境变量。

1. 复制 `deploy/.env.example` 为 `deploy/.env`，填写 `HOST`、`PORT` 和初始 `ADMIN_PASSWORD`。保留 Go 的回环监听地址，确保 `API_UPSTREAM` 与 `LISTEN_ADDR` 一致。
2. 构建并启动：

   ```bash
   bash scripts/build-release.sh
   python3 scripts/server.py start
   python3 scripts/server.py status
   ```

3. 访问 `http://${HOST}:${PORT}/api/health`，应返回 `{"status":"ok"}`，然后打开网页登录。
4. 更新源码后重新构建，执行 `python3 scripts/server.py restart`。停止使用 `python3 scripts/server.py stop`。

脚本启动本项目的 Go 与 Caddy 后台进程，不修改系统已有 Caddy 配置。日志在 `.cache/server/server.log`、`.cache/server/caddy.log`。普通后台进程不会开机自启；长期运行可使用 [部署说明](deploy/README.md) 中的 systemd 示例。

当前不启用域名、证书或 HTTPS；HTTP 使用 `COOKIE_SECURE=false`。环境文件、SQLite 文件和真实部署截图不提交到 Git。

### 环境变量

| 变量 | 用途 |
| --- | --- |
| `PORT` / `BIND_HOST` | Caddy 对外端口和监听网卡 |
| `HOST` | 访问地址说明使用的主机名，不限制 Caddy 的 Host 请求头 |
| `USE_CADDY` | `true` 启动 Caddy；`false` 由 Go 同时提供页面和 `/api` |
| `LISTEN_ADDR` | Go 监听地址，本地开发默认 `0.0.0.0:8080`，Caddy 部署使用回环地址 |
| `API_UPSTREAM` | Caddy 转发目标，须与 Go 监听地址一致 |
| `DB_FILE` | SQLite 路径，Go 默认 `data/inspection.db`，相对于进程工作目录 |
| `LEGACY_JSON_FILE` | 可选：旧问题 JSON 路径，用于一次性迁移 |
| `FRONTEND_DIR` | 构建后的静态资源目录 |
| `ADMIN_PASSWORD` | 首次创建 Admin 的密码，12～72 字节，不会覆盖已有密码 |
| `COOKIE_SECURE` | HTTP 为 `false`，后续启用 HTTPS 时设为 `true` |
| `CORS_ORIGINS` | 允许的浏览器来源，逗号分隔；同域访问无需添加来源 |
| `VITE_API_BASE_URL` | 前端 API 前缀，默认 `/api`；构建时生效 |
| `DEV_API_TARGET` | Vite 开发代理目标，默认 `http://127.0.0.1:8080` |

`server.py` 的 `.env` 解析器只读取键值，不执行 shell 命令，也不展开 `${...}`。模板中的路径相对于项目根目录；直接运行 `go run` 时路径相对于其工作目录。

## 数据与迁移

SQLite 保存问题、用户、密码哈希和会话。密码使用 bcrypt 哈希，会话以 HttpOnly、SameSite=Lax Cookie 保存，默认有效期 24 小时；数据库中只保存会话令牌的哈希。

从旧 JSON 版本升级时，将 `LEGACY_JSON_FILE` 指向原数据文件并将 `DB_FILE` 指向新的 SQLite 文件。启动时在一个事务中导入问题，保留原 ID、状态、坐标和时间；导入成功后记录迁移标记，后续启动不会再次导入。损坏文件、重复 ID 或已有非空数据库会导致迁移失败，不会部分写入，也不会修改原 JSON。

重新部署不要覆盖 `DB_FILE`。SQLite 使用 WAL 模式，运行时不要只复制 `.db` 文件作为备份；可使用 SQLite 的 `.backup` 命令，或停止本项目后再备份数据库。备份包含用户及会话数据，应与环境文件一并妥善保存。

## API

下表为 Go 内部路径。通过 Caddy 访问时加 `/api` 前缀，例如 `/api/issues`。

| 方法 | 路径 | 输入/用途 | 权限 |
| --- | --- | --- | --- |
| GET | `/health` | 服务与数据库状态 | 开放 |
| GET | `/issues` | 返回问题数组；空列表为 `[]` | 开放 |
| POST | `/issues` | 创建问题 | 开放 |
| PATCH | `/issues/{id}/status` | `{"status":"resolved"}` | 开放 |
| POST | `/auth/login` | `{"username":"Admin","password":"..."}` | 开放 |
| GET | `/auth/me` | 当前用户 | 登录 |
| POST | `/auth/logout` | 注销当前会话 | 当前会话 |
| PATCH | `/auth/password` | `currentPassword`、`newPassword` | 登录 |
| GET | `/users` | 用户列表，不包含密码哈希 | 超级管理员 |
| POST | `/users` | `username`、`password`、`role` | 超级管理员 |

创建问题：

```json
{
  "title": "入口墙面破损",
  "description": "左侧墙体存在裂缝",
  "priority": "high",
  "position": {"x": 0.42, "y": 0.03, "z": 1.26}
}
```

成功返回 `201` 和完整问题：`id/title/description/priority/status/position/createdAt/updatedAt`。ID 和 UTC 时间由服务生成，初始状态为 `open`。优先级为 `low/medium/high`，标题去空白后必填（最多 600 字节），描述可选（最多 12000 字节），坐标必须全部为数值，零有效。请求上限 64 KiB。

用户名为 3～32 位，以英文字母开头，可含数字、下划线、点、横线；不区分大小写。创建用户时 `role` 只能为 `user` 或 `admin`，不能创建第二个超级管理员。

错误统一返回 `{"error":{"code":"...","message":"..."}}`：参数错误 `400`、未登录 `401`、权限不足 `403`、不存在 `404`、用户名冲突 `409`、数据库错误 `500`。前端保留失败操作之前的状态并提供错误提示。

## Unity Android

前后端在服务器运行；Windows 负责 Unity/Android 开发和设备测试。当前 Unity 源码与资源来自 `unity-android-validation` 的 `4017994`，使用 Unity **2022.3.62f3c1**、AR Foundation/ARCore **5.2.0**、XR Management **4.5.0**、Input System **1.14.0**、URP **14.0.12**。

使用 Unity Hub 打开 `unity/`，等待包解析和编译，手动执行 **Inspection → Setup Project**，打开 `Assets/Scenes/Inspection.unity`。输入模式为 **Input System Package (New)**。AR 相机绑定手机 HandheldARInputDevice 的位置/旋转，并保留 XRHMD 兼容输入；Play 模式可通过 **Inspection → Validate Handheld Pose Input** 复查手机布局绑定。 Setup 与构建会把标记升级为置顶动画样式（7 厘米浮动旋转方块、12 厘米青色网格底盘），底盘和检测平面均使用 10% 不透明度的透空网格；标记位于相机背景与场景物体之前，不受深度遮挡。URP 工程可执行 **Inspection → Validate Marker Rendering**，用前景遮挡物渲染同一 Marker Prefab，输出 Builds/marker-grid-validation.json 与对照图，并检查动画不改变标记根节点坐标。该检查覆盖 Editor 渲染，真机可见性仍需设备回测。场景、Prefab 和 `.meta` 已纳入源码；仓库副本仍需在安装 Unity 的机器上完成独立导入验证。

| 构建菜单 | 产物 | 用途 |
| --- | --- | --- |
| Inspection → Build Android Development APK | `Builds/inspection-dev.apk` | ARM64 手机，ARCore 真实平面检测 |
| Inspection → Build x86_64 Simulation APK | `Builds/inspection-x86-simulation.apk` | x86_64 AVD，虚拟地面与表单流程测试 |

模拟版具有独立包名和明确的模拟标识，不提供真实 AR。两个版本提交同一问题结构，无需登录；当前服务器无需为新版登录系统重新构建 Unity APK。

运行时选择 API 地址“默认”或“自定义”：默认读取 `Assets/Resources/api-config.txt` 并隐藏输入框；自定义显示输入框，地址及选项保存到 PlayerPrefs。仓库默认使用本地开发地址，实际设备填写 `http://${HOST}:${PORT}/api`。手机中的 localhost 指手机自己，配置后先在浏览器访问对应 `/health` 检查网络。

开发构建允许 HTTP。实际平面检测、相机权限、触摸防误放置和字体显示需要在支持 ARCore 的真机上验证；AVD 虚拟地面不能替代真机验收。坐标属于当前 AR 会话或模拟测试场景，不支持跨会话物理位置恢复。已交付 APK 信息与 Windows 接手位置见 [Unity 交接说明](docs/handoff.md)。

## 验证

```bash
cd backend
go test -race ./...
go vet ./...
cd ../frontend
npm run build
```

安装 Chrome/Chromium 与 Caddy 后，可在项目根目录运行：

```bash
CHROME_PATH=/完整路径/chrome node scripts/browser-smoke.mjs
```

脚本使用独立 SQLite 数据和测试端口，验证登录、角色、用户创建、密码修改、问题接口、状态更新、重启持久化、后端失败恢复及 Caddy 路由。默认测试端口为 18080、15175，可用 `SMOKE_API_PORT`、`SMOKE_WEB_PORT` 修改；不会修改部署数据库。
