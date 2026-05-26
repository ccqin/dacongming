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
```

### 3. Docker Compose 启动
```bash
docker-compose up -d --build
```
服务启动后，访问 `http://服务器IP:8980` 即可看到主站。

## 开发配置
- **MainSite 环境变量**: `ZhuiyingHubUrl`, `ADMIN_USER`, `ADMIN_PASSWORD` 已在 `docker-compose.yml` 中注入。
- **Nginx 配置**: 位于 `nginx/default.conf`，包含域名分发规则。
- **数据持久化**: 容器内 `/app/data` 已映射，重启不丢失用户数据。

---
_由小拉 (default) 架构开发_
