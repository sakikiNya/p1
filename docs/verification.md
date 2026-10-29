# 验收记录

日期：2026-10-29。项目目录 `/home/skk/go_project/p1`。

## 当前服务器 HTTP 部署（2026-10-29）

- 已在本服务器持续运行：`LISTEN_ADDR=0.0.0.0:${PORT}`，由 Go 同端口提供 React 页面及 `/api/*`。
- 环境文件：`deploy/.env`；数据：`backend/data/issues.json`；日志：`.cache/server/server.log`；脚本：`scripts/server.py`。
- 本机 `127.0.0.1:${PORT}` 与内网网卡 `${PRIVATE_HOST}:${PORT}` 的 `/api/health` 均返回 `{"status":"ok"}`；监听检查确认 ${PORT} 已开放监听。
- Chromium 直接访问该端口的构建后页面，创建 `[部署验证] HTTP ${PORT} 联调` 并用页面将其改为 resolved，未出现未捕获页面错误。保留一条验证记录，ID `2682d5770b8810cc29dfb0ac9ceb040a`。
- 实际停止并重启本项目服务，再次查询上述 ID 为 resolved，磁盘数据与 API 一致。
- 适配静态托管与 `/api` 前缀后，新增 `TestSiteHandler` 并通过全部 Go race 测试及 vet；前端重新构建通过。
- 部署截图含环境信息，按后续要求不纳入 Git；通用页面截图见下文。
- 公网地址为 `http://${HOST}:${PORT}/`，用户已实际验证可访问。此前服务器回访公网 IP 超时的现象不影响用户已确认的外部访问；UFW 状态为 inactive，未修改全局防火墙。
- 临时 HTTP，尚未配置域名、HTTPS 或开机自启。Unity 实际部署 API 地址为 `http://${HOST}:${PORT}/api`，仓库默认值使用本地开发地址；已有 PlayerPrefs 地址仍须在表单中修改。

## 已实际执行

| 验证项 | 结果 | 方法与证据 |
| --- | --- | --- |
| Go 参数校验 | 通过 | `TestInvalidInputsAndCORS`：空白标题、错误优先级/状态、缺失/null/字符串/溢出坐标、未知字段、坏 JSON、多个 JSON 对象 |
| 默认值与更新 | 通过 | `TestLifecycleAndReload`：唯一 ID、open、UTC 时间、标题裁剪、零坐标；全部状态可更新，原始字段不变 |
| 并发保存 | 通过 | `TestConcurrentCreates`：20 个并发创建，重载后数量和 ID 完整；`go test -race` 通过 |
| 持久化失败 | 通过 | `TestSaveFailureKeepsMemoryAndFile`：确定性触发创建/状态保存失败，返回 500，内存和原文件不变 |
| 损坏文件 | 通过 | `TestCorruptFileRejected`：拒绝启动，不覆盖坏文件 |
| Go 静态检查 | 通过 | `go vet ./...`；测试语句覆盖率 80.5%（main 启动部分另由浏览器进程检查覆盖行为） |
| 前端构建 | 通过 | TypeScript 检查 + Vite production build |
| 发布产物 | 通过 | `bash scripts/build-release.sh`：Linux amd64 Go 可执行文件与 React dist 已生成，未上传服务器 |
| 空列表与真实数据 | 通过 | Chromium 打开页面，通过 POST 创建真实后端问题，刷新后显示 |
| 状态更新 | 通过 | 浏览器依次保存 in_progress 和 resolved，再次读取确认 |
| 服务进程重启 | 通过 | 浏览器脚本终止并重新启动编译后的 Go 二进制，同一文件重载；相同 ID 保持 resolved |
| 后端不可用 | 通过 | 停止 Go 后保存失败保留原状态；刷新显示错误且保留上次数据；恢复 Go 后重试成功 |
| 浏览器 CORS | 通过 | 页面直接跨域 GET 与 JSON PATCH；实际预检和请求成功 |
| 桌面/移动布局 | 通过 | 1440×1100 与 390×844；移动宽度无横向溢出，无未捕获页面错误 |
| 环境文件忽略 | 通过 | `git check-ignore deploy/.env` 返回该文件 |
| 脚本语法 | 通过 | `node --check scripts/browser-smoke.mjs` 和 `bash -n scripts/build-release.sh` |

后端测试命令：在 backend 中执行 `go test -race -cover ./...`。此机器缓存位于项目 `.cache/go-build`。

浏览器检查：`CHROME_PATH=/完整路径/chrome node scripts/browser-smoke.mjs`，测试脚本启动并关闭自己的 Go/Vite 进程，独立数据位于 `.cache/browser/issues.json`。测试创建的数据不是 Unity 真机上报数据。截图：

- [桌面列表与已解决状态](screenshots/desktop.png)
- [移动宽度](screenshots/mobile.png)
- [后端不可用提示](screenshots/error.png)

## Unity 环境检查与限制

- `command -v unity`、`command -v adb` 未找到可执行文件，当前环境无法实际导入、编译或运行 Unity 项目。
- 已从 Unity 官方包源下载所锁定的 AR Foundation、ARCore 与 XR Management 包，在 `.cache/unity-packages/` 检查相关 API 和原生库；这只是源码/包检查，不是 Unity 编译通过。
- ARCore 5.1.6 的 `UnityARCore.aar` 仅含 `armeabi-v7a`、`arm64-v8a` 的 libUnityARCore.so；`ARPresto.aar` 同样仅含 ARM。虽然 arcore_client.aar 本身也有 x86 库，完整插件仍缺失匹配的 UnityARCore/ARPresto x86 库。
- 因此本方案退出标准 x86 AVD 路径，准备使用 Editor XR Simulation。该路径仍需在装有 Unity 的机器上验证，当前不能勾选“模拟 AR 已运行”。
- `ProjectSetup.cs` 负责生成场景、Prefab、Dropdown、XR 和 Android 设置；这些生成结果尚未经过实际 Editor 检验和提交，需要首次打开工程后生成并保留。

## 尚未完成

| 待办 | 所需条件 |
| --- | --- |
| Unity 首次导入、编译、生成资源与 XR Simulation 运行 | Unity 2022.3.62f1 及包下载能力；检查 Both 输入模式、模拟环境、字体和表单布局 |
| Android APK 构建与安装 | Unity Android Build Support / SDK / NDK / OpenJDK |
| 真实环境水平面检测、触摸 UI 防误放置、相机权限 | 官方支持的 ARCore 真机 |
| Unity → Go → React 端到端验收 | 上述 Unity 运行环境；当前已验收链路起点是测试 API 客户端 |
| 域名与 HTTPS | 按最新用户要求暂不使用 |
| 手机、电脑各 3～5 分钟视频 | 真机与录像工具，按 docs/demo.md 执行 |

**未宣称完成题目全部验收。** 已提供可本地运行并验证的 Go/React，Unity 源码与初始化工具，以及后续部署和设备验收材料。
