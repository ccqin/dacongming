# 追影重构项目 (大聪明 / dacongming)

## 项目定位
- **追影 (zhuiying) 全新重构版**
- 目标：将原有单体架构拆分为更清晰的 .NET 10 微服务

## 架构概览
本项目使用一个 Git 仓库，包含多个 .NET 服务，通过 Docker Compose 统一编排。

| 服务 | 端口 | 职责 | 负责 Agent |
|------|------|------|------------|
| `tmdbproxy` | 5001 | TMDB API 反代 + SQLite 缓存 | `default` (我) |
| `mainsite` | 5000 | 追影主站 API (接收 TG 推送、提供影视数据) | 待分配 |
| `tgbot` | - | Telegram 机器人，接收消息并推送给 `mainsite` | `default` (我) |
| `shared` | - | 共享模型 (`TgMessageDto`, `MovieDto` 等) | 共用 |

## 服务间通信
- **TgBot -> MainSite**: HTTP POST `/api/tg/messages` (推送用户消息)
- **MainSite -> TmdbProxy**: HTTP GET `http://tmdbproxy:5001/api/*` (获取影视数据)
- 所有服务运行在同一 Docker 网络 `zhuiying-net`，通过容器名互访。

## 目录结构
```
dacongming/
├── docker-compose.yml
├── Dockerfile (通用，通过 BUILD ARG 区分服务)
├── src/
│   ├── Zhuiying.TmdbProxy/  (TMDB 反代)
│   ├── Zhuiying.MainSite/   (主站)
│   ├── Zhuiying.TgBot/      (TG 机器人)
│   └── Zhuiying.Shared/     (共享模型)
└── .gitignore
```

## 快速启动
```bash
# 1. 配置环境变量
export TMDB_API_KEY="your_tmdb_key"
export TG_BOT_TOKEN="your_bot_token"

# 2. 启动
docker-compose up -d --build
```

## 待确认
- 主站的具体业务逻辑 (UI? 数据库?)
- GitHub 仓库地址 (用于代码推送)
