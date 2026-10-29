# 当前服务器部署：HTTP ${PORT}

本项目在当前服务器 `/home/skk/go_project/p1` 中运行。Go 同时提供构建后的 React 页面和 `/api/*` 接口，监听 `0.0.0.0:${PORT}`，暂不使用域名和 TLS。

`${HOST}` 为服务器公网 IP 或域名，`${PORT}` 为监听端口（以部署配置为准）。将下列占位符替换为实际值后访问；Unity 输入框需填写替换后的完整地址。

- 管理页面：`http://${HOST}:${PORT}/`
- 健康检查：`http://${HOST}:${PORT}/api/health`
- Unity 后端地址：`http://${HOST}:${PORT}/api`

`0.0.0.0` 表示监听所有 IPv4 网卡；它不是应填入手机的服务器地址。

## 配置与运维

实际配置文件 `deploy/.env` 已创建并被 Git 忽略，模板见 `.env.example`。管理脚本直接解析键值，不执行文件中的 shell 命令。当前 DATA_FILE 固定到 `backend/data/issues.json`；FRONTEND_DIR 指向构建产物目录。

```bash
cd /home/skk/go_project/p1
bash scripts/build-release.sh
python3 scripts/server.py start
python3 scripts/server.py status
# 更新源码并重新构建后，通过 restart 运行新产物
python3 scripts/server.py restart
# 暂时关闭
python3 scripts/server.py stop
```

脚本只管理本项目运行目录中的二进制进程，日志 `.cache/server/server.log`，PID `.cache/server/server.pid`。每次启动复制发布二进制到独立运行目录，构建不会覆盖正在运行的二进制。数据文件不属于发布产物，重启保留。

这是普通后台进程，**尚未配置开机自启**。在普通服务器终端执行上述命令即可；Codex 沙箱会回收后台子进程，需在沙箱外启动才能持续服务。

`FRONTEND_DIR` 非空时管理页面位于 `/`，API 使用 `/api/health`、`/api/issues` 等地址。同域网页无需额外 CORS 配置；开发期直接跨域访问时在 CORS_ORIGINS 中加入前端实际来源。

当前本机、内网验证已通过，用户也已实际验证公网地址可访问。UFW 为 inactive；服务器自身回访公网 IP 曾超时，外部访问结果以用户验证为准。

公网访问还取决于主机防火墙和云安全组是否允许 TCP ${PORT}；本项目启动脚本不自动修改全局防火墙。当前使用临时 HTTP，Unity 的 Development APK 已配置允许 HTTP；非开发构建需调整平台配置或后续使用 HTTPS。

## 后续域名方案

`Caddyfile`、`inspection.service` 和 `backend.env.example` 保留为后续域名 + HTTPS + systemd 部署模板，当前运行不使用它们。启用时须按实际目录调整：原模板默认 `/srv/inspection`。Go 改为回环监听，由 Caddy 托管页面并转发 `/api/*`；不要覆盖已有站点或删除数据目录。
