# 当前验收结果

更新：2026-10-30。本轮对照 `origin/unity-android-validation`（4017994）完善服务端和管理端，以下为当前版本结果；文末保留分支交接时的历史证据。

| 验证 | 当前结果 |
| --- | --- |
| Unity 接口兼容 | 对照分支 CreateIssue/IssueResponse，保留字段、枚举、201 对象与错误封装，问题接口不加认证 |
| SQLite 生命周期与并发 | Go race 测试通过；新建、状态变更、重载和 20 并发创建完整 |
| JSON 迁移 | 一次性、事务原子性、重复导入不覆盖后续修改；坏数据/重复 ID 回滚 |
| 写入失败 | SQLite query_only 模式下 POST/PATCH 返回 500，已有状态不变 |
| Admin / 密码 | 初始化密码要求、首次创建、再次启动不重置、bcrypt、密码更改和所有会话失效通过 |
| 用户权限 | 未登录用户管理 401、普通用户/管理员 403、super admin 创建 user/admin 成功；用户名大小写冲突 409、不能创建第二个 super admin |
| 会话 | 登录、me、退出、过期及数据库重连后有效会话恢复通过；Cookie HttpOnly/SameSite=Lax |
| 来源保护 | Cookie 账号变更拒绝未获允许的 Origin；问题 API 继续开放 |
| 前端 | TypeScript 与 Vite 构建通过；增加搜索、筛选、角色入口和无效列表响应保护 |
| Caddy | v2.10.2 validate 通过，无域名 HTTP，/api 前缀剥离、未知 API 404、SPA 回退通过 |
| 浏览器回归 | Admin 登录、创建两类账号、角色按钮、后端权限、退出、密码修改、状态流转、持久化会话、停服重试、跨域 PATCH、移动布局与无未捕获异常通过 |
| 实际部署 | Caddy + Go 双进程已运行，健康检查、Admin 登录/退出、用户列表通过 |
| 实际迁移 | 原 JSON 的 3 条问题完整迁移，ID、状态、坐标、时间不变，源文件 SHA-256 不变；SQLite integrity_check=ok |

本轮 `go test -race -cover ./...` 覆盖率 75.1%；`go vet ./...` 通过。浏览器脚本使用独立测试数据库，线上检查没有创建额外问题或用户。初始 Admin 密码随机生成并写入忽略文件 `deploy/.env`，未写入 Git 或日志。

本轮截图：[登录](screenshots/login.png)、[问题列表](screenshots/desktop.png)、[用户管理](screenshots/users.png)、[移动布局](screenshots/mobile.png)、[失败提示](screenshots/error.png)。

Unity 资源与代码同步自已验证分支；Linux 当前机器没有 Unity Editor，因此本轮没有重新构建 APK或宣称真机通过。真实 AR、手机兼容性、仓库副本独立导入及视频仍按下方记录继续验收。

---

# 验收记录

更新：**2026-10-30**。将历史 Linux 验证、本机 Unity/AVD 验证与本轮交接复查分开记录。最新结论覆盖此前阶段性失败状态；保留关键失败原因供接手排查。

## 当前结论

| 范围 | 结论 |
| --- | --- |
| Go / React 业务与部署 | 历史 Linux 测试、浏览器联调及 HTTP 重启持久化通过；服务器后续验收由服务器端处理 |
| 实际 Unity 工程 | 编译、场景资源、表单播放和 Android 两种包构建通过 |
| 最新 x86_64 模拟包 | AVD 覆盖安装/冷启动、默认与自定义切换、旧地址保留及重启记忆通过 |
| 模拟上报 | 前一版模拟包已真实创建一条测试记录并核对、改 resolved；最新地址 UI 版本没有再次向服务器写入 |
| 真实 AR 与题目总验收 | 未完成：真机安装兼容性、真实水平面、触摸验收、真机闭环、仓库副本独立导入与视频 |

## 历史 Linux Go / React 验证（2026-10-29）

环境：`/home/skk/go_project/p1`，Go 1.22.2、Node 24.21.0、npm 11.19.0。

| 验证 | 结果与证据 |
| --- | --- |
| 参数校验 / CORS | TestInvalidInputsAndCORS：空白标题、枚举、缺失/null/字符串/溢出坐标、未知字段、坏 JSON、多 JSON 对象 |
| 默认值 / 更新 / 重新加载 | TestLifecycleAndReload：ID、open、UTC、标题裁剪、零坐标和状态更新 |
| 并发持久化 | TestConcurrentCreates：20 并发创建及重载；go test -race 通过 |
| 保存失败保护 | TestSaveFailureKeepsMemoryAndFile：返回 500，内存和文件不变 |
| 损坏数据 | TestCorruptFileRejected：拒绝加载，不覆盖 |
| 静态检查 / 覆盖率 | go vet 通过；历史测试覆盖率 80.5% |
| TypeScript / Vite | production build 通过 |
| 浏览器 | 空列表、真实 API 新建、in_progress/resolved、进程重启、后端失败与恢复、实际 CORS/预检通过 |
| 布局 | Chromium 1440×1100、390×844 无横向溢出、无未捕获页面错误 |
| 发布脚本 | build-release.sh 构建 Linux amd64 Go 及前端产物；语法检查通过 |

历史截图：[桌面](screenshots/desktop.png)、[移动](screenshots/mobile.png)、[后端不可用](screenshots/error.png)。浏览器脚本为 scripts/browser-smoke.mjs，使用独立数据，不把 API 客户端创建的数据当作手机 AR 上报。

服务器历史部署由 Go 同端口托管页面与 `/api/*`，环境在忽略文件 deploy/.env，数据 backend/data/issues.json，运维脚本 scripts/server.py。回环与内网 health 通过，用户确认公网访问。部署验证记录 `2682d5770b8810cc29dfb0ac9ceb040a` 的页面状态更新与服务重启保留已记录；该 ID 以原始记录为准，文档交接不重新查询线上。服务器仍是临时 HTTP，无 HTTPS/域名/开机自启验收。

## Unity / Android 本机验证（2026-10-30）

- 实际工程 `D:\Unity\project\My project`，Unity 2022.3.62f3c1；AR Foundation/ARCore 5.2.0、XR Management 4.5.0、Input System 1.14.0、URP 14.0.12。仓库直接依赖已同步，仓库副本全新导入待验证。
- 修复 TouchPhase 二义性，改用 Input System；先配置禁用的 XR Origin、相机、位姿驱动后再激活，原初始化顺序警告不再复现。Editor 实际启动场景并显示表单；Editor 的完整 AR 平面命中和上报未验收。
- 场景、Marker/Plane Prefab、PriorityDropdown 及模拟材质已生成并同步源码副本，.meta 随资产保留。Setup Project 为手动菜单，不再自动执行。
- Android Studio 管理的 AVD：Medium_Phone_API_37.0，Android 17 API 37 x86_64，Emulator 37.2.12。Google 官方 ARCore 1.56 x86 服务已安装；相机授权由用户完成。
- Gradle 早期失败包括 Android 插件解析与依赖 HTTPS TLS 中断；加入镜像与官方回退、使用缓存与最多两个 worker 后生成包，最新两种 APK 的 Unity 构建结果均为 Success。调试签名隔离问题已处理。

### AR 模拟器诊断边界

ARCore 5.2.0 的 UnityARCore.aar / ARPresto.aar 仅含 ARM 原生库；下载检查 5.2.2 仍然如此。纯 x86_64 AR 包启动报 `DllNotFoundException: libUnityARCore.so not found`。ARM64 包虽可转译安装，AR 会话仍报 `failed to load symbols` / `AR_ERROR_FATAL`，未建立可验收 AR 会话。因此独立模拟包关闭 XR 自动初始化，普通相机和虚拟平面用于表单业务测试。

### 模拟业务上报（前一版模拟包）

AVD 放置标记显示坐标、空标题被阻止；应用创建 `[SIMULATION TEST] x86_64 2026-10-30`，成功后清空表单和标记。GET 核对 ID `327a5dc1bea0a22e9c74170f18866315`、默认 open/medium、坐标约 (-0.00585, 0, 2.19408)；PATCH 将该记录设为 resolved。只改动该测试记录，不宣称已完成手机 AR 或该记录的服务器重启验证。

### 最新 API 下拉版

- 最新手机包仅含 arm64-v8a，最低 API 24 / target 35，包名 cn.inspection.prototype；签名 v2/v3 通过，证书与先前手机包相同。
- 最新模拟包仅含 x86_64，独立包名 cn.inspection.prototype.simulation；签名 v2 通过，AVD 覆盖安装、冷启动成功。
- 对两个 APK 解包核对默认资源均包含用户确认的 `${TEST_SERVER}/api`，未把实际地址写入仓库默认配置或文档。
- Unity 播放及 AVD 验证默认隐藏地址、自定义输入显示、旧地址保留；AVD 冷启动仍保留自定义模式和值，最后切回默认。下拉选项行高从 20px 调整以修复大字号文字裁切。
- 新版仅复查界面与启动，没有再次向真实服务器提交。启动日志有既有两个 missing script 警告及 EGL/GFXSTREAM 兼容日志；应用持续运行，未观察到新的 C# 异常或崩溃。
- 用户报告真机“未对最新64位兼容”，原因未确定；尚未拿到设备型号、Android 版本和安装日志，不把 ABI/签名通过写为真机安装已解决。

## 本轮交接复查（2026-10-30）

- 按用户最终分工，Windows 仅处理 Unity/Android，Go/React 的运行、部署和验收归服务器端；不再继续本机前后端工作。
- 在收到分工修正前已启动的临时检查已结束：Go 1.27.3 的 `go test -cover ./...` 通过（78.0%），`go vet ./...` 通过；Node 24.5.0 / npm 9.6.4 安装锁定依赖后 TypeScript/Vite 构建通过。该结果仅作过程记录，不代替服务器验收，也不构成 Windows 前后端交付要求。首次沙箱临时目录 rename 被拒绝，改为工作区临时目录后通过。
- 这些操作未修改前后端源码或依赖锁文件；生成的 node_modules/dist 为忽略产物，未启动本机业务服务。未重启线上服务、未更改线上数据、未远程推送。
- README、plan、验收与交接说明已按当前已完成项统一更新，清除“未安装 Unity / 未生成 APK”等过期状态；文档本地链接、版本、产物 SHA-256 与 Git 空白差异检查见交接清单。

## 待补验收

1. 真机安装兼容性、ARCore 支持、相机授权及真实水平面/标记/触摸防误触。
2. 手机上报与 React 更新同一 ID，并在固定 DATA_FILE 的 Go 重启后复核。
3. Unity 提交网络失败时表单/标记保留与恢复的实际运行证据。
4. 仓库 Unity 副本首次解析包、Setup、场景资源、构建；实际模板工程成功不替代此项。
5. 两段各 3～5 分钟手机和电脑视频，按 docs/demo.md 保存文件或链接。

题目全部验收尚未完成。源码、已验证模拟流程与最新 APK 可交接，未完成项按 plan.md 继续。

## 真机标记显示修复（2026-10-30，基于 origin/master 9e51a24）

- 用户确认点击后出现坐标但无可见标记，说明放置路径执行；实际工程 URP 与 Marker Standard / Plane Unlit-Color 不匹配。修正 Setup，使已存在的材质同样迁移，移除仅模拟版替换标记材质的分支。
- 新增可重复菜单 Inspection/Validate Marker Rendering。在 URP-Performant、256×256 渲染中，旧 Standard 材质显示紫色错误材质，橙色像素 0；真实 Marker Prefab 修复材质橙色像素 10304，验证通过。输出 Builds/marker-render-validation.json 和两张对照 PNG。
- 该检查仅证明 Editor 的材质回归已修复，没有完整复现真机完全不可见；真机 AR 回测仍待完成。放置日志增加 shader、viewport 与 distance 以便继续排查。
- 本次未运行 Android 模拟机；需要时依用户要求采用 ARM 模型机。没有连接项目 API，也没有运行 Windows 前后端。
- 新 ARM64 包 inspection-phone-arm64-marker-fix.apk 构建 Success；签名 v2/v3 及原证书一致，只有 arm64-v8a、ARCore 桥接库、新放置诊断和默认地址均已确认。SHA-256：f4bf6e6d76fb7097d420302085318b0192eafb5e07145c243007ea4d1ae7badd。真机复测待用户安装确认。

## 放大、动画与置顶显示（2026-10-30）

- 用户反馈上一版真机仍不可见，要求更大、可动且显示在真实背景前。本轮不把“背景遮挡”当作已证实的唯一根因，而是按要求明确实现置顶绘制。
- 标记方块 0.28m，底盘 0.48m，子物体每秒一轮上下浮动（幅度 0.05m）并每秒旋转 55 度；根节点和上报位置不随动画变化。
- 自定义 MarkerOverlay 使用透明末尾队列 4000、ZTest Always、ZWrite Off；AR 相机请求 BeforeOpaques。世界空间定位仍由 AR 命中决定，UI 表单正常显示在场景之上。
- Inspection/Validate Marker Rendering 在 URP-Performant 用前景物体遮挡真实序列化 Prefab：普通材质无遮挡 8824 橙色像素、遮挡后 0；置顶材质 5202、动画帧 6459。animationMoved 与 anchorUnchanged 均为 true，验证通过。文件 Builds/marker-overlay-validation.json 与对照图。
- 本轮没有 Android 模拟机或真机运行证据；新 ARM64 包需在用户原真机覆盖安装复测。

- 补充定位到手机相机位姿绑定错误：ARCore 注册的是 HandheldARInputDevice，原 XRHMD-only 绑定不匹配。增加手机 devicePosition/deviceRotation，保留编辑器头显绑定。Play 模式合成设备验证旧绑定不匹配、两个新绑定匹配，位置和旋转值读取通过；这只是输入回归，没有连接真机。
- 最终 ARM64 包 inspection-phone-arm64-overlay-animated.apk 已包含手机位姿修正，构建 Success；签名 v2/v3 通过，原签名一致。解包确认 Overlay Shader、动画组件及手机位置/旋转绑定均在包内，原默认 API 配置保留。SHA-256：f68cb9f67dc55839dc514b133d8287d5190a0eb4ad04c7cb26a57871b8fd41c7。设备可见性仍待用户真机复测。

## 四分之一尺寸与透明网格（2026-10-30）

- 用户确认上版真机已经能看到标记；本轮新增外观调整尚未真机回测。
- 方块宽度 0.07m、底盘直径 0.12m；浮动中心 0.075m、幅度 0.0125m，均为上一版的 1/4。AR 根坐标及位姿输入不变。
- 底盘与检测平面均使用 TransparentGrid，alpha=0.1；网格间隙 clip 透空、ZWrite Off。底盘改用单层圆盘 Mesh（高度 0），避免自身前后两面叠加；底盘仍置顶，检测平面保持正常深度规则。
- Editor 回归通过：普通材质被遮挡为 0 橙色像素，置顶为 326、动画帧 401；animationMoved/anchorUnchanged 均 true。检测平面网格只改变 9840/65536 像素；底盘内部只改变 3652/20069 像素，其余保留背景。两个 alpha 均核对为 0.1。
- 输出 Builds/marker-grid-validation.json、grid-detected-plane.png、grid-marker-base.png。点击范围未随网格空隙变化，仍使用原 AR 平面命中。

- 新 ARM64 包 inspection-phone-arm64-small-grid.apk 构建成功，签名 v2/v3 与原手机签名一致；解包确认新透明网格/置顶 Shader、手机位姿绑定及默认 API 配置。SHA-256：336a061d1bd6375a164af12689c2c265894190ce98c41b80031e300314b5151b。新外观真机回测待完成，未提交或推送。

## 最新真机外观确认与提交（2026-10-30）

- 用户安装最新 inspection-phone-arm64-small-grid.apk 后反馈“可以了 提交git”，本轮 7cm 标记、12cm 底盘及两类 10% 不透明度网格的真机外观回测通过。此为用户反馈证据，未取得设备日志或录屏。
- 此确认仅覆盖本轮外观问题；服务器同一 ID 闭环、失败恢复、仓库副本独立导入及演示视频等项目仍按 plan 待验收。
- 本轮源码与验证文档创建本地 Git 提交，未推送；未运行前后端或连接项目 API。
