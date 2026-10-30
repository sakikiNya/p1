AI许愿测试

### 使用版本
go 1.22.2
react 19.2.0
ts 7.0.2

db: sqlite
Caddy

### 启动
可使用python作为启动器
```bash
# bash scripts/build-release.sh # 构建
python3 scripts/server.py start
python3 scripts/server.py status
```

### unity真机配置后端地址 
有默认配置(本项目)
可自定义http://${HOST}:${PORT}/${api}

### 完成项
必做功能均完成

### 后续优化
1. app端登陆功能 以保证来源
2. 使用postgresql替代sqlite
3. 优化UI显示 
4. 图片上传而不是仅上传坐标 使用s3