# 追影项目部署配置记录

## 项目位置
`/opt/codex/dacongming`

## 服务配置

### MainSite（主站）
- **容器名称**: `zhuiying-mainsite`
- **端口映射**: `5000:80`
- **访问地址**: 
  - 本地测试: `http://localhost:5000`
  - 生产环境: 通过 1panel 配置反向代理
- **Hub 地址**: `https://zhuiyinghub.19856789.xyz`（外部域名）
- **配置文件**: `/opt/codex/dacongming/.env` 中的 `HUB_URL`

### Hub API（数据服务）
- **容器名称**: `zhuiying-hub`
- **内部端口**: `5002`
- **端口映射**: 已映射到宿主机（通过 1panel）
- **外部域名**: `https://zhuiyinghub.19856789.xyz`
- **API 端点示例**:
  - `/api/tmdb/trending` - 获取热门影视
  - `/api/tmdb/search?q=关键词` - 搜索影视
  - `/api/tmdb/movie/{id}` - 获取电影详情
  - `/api/tmdb/tv/{id}` - 获取电视剧详情
  - `/health` - 健康检查

### TgBot（Telegram 机器人）
- **容器名称**: `zhuiying-tgbot`
- **状态**: 已停止（未配置 Bot Token）
- **配置**: 需要在 `.env` 中设置 `TG_BOT_TOKEN`

## 环境变量配置

配置文件: `/opt/codex/dacongming/.env`

```bash
# TMDB API Key
TMDB_API_KEY=fc31a8b1c5feb49758adf32783392127

# Telegram Bot Token（可选）
TG_BOT_TOKEN=

# Hub API URL
HUB_URL=https://zhuiyinghub.19856789.xyz

# Admin credentials（可选）
ADMIN_USER=
ADMIN_PASSWORD=

# Main Site URL（内部 Docker 网络）
MAIN_SITE_URL=http://mainsite:5000
```

## Docker 网络

- **网络名称**: `dacongming`
- **类型**: 自定义桥接网络
- **容器间通信**: 使用容器名称（如 `zhuiying-hub:5002`）

## 部署流程

### 1. 本地编译 MainSite
```bash
export PATH="/usr/share/dotnet:$PATH"
cd /opt/codex/dacongming
dotnet publish src/Zhuiying.MainSite/Zhuiying.MainSite.csproj -c Release -o publish/release
```

### 2. 拷贝编译产物
```bash
mkdir -p publish/wwwroot
cp -r publish/release/wwwroot/* publish/wwwroot/
```

### 3. 构建 Docker 镜像
```bash
docker compose build mainsite
docker compose build zhuiyinghub tgbot
```

### 4. 启动服务
```bash
docker compose up -d mainsite zhuiyinghub
```

### 5. 验证部署
```bash
# 检查容器状态
docker ps

# 检查 MainSite 配置
docker exec zhuiying-mainsite cat /usr/share/nginx/html/appsettings.json

# 测试 Hub API
curl http://localhost:5002/health
```

## 1panel 配置

### Hub API 反向代理
- **域名**: `zhuiyinghub.19856789.xyz`
- **反向代理目标**: Hub 容器端口 5002
- **SSL**: 通过 Cloudflare 或 Let's Encrypt 配置

### MainSite 反向代理（可选）
- **域名**: 自定义域名
- **反向代理目标**: `http://127.0.0.1:5000`

## 注意事项

1. **MainSite 必须在本地编译**：Docker 内编译会导致 Blazor WASM 文件 hash 不一致，SRI 校验失败
2. **Hub 地址配置**：
   - 内部部署使用 `http://zhuiying-hub:5002`（Docker 内部网络）
   - 外部部署使用 `https://zhuiyinghub.19856789.xyz`（外部域名）
3. **环境变量替换**：MainSite 容器启动时会自动将 `appsettings.json` 中的 `${HUB_URL}` 替换为实际值
4. **缓存清理**：容器启动时会自动给 `_framework` 文件加时间戳版本号，防止浏览器缓存

## 常用命令

```bash
# 查看日志
docker logs zhuiying-mainsite
docker logs zhuiying-hub

# 重启服务
docker compose restart mainsite
docker compose restart zhuiyinghub

# 停止服务
docker compose down

# 重新构建并启动
docker compose build mainsite && docker compose up -d mainsite
```
