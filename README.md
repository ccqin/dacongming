# 大聪明 - 追影重构项目 (Zhuiying Rebuilt)

## 简介
**dacongming** 是追影项目的重构版本，基于 **.NET 10** 构建。
目标是将原有单体架构拆分为清晰、可扩展的微服务，支持独立开发、部署和高并发。

## 架构概览
本项目采用多服务架构，包含以下核心组件：

| 服务 | 目录 | 端口 | 说明 |
| :--- | :--- | :--- | :--- |
| **Nginx** | `nginx/` | **80/443** | 统一网关 (Cloudflare SSL) |
| **MainSite** | `src/Zhuiying.MainSite` | 5000 | 主站前端 (Blazor WASM SPA) |
| **Unified Hub** | `src/Zhuiying.Hub` | 5002 | 统一数据 Hub (TMDB + PanSou) |
| **TgBot** | `src/Zhuiying.TgBot` | - | Telegram 机器人后台 |
| **Shared** | `src/Zhuiying.Shared` | - | 公共模型 (DTOs) |

所有容器运行在统一 Docker 网络 `dacongming` 中。

## 访问入口
> **主站**: `https://zhuiying.19856789.xyz`
> **Hub API**: `https://zhuiyinghub.19856789.xyz`

## 技术栈
- **.NET 10** (C# 14)
- **ASP.NET Core** (Minimal APIs, Background Services)
- **Blazor WebAssembly** (前端 SPA)
- **MudBlazor 7.15.0** (UI 组件库)
- **SQLite** (数据缓存/用户系统)
- **Docker & Docker Compose**
- **Cloudflare** (CDN + SSL)

## ⚠️ 部署注意事项（重要）

**MainSite 必须在本地编译后再打包到 Docker，不能在 Docker 内编译！**

原因：Docker SDK 版本与本地 SDK 不同会导致 Blazor WASM 文件 hash 不一致，`dotnet.js` 中硬编码的 WASM 文件名与实际文件不匹配，导致浏览器 SRI 校验失败、全部 404。

### 部署流程
```bash
# 一键部署（本地编译 → Docker 打包 → 重启）
bash build-mainsite.sh
```

或手动步骤：
```bash
# 1. 本地编译
dotnet publish src/Zhuiying.MainSite/Zhuiying.MainSite.csproj -c Release -o publish/release

# 2. 拷贝到 Docker 构建目录
rm -rf publish/wwwroot && cp -r publish/release/wwwroot publish/wwwroot

# 3. 重建并重启
docker compose build mainsite
docker compose up -d mainsite
```

> 部署后 `docker-entrypoint.sh` 会自动给 `_framework` 文件加时间戳版本号，防止 Cloudflare/浏览器缓存旧文件。

## 快速开始

### 1. 克隆项目
```bash
git clone https://github.com/ccqin/dacongming.git
cd dacongming
```

### 2. 配置环境变量
在根目录下创建 `.env` 文件：
```env
TMDB_API_KEY=your_tmdb_api_key
TG_BOT_TOKEN=your_telegram_bot_token  # 可选
HUB_URL=https://zhuiyinghub.19856789.xyz
```

### 3. Docker Compose 启动
```bash
docker-compose up -d --build
```

### 4. 本地开发
```bash
cd src/Zhuiying.MainSite
dotnet run
```
本地开发服务器运行在 `http://localhost:5217`。

## 前端路由

| 路由 | 页面 | 说明 |
|------|------|------|
| `/` | Home | 首页（Hero Banner + 热门/最新） |
| `/movies` | Movies | 电影列表 |
| `/tv` | TV | 电视剧列表 |
| `/browse` | Browse | 筛选（流派/年份/评分 + 分页） |
| `/search/{Keyword}` | Search | 搜索结果 |
| `/movie/{Type}/{Id}` | MovieDetail | 影视详情（演员/相似/预告片） |

## 最新功能

### 详情页增强 ✅
- 演员列表：显示主要演员头像、姓名和角色
- 相似推荐：展示同类型影视作品卡片
- 预告片视频：支持 YouTube 嵌入播放
- Hub API 并行获取 credits/similar/videos 数据

### 影视筛选 ✅
- 接入 TMDB discover API，服务端筛选
- 支持按类型、流派、年份、评分筛选
- 分页加载

### 缓存优化 ✅
- 前端 CacheService：localStorage 2h TTL
- nginx `_framework/` 文件 `no-cache` 防止浏览器缓存
- Hub API SQLite 缓存（热门 30min / 详情 24h）

### 电影/电视剧独立列表页 ✅
- `/movies` 和 `/tv` 独立页面
- 支持"加载更多"分页

## 项目结构
```
src/
├── Zhuiying.MainSite/          # 主站 (Blazor WASM)
│   ├── Components/             # 可复用组件
│   │   ├── LazyImage.razor    # 懒加载图片组件
│   │   └── MovieCard.razor    # 影视卡片组件
│   ├── Layout/                 # 布局
│   │   ├── Header.razor       # 顶部导航 + 搜索
│   │   ├── Footer.razor       # 页脚
│   │   └── MainLayout.razor   # 主布局
│   ├── Pages/                  # 页面
│   │   ├── Home.razor         # 首页
│   │   ├── Movies.razor       # 电影列表
│   │   ├── TV.razor           # 电视剧列表
│   │   ├── Browse.razor       # 筛选页面
│   │   ├── MovieDetail.razor  # 详情页
│   │   └── Search.razor       # 搜索页
│   ├── Services/               # 服务层
│   │   ├── MovieService.cs    # 影视数据服务
│   │   ├── SearchService.cs   # 搜索服务
│   │   └── CacheService.cs    # 缓存服务
│   ── Dockerfile             # 专用 Dockerfile（本地编译产物）
├── Zhuiying.Hub/               # Hub API (TMDB 代理)
├── Zhuiying.TgBot/             # Telegram 机器人
└── Zhuiying.Shared/            # 共享模型
```

## 开发配置
- **MainSite 环境变量**: `HUB_URL` 已在 `docker-compose.yml` 中注入
- **Nginx 配置**: `nginx/default.conf`（外部网关），`src/Zhuiying.MainSite/nginx.conf`（容器内）
- **数据持久化**: Hub 容器内 `/app/data` 映射为 volume

---
_由小拉 (default) 架构开发_
