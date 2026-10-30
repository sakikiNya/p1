# HTTP 部署

## 网络结构

```text
浏览器 / Unity → http://${HOST}:${PORT}
                     Caddy
                     ├── /api/* → Go（回环监听，移除 /api 前缀）→ SQLite
                     └── /*     → React 构建产物
```

当前不使用域名和 TLS。Caddyfile 显式设置 HTTP 地址并关闭自动 HTTPS；`admin off` 避免占用系统其他 Caddy 的管理端口。此配置作为独立进程运行，不直接覆盖系统 Caddyfile。

## 运行

1. 安装 Caddy 2、Go、Node.js/npm。
2. 在项目根目录复制 `deploy/.env.example` 为 `deploy/.env`，填写端口、数据路径及 `ADMIN_PASSWORD`。`API_UPSTREAM` 与 `LISTEN_ADDR` 必须一致，Go 使用回环地址，Caddy 使用对外网卡。
3. 执行 `bash scripts/build-release.sh`，再执行 `python3 scripts/server.py start`。
4. 打开 `http://${HOST}:${PORT}/api/health` 和首页。通过 Admin 登录并创建所需账号。

```bash
python3 scripts/server.py status
python3 scripts/server.py restart
python3 scripts/server.py stop
```

脚本读取 `.env` 但不展开 shell 变量。首次初始化失败时不会设置一个公开的默认密码。更新 `.env` 后通过 restart 生效；已有 Admin 的密码只能通过登录后的修改密码流程更换。

- 数据库：由 DB_FILE 指定，不随构建产物覆盖。
- 日志：`.cache/server/server.log`、`.cache/server/caddy.log`。
- PID：`.cache/server/server.pid`、`.cache/server/caddy.pid`。
- 配置校验：在相同环境变量下执行 `caddy validate --config deploy/Caddyfile`；启动脚本会自动校验。

如从旧直连 Go 部署切换，先停止原项目进程以释放对外端口，再启动新配置。旧 JSON 路径放入 LEGACY_JSON_FILE，一次迁移后原文件保留。不要让旧 JSON 服务和新 SQLite 服务同时接收现场上报。

## 长期运行

仓库中的 `inspection.service` 和 `inspection-caddy.service` 为 Linux systemd 模板，默认目录 `/srv/inspection`，需按实际目录、运行用户和 Caddy 路径调整。API 的 `backend.env` 使用 `backend.env.example`；Caddy 的 `caddy.env` 填写 PORT、BIND_HOST、API_UPSTREAM、FRONTEND_DIR。Caddy 的静态资源目录使用绝对路径。

安装前确认现有站点和端口。运行用户需能写数据库目录，Caddy 需能读取前端目录。API 服务重启不会改变 SQLite 数据或有效会话。普通后台脚本和 systemd 二选一管理同一实例，避免端口冲突。

HTTP 的 COOKIE_SECURE=false；以后启用 HTTPS 时设为 true，并调整 Caddy 地址。用户管理有后端权限校验，问题 API 按需求开放。
