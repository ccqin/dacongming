# 追影 (Zhuiying) 接口文档

> **最后更新:** 2026-05-29
> **状态:** 双机部署运行中

---

## 1. 前端统一入口 (MainSite)

前端仅与 MainSite 交互，MainSite 负责聚合数据并转发请求。
**Base URL:** `http://zhuiying.19856789.xyz:8980` (或 IP 直连)

###  影视列表接口

前端调用以下接口获取分类数据，MainSite 会自动处理鉴权并转发给 Hub。

| 分类参数 (`category`) | MainSite 路由 | 转发至 Hub 路径 | 说明 |
| :--- | :--- | :--- | :--- |
| **热门** | `GET /api/movies?category=trending` | `/api/movie/trending` | 获取近期热门影视 |
| **最新** | `GET /api/movies?category=upcoming` | `/api/movie/latest` | 获取最新上映/即将上映 |
| **高分** | `GET /api/movies?category=top_rated` | `/api/movie/latest` | *注：Hub 暂未开放 top_rated，暂用 latest 替代* |

**返回格式 (MainSite 处理后):**
```json
{
  "page": 1,
  "results": [
    {
      "tmdbId": 12345,
      "title": "电影名称",
      "posterPath": "/path/to/poster.jpg",
      "mediaType": "movie",
      "releaseDate": "2026-01-01",
      "voteAverage": 8.5
    }
  ]
}
```

### 🔍 聚合搜索

| 接口 | 方法 | 说明 |
| :--- | :--- | :--- |
| `/api/hub/search?q=关键词` | GET | 调用 Hub 进行网盘资源聚合搜索 |

---

## 2. 数据中心 (Hub)

供 MainSite 调用的底层数据服务。
**Base URL:** `https://zhuiyinghub.19856789.xyz`

###  TMDB 影视数据 (新路由)

| 接口路径 | 方法 | 说明 | 参数示例 |
| :--- | :--- | :--- | :--- |
| `/api/movie/trending` | GET | 热门榜单 | `?language=zh-CN` |
| `/api/movie/latest` | GET | 最新榜单 | `?language=zh-CN` |

**返回格式 (Hub 原始格式):**
```json
{
  "success": true,
  "data": [ ... ] 
}
```

### 🛠 系统接口

| 接口 | 方法 | 说明 |
| :--- | :--- | :--- |
| `/health` | GET | 健康检查 |
| `/api/admin/sources` | GET/POST/DELETE | 数据源管理 (需鉴权) |

---

## ⚠️ 注意事项

1. **路由变更**: Hub 的路由已从 `/api/tmdb/...` 迁移至 `/api/movie/...`。
2. **MainSite 缓存**: MainSite 不缓存 TMDB 原始数据，依赖 Hub 的缓存机制。
3. **跨域**: MainSite 已配置 CORS，允许前端跨域访问。

