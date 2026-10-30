# 开发阶段验证交接

日期：**2026-10-30**。接手结论：Go/React 历史验证、Unity 本机开发、ARM64 构建、x86_64 模拟业务与新版地址 UI 验证已完成；真机 AR、安装兼容性、仓库副本全新导入和两段视频仍待完成。详见 README、plan 与 verification。

## 当前服务端升级

服务端现已升级为 SQLite、登录与无域名 Caddy HTTP 部署，使用方式以根目录 README 和 deploy/README.md 为准。旧 JSON 已一次性导入并保留源文件；问题 API 保持开放，与本分支的 Unity APK 兼容，用户管理要求超级管理员登录。下文 Windows 产物路径与 APK 验证结论保留为交接记录。

## 工作分工

- **Windows：** Unity 开发、Android APK 构建、Android Studio/AVD 测试及真机配合。无需运行 Go 或 React。
- **服务器：** Go 和 React 依赖、构建、运行、部署、持久化和浏览器验收；Unity 仅在测试时连接用户确认的服务器。

## 接手位置

| 内容 | 位置 |
| --- | --- |
| 本地源码 / Git 分支 | `G:\tmp\project1029\p1` / `unity-android-validation` |
| 实际开发与构建的 Unity 工程 | `D:\Unity\project\My project` |
| 同步源码副本 | `G:\tmp\project1029\p1\unity`；尚未全新导入 |
| 最新 APK 与交接副本 | `C:\Users\AojiaoKong-Aya\Documents\Codex\2026-10-29\gen-j\outputs` |
| Unity 日志 | `%LOCALAPPDATA%\Unity\Editor\Editor.log`；下一次操作可能继续写入 |
| 本轮工作日志与缓存 | Codex 工作区 `work/`；不纳入 Git |

代码、生成资产与文档随本次本地提交保存在工作分支，使用 git log -1 查看提交编号。提交包含生成资产与 .meta；本机地址、数据、调试密钥、Library 和缓存不纳入 Git。未执行远程推送，远程由用户处理。

## 最新安装包

| 文件 | 平台 / 包名 | 校验与运行结论 |
| --- | --- | --- |
| inspection-phone-arm64-api-options.apk | ARM64 / cn.inspection.prototype | ABI、默认地址、签名 v2/v3 通过，沿用先前手机签名；未真机验收 |
| inspection-x86-simulation-api-options.apk | x86_64 / cn.inspection.prototype.simulation | ABI、默认地址、签名 v2 通过；AVD 安装、启动、地址切换及重启设置保留通过 |

两个包都是 Development 测试包，默认服务器已配置为用户确认的 `/api` 地址，界面隐藏其值。模拟包不提供真实 AR。旧的 diagnostic 包用于排错，交接使用上述最新包。

SHA-256（同时见 outputs/SHA256SUMS.txt）：

```text
981622716ed1db08fac835ad99f3e2a83c32de718ddf7454bbfdc47fb78849a6  inspection-phone-arm64-api-options.apk
d8bfd9b174e73ec82d96f7e2b817c49dcea9a3c690109c1762f28dedf0928982  inspection-x86-simulation-api-options.apk
```

PowerShell 核对：`Get-FileHash -Algorithm SHA256 -LiteralPath '<APK完整路径>'`。

## 地址和重建

首次使用为默认模式；选择自定义显示输入框。PlayerPrefs 中 apiAddressMode 保存选项，customApiBaseUrl 保存自定义值，兼容旧 apiBaseUrl。最后的 AVD 测试已切回默认。

默认配置位于实际工程 Assets/Resources/api-config.txt；本机包含确认地址，仓库副本保留本地开发地址。界面隐藏不代表 APK 中地址被加密。构建前按目标环境在本机文件填写完整 URL，再执行相应菜单；应用仅提交时联网。

使用 Unity 2022.3.62f3c1；打开 Inspection.unity。手机执行 Inspection → Build Android Development APK；AVD 执行 Build x86_64 Simulation APK。两者在构建前会调用 Setup。模拟构建之后再次生成手机包时仍应使用手机菜单，避免手工复用 x86 架构或 Gradle 中间产物。最后一次只读检查的磁盘架构为 x86_64；手机菜单会先执行 Setup 设置 ARM64。已补充模拟构建 finally 末尾保存恢复配置，此 Editor 保存调整未再完整构建，不能据此宣称当前磁盘设置已恢复；最新两个 APK 的实际 ABI 已独立核对。

## 服务与测试数据

服务器仅在测试时连接；文档交接未探测线上当前状态、修改数据或重启服务。服务器运维和启动分别见 deploy/README.md 与 README；Windows 不承担前后端启动。

前一轮模拟上报保留一条 `[SIMULATION TEST] x86_64 2026-10-30`，ID `327a5dc1bea0a22e9c74170f18866315`，状态已为 resolved。最新下拉版没有再次创建测试记录。不要把该记录当成真实 AR 采集数据；创建无幂等键，响应丢失后先查管理端再重试。

## 接手后优先处理

1. 收集真机型号、系统版本、完整安装提示及安装日志，确认设备 ARCore 支持，实际安装最新 ARM64 包。
2. 完成真实水平面、放置与所有 UI 防误触测试，并用同一 ID 跑手机、React、Go 重启闭环。
3. 另行验证仓库 unity/ 全新导入，记录包解析锁文件、生成 XR 配置与 Android 构建结果。
4. 排查已有两个 missing script 警告及目标机显示/键盘/安全区；AVD 图形日志不等于真机结果。
5. 按 docs/demo.md 录手机与电脑各 3～5 分钟视频，更新验收状态后再标记题目全部完成。

## 本轮交接检查

- [x] 已完成/未完成状态与 Unity、AVD、APK 证据一致，明确保留真机兼容性与真实 AR 待验收。
- [x] 最新两个 APK 的 ABI、默认配置、签名和 SHA-256 已核对，交接副本包含校验清单。
- [x] README 与 plan 已明确 Windows/服务器分工；临时前后端检查已结束，不作为 Windows 接手要求。
- [x] 文档本地链接与版本信息检查通过，Git diff --check 无空白错误；真实服务器地址未进入本轮仓库文档。
- [x] AI_LOG 记录本次文档交接和分工修正，保持已有指令原文。
- [ ] 真机、仓库 Unity 副本全新导入、两段视频和服务端后续验收由接手者继续。
