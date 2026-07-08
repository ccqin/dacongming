# 大聪明 - 追影重构项目 (Zhuiying Rebuilt)

## 简介
**dacongming** 是追影项目的重构版本，基于 **.NET 10** 构建。
目标是将原有单体架构拆分为清晰、可扩展的微服务，支持独立开发、部署和高并发。

## 架构概览
本项目采用多服务架构，包含以下核心组件：

| 服务 | 目录 | 端口 | 说明 |
| :--- | :--- | :--- | :--- |
| **Nginx** | `nginx/` | **8980** | 统一网关，按域名分发流量 |
| **MainSite** | `src/Zhuiying.MainSite` | 5000 | 主站 API + 前端 SPA，聚合数据、管理用户 |
| **Unified Hub** | `src/Zhuiying.Hub` | 5002 | 统一数据 Hub (TMDB + PanSou)，合并旧服务 |
| **TgBot** | `src/Zhuiying.TgBot` | - | Telegram 机器人后台 |
| **Shared** | `src/Zhuiying.Shared` | - | 公共模型 (DTOs) |

## 访问入口
> **主站 URL**: `http://zhuiying.19856789.xyz:8980`
>
> **Hub URL**: `http://zhuiyinghub.19856789.xyz:8980` (内网/调试用)
>
> *注：防火墙已开放 8980 端口。*

## 技术栈
- **.NET 10** (C# 14)
- **ASP.NET Core** (Minimal APIs, Background Services)
- **Blazor WebAssembly** (前端 SPA)
- **MudBlazor 7.15.0** (UI 组件库)
- **SQLite** (数据缓存/用户系统)
- **Docker & Docker Compose**

## 快速开始

### 1. 克隆项目
```bash
git clone https://github.com/ccqin/dacongming.git
cd dacongming
```

### 2. 配置环境变量
在根目录下创建 `.env` 文件：
```env
# TMDB API Key (必填)
TMDB_API_KEY=your_tmdb_api_key

# Telegram Bot Token (可选)
TG_BOT_TOKEN=your_telegram_bot_token

# Hub API URL (本地开发)
HUB_URL=https://zhuiyinghub.19856789.xyz
```

### 3. Docker Compose 启动
```bash
docker-compose up -d --build
```
服务启动后，访问 `http://服务器IP:8980` 即可看到主站。

### 4. 本地开发
```bash
cd src/Zhuiying.MainSite
dotnet run
```
本地开发服务器运行在 `http://localhost:5217`，需要配置 `appsettings.Development.json` 文件。

## 开发配置
- **MainSite 环境变量**: `ZhuiyingHubUrl`, `ADMIN_USER`, `ADMIN_PASSWORD` 已在 `docker-compose.yml` 中注入。
- **Nginx 配置**: 位于 `nginx/default.conf`，包含域名分发规则。
- **数据持久化**: 容器内 `/app/data` 已映射，重启不丢失用户数据。
- **本地开发配置**: `src/Zhuiying.MainSite/wwwroot/appsettings.Development.json` 用于覆盖 Docker 环境变量。

## 最新功能 (2026-07-08)

### 1. 详情页信息展示增强 ✅
- **演员列表**：显示主要演员头像、姓名和角色
- **相似推荐**：展示同类型影视作品卡片
- **预告片视频**：支持 YouTube 嵌入播放
- **Hub API 优化**：并行获取 credits/similar/videos 数据

### 2. 影视筛选功能 ✅
- **多条件筛选**：支持按类型(电影/电视剧)、年份、评分、地区筛选
- **Browse 页面**：`/browse` 路由，响应式网格布局
- **动态加载**：支持分页加载，滚动加载更多内容

### 3. 图片懒加载优化 ✅
- **LazyImage 组件**：`Components/LazyImage.razor` 封装懒加载逻辑
- **HTML5 原生懒加载**：使用 `loading="lazy"` 属性
- **全局应用**：MovieCard、详情页、搜索结果均已替换

### 4. 前端数据缓存 ✅
- **CacheService**：基于 localStorage 的浏览器端缓存服务
- **2小时过期**：默认缓存有效期 2 小时
- **缓存策略**：
  - 热门列表：`trending_{type}_{region}_{page}`
  - 最新列表：`latest_{type}_{page}`
  - 影视详情：`movie_{id}_{type}`
  - 搜索结果：`search_{keyword}_{type}_{page}`
- **自动失效**：过期自动清理，支持手动清除

## 项目结构
```
src/
├── Zhuiying.MainSite/          # 主站 (Blazor WASM)
│   ├── Components/             # 可复用组件
│   │   ├── LazyImage.razor    # 懒加载图片组件
│   │   └── MovieCard.razor    # 影视卡片组件
│   ├── Pages/                  # 页面
│   │   ├── Home.razor         # 首页
│   │   ├── Browse.razor       # 筛选页面
│   │   ├── MovieDetail.razor  # 详情页
│   │   └── Search.razor       # 搜索页
│   ├── Services/               # 服务层
│   │   ├── MovieService.cs    # 影视数据服务 (带缓存)
│   │   ├── SearchService.cs   # 搜索服务 (带缓存)
│   │   └── CacheService.cs    # 缓存服务
│   └── wwwroot/                # 静态资源
│       └── appsettings.Development.json  # 本地开发配置
├── Zhuiying.Hub/               # Hub API (TMDB 代理)
├── Zhuiying.TgBot/             # Telegram 机器人
└── Zhuiying.Shared/            # 共享模型
```

---
_由小拉 (default) 架构开发_
