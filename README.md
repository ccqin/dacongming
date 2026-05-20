# 大聪明 - 追影重构项目 (Zhuiying Rebuilt)

## 简介
**dacongming** 是追影项目的重构版本，基于 **.NET 10** 构建。
目标是将原有单体架构拆分为清晰、可扩展的微服务，支持独立开发、部署和高并发。

## 架构概览
本项目采用多服务架构，包含以下核心组件：

| 服务 | 目录 | 端口 | 说明 |
| :--- | :--- | :--- | :--- |
| **MainSite** | `src/Zhuiying.MainSite` | 5000 | 主站 API，聚合数据、管理用户、接收 TG 推送 |
| **TmdbProxy** | `src/Zhuiying.TmdbProxy` | 5001 | TMDB API 反代，内置 SQLite 缓存，降低 API 配额消耗 |
| **TgBot** | `src/Zhuiying.TgBot` | - | Telegram 机器人后台，实时处理用户指令并推送主站 |
| **Shared** | `src/Zhuiying.Shared` | - | 公共模型 (DTOs) |

## 技术栈
- **.NET 10** (C# 14)
- **ASP.NET Core** (Minimal APIs, Background Services)
- **SQLite** (数据缓存)
- **Telegram.Bot** SDK
- **Docker & Docker Compose**

## 快速开始

### 1. 克隆项目
```bash
git clone https://github.com/ccqin/dacongming.git
cd dacongming
```

### 2. 配置环境变量
在根目录下创建 `.env` 文件（可选，用于自定义配置）：
```env
# TMDB API Key (必填)
TMDB_API_KEY=your_tmdb_api_key

# Telegram Bot Token (可选，如需启动 TG Bot)
TG_BOT_TOKEN=your_telegram_bot_token

# 主站 URL (供 Bot 使用)
MAIN_SITE_URL=http://mainsite:5000
```

### 3. Docker Compose 启动
```bash
docker-compose up -d --build
```
服务启动后，可通过以下地址访问：
- **主站 API**: `http://localhost:5000`
- **TMDB 反代**: `http://localhost:5001`

## 目录结构
```text
dacongming/
├── docker-compose.yml       # 统一编排文件
├── Dockerfile               # 通用构建文件 (通过 TARGET 参数区分服务)
├── src/
│   ├── Zhuiying.TmdbProxy/  # TMDB 代理服务
│   ├── Zhuiying.MainSite/   # 主站服务
│   ├── Zhuiying.TgBot/      # TG 机器人服务
│   └── Zhuiying.Shared/     # 共享代码库
└── .gitignore
```

## 服务间通信
- **MainSite <-> TmdbProxy**: 通过 Docker 网络内部 HTTP 调用 (`http://tmdbproxy:5001`).
- **TgBot -> MainSite**: 通过 HTTP POST (`http://mainsite:5000/api/tg/messages`) 推送用户交互数据.

---
_由小拉 (default) 架构开发_
