# Unity / 服务端接口对照

基准：`origin/unity-android-validation`，提交 `4017994`。Unity 工程的 35 个文件已同步到当前工作区，后端与前端改造保留其问题协议。

## 对照结论

| Unity 行为 | 服务端对应行为 |
| --- | --- |
| 默认/自定义地址，向 baseUrl + /issues 提交 | 公网 baseUrl 使用 `http://${HOST}:${PORT}/api`；Caddy 去除 /api 后转发 |
| CreateIssue 包含 title、description、priority、position.x/y/z | 字段不变，数字坐标含零值均可；新校验上限覆盖 Unity 200 字标题与 2000 字描述 |
| 仅提交问题时联网，无登录令牌 | 问题 API 按要求开放；登录只影响管理页面和账号管理 |
| JsonUtility 读取 IssueResponse.id/title/status | POST 保持 201 完整对象，不增加包装层 |
| 错误读取 error.code/message | 参数、保存及账号错误使用相同封装 |
| x86_64 模拟与 ARM64 真机使用同一问题结构 | 后端均可接收；模拟坐标不能当作真实 AR 验收 |

## 本轮完善

- JSON 全量文件读写改为 SQLite 事务保存，问题/用户/会话集中存储；旧 JSON 一次迁移，避免重启重复导入覆盖已更新状态。
- 增加实际登录与服务端权限：Admin 初始化、bcrypt、HttpOnly 会话、退出与修改密码。用户管理仅允许超级管理员，不依赖前端隐藏按钮。
- Caddy 直接提供无域名 HTTP 页面和 API 转发，避免暴露 Vite 开发服务器；API 与静态页面使用同一来源。
- 网页增加问题搜索筛选、角色操作、输入/响应检查和账号界面；请求失败保留已保存状态。
- README 改为安装、使用、配置和接口文档；具体运行证据放在 verification，Windows/APK 历史交接放在 handoff。

## 后续边界

- 问题接口不鉴权是本次明确要求，网页“普通用户只读”不限制直接调用开放 API。
- 创建问题没有幂等键，Unity 请求结果不确定时应先查询管理端，再决定重试。
- 模拟上报没有单独 source 字段，建议用明确测试标题区分；不能仅凭坐标判断是否来自真实设备。
- SQLite/WAL 备份需使用 SQLite 备份方式或停服备份；当前脚本进程不是开机自启服务。
- 本机缺少 Unity Editor；已核对同步资产和协议，未在 Linux 重新构建 APK。分支记载的两个 missing script 警告、真机兼容提示及真实 AR/视频仍待 Windows/设备侧验收。
