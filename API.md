# Hub API 文档 (REST Standard)

> **服务名:** Zhuiying Hub (追影 Hub)
> **版本:** v1.0.0
> **基础 URL:** `http://zhuiyingapi.198556789.xyz` (通过 Nginx 网关) / `http://localhost:8080` (本地直连)
> **协议:** HTTPS (生产) / HTTP (开发)
> **数据格式:** JSON
> **CORS:** 已开启 (AllowAnyOrigin)

---

## 1. 统一响应格式

所有 API 返回统一使用 `ApiResponse<T>` 包装:

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

### 失败响应示例

```json
{
  "success": false,
  "data": null,
  "error": "关键词不能为空"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `success` | `boolean` | 请求是否成功 |
| `data` | `T \| null` | 业务数据，成功时返回 |
| `error` | `string \| null` | 错误信息，失败时返回 |

---

## 2. 健康检查

### `GET /api/health`

检查 Hub 服务及各搜索源的健康状态。

**请求:** 无参数

**响应 200:**

```json
{
  "success": true,
  "data": {
    "status": "ok",
    "providers": {
      "PanSou": true
    },
    "time": "2026-05-29T10:00:00Z"
  },
  "error": null
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `status` | `string` | 服务状态，固定 `"ok"` |
| `providers` | `object` | 各搜索源可用性 `{ providerName: boolean }` |
| `time` | `string` | 服务器当前时间 (UTC, ISO 8601) |

---

## 3. 影视数据 (Movies)

### 3.1 获取热门影视

`GET /api/movie/trending`

**Query 参数:**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `type` | `string` | 否 | `"movie"` | 类型: `"movie"` / `"tv"` |
| `region` | `string` | 否 | `null` | 地区筛选 (如 `"CN"`) |
| `page` | `int` | 否 | `1` | 页码 |

**响应 200:**

```json
{
  "success": true,
  "data": [
    {
      "tmdbId": 12345,
      "title": "流浪地球",
      "posterPath": "/xxxxx.jpg",
      "tmdbVoteAverage": 8.5,
      "mediaType": "movie",
      "releaseDate": "2023-01-22",
      "overview": "故事简介..."
    }
  ],
  "error": null
}
```

### 3.2 获取最新影视

`GET /api/movie/latest`

**Query 参数:**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `type` | `string` | 否 | `"movie"` | 类型: `"movie"` / `"tv"` |
| `page` | `int` | 否 | `1` | 页码 |

**响应 200:** 数据结构同「热门影视」。

### 3.3 获取影视详情

`GET /api/movie/{id}`

**Path 参数:**

| 参数 | 类型 | 说明 |
|------|------|------|
| `id` | `int` | TMDB 影视 ID |

**Query 参数:**

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|------|------|------|--------|------|
| `type` | `string` | 否 | `"movie"` | 类型: `"movie"` / `"tv"` |

**响应 200:**

```json
{
  "success": true,
  "data": {
    "tmdbId": 12345,
    "title": "流浪地球",
    "originalTitle": "The Wandering Earth",
    "overview": "故事简介...",
    "posterPath": "/xxxxx.jpg",
    "backdropPath": "/yyyyy.jpg",
    "tmdbVoteAverage": 8.5,
    "tmdbVoteCount": 5000,
    "mediaType": "movie",
    "originalLanguage": "zh",
    "releaseDate": "2023-01-22",
    "runtime": 125,
    "genres": "[{\"id\": 1, \"name\": \"科幻\"}]",
    "productionCountries": "[{\"iso_3166_1\": \"CN\", \"name\": \"中国\"}]",
    "credits": "{...}",
    "similar": "{...}",
    "videos": "{...}",
    "douban": {
      "doubanId": "3456789",
      "title": "流浪地球",
      "rating": 8.3,
      "ratingCount": 120000,
      "summary": "豆瓣简介...",
      "imageUrl": "https://img.doubanio.com/..."
    }
  },
  "error": null
}
```

> **说明:** 首次请求时会自动搜索并缓存豆瓣数据，后续请求直接返回缓存。

### 3.4 获取豆瓣数据

`GET /api/movie/douban/{doubanId}`

**Path 参数:**

| 参数 | 类型 | 说明 |
|------|------|------|
| `doubanId` | `string` | 豆瓣 ID |

**响应 200:**

```json
{
  "success": true,
  "data": {
    "doubanId": "3456789",
    "title": "流浪地球",
    "rating": 8.3,
    "ratingCount": 120000,
    "summary": "豆瓣简介...",
    "imageUrl": "https://img.doubanio.com/..."
  },
  "error": null
}
```

**响应 404:**

```json
{
  "success": false,
  "data": null,
  "error": "Douban data not found"
}
```

---

## 4. 资源搜索 (Search)

### `POST /api/search`

根据关键词搜索网盘资源链接。支持缓存（默认 1 小时）。

**请求体:**

```json
{
  "keyword": "流浪地球",
  "cloudTypes": ["baidu", "quark"],
  "forceRefresh": false
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `keyword` | `string` | 是 | 搜索关键词 (影视名称) |
| `cloudTypes` | `string[]` | 否 | 网盘类型过滤: `"baidu"`, `"aliyun"`, `"quark"`, 等 |
| `forceRefresh` | `boolean` | 否 | 强制刷新缓存 (默认 `false`) |

**响应 200:**

```json
{
  "success": true,
  "data": [
    {
      "url": "https://pan.baidu.com/s/xxxxx",
      "password": "abcd",
      "cloudType": "baidu",
      "note": "1080P 中字",
      "source": "PanSou",
      "dateTime": "2026-05-28T12:00:00"
    }
  ],
  "error": null
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `url` | `string` | 网盘分享链接 |
| `password` | `string` | 提取码 (空字符串表示无密码) |
| `cloudType` | `string` | 网盘类型: `"baidu"`, `"aliyun"`, `"quark"`, 等 |
| `note` | `string` | 备注信息 (画质、字幕等) |
| `source` | `string` | 数据来源 (搜索源名称) |
| `dateTime` | `string \| null` | 资源发布时间 (ISO 8601) |

**响应 400:**

```json
{
  "success": false,
  "data": null,
  "error": "关键词不能为空"
}
```

---

## 5. 订阅 (Subscriptions)

### 5.1 创建订阅

`POST /api/subscribe`

创建影视资源订阅，当有新资源时自动推送。

**请求体:**

```json
{
  "userId": "user_001",
  "movieId": 12345,
  "keyword": "流浪地球2"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `userId` | `string` | 是 | 用户唯一标识 |
| `movieId` | `int` | 是 | TMDB 影视 ID |
| `keyword` | `string` | 否 | 自定义搜索关键词 (默认使用影视标题) |

**响应 200:**

```json
{
  "success": true,
  "data": {
    "id": 1,
    "movieId": 12345,
    "keyword": "流浪地球2",
    "status": "pending",
    "searchResult": null,
    "createdAt": "2026-05-29T10:00:00Z",
    "foundAt": null
  },
  "error": null
}
```

> **说明:** 如果同一用户已对同一影视存在未取消的订阅，直接返回已有订阅信息。

### 5.2 获取用户订阅列表

`GET /api/subscribe/{userId}`

**Path 参数:**

| 参数 | 类型 | 说明 |
|------|------|------|
| `userId` | `string` | 用户唯一标识 |

**响应 200:**

```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "movieId": 12345,
      "keyword": "流浪地球2",
      "status": "pending",
      "searchResult": null,
      "createdAt": "2026-05-29T10:00:00Z",
      "foundAt": null
    },
    {
      "id": 2,
      "movieId": 67890,
      "keyword": "满江红",
      "status": "found",
      "searchResult": "[{\"url\": \"https://pan.baidu.com/s/yyy\", ...}]",
      "createdAt": "2026-05-28T08:00:00Z",
      "foundAt": "2026-05-28T14:00:00Z"
    }
  ],
  "error": null
}
```

**订阅状态说明:**

| 状态 | 说明 |
|------|------|
| `pending` | 等待匹配资源 |
| `found` | 已找到匹配资源 (`searchResult` 包含结果) |
| `cancelled` | 已取消 (查询时自动过滤) |

### 5.3 取消订阅

`DELETE /api/subscribe/{id}`

**Path 参数:**

| 参数 | 类型 | 说明 |
|------|------|------|
| `id` | `int` | 订阅记录 ID |

**Query 参数:**

| 参数 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `userId` | `string` | 是 | 用户唯一标识 (用于权限校验) |

**响应 200:**

```json
{
  "success": true,
  "data": "已取消",
  "error": null
}
```

**响应 404:**

```json
{
  "success": false,
  "data": null,
  "error": "订阅不存在"
}
```

---

## 6. API 端点汇总

| 方法 | 路径 | 说明 | 认证 |
|------|------|------|------|
| `GET` | `/api/health` | 健康检查 | 无 |
| `GET` | `/api/movie/trending` | 获取热门影视 | 无 |
| `GET` | `/api/movie/latest` | 获取最新影视 | 无 |
| `GET` | `/api/movie/{id}` | 获取影视详情 | 无 |
| `GET` | `/api/movie/douban/{doubanId}` | 获取豆瓣数据 | 无 |
| `POST` | `/api/search` | 搜索网盘资源 | 无 |
| `POST` | `/api/subscribe` | 创建订阅 | 无 |
| `GET` | `/api/subscribe/{userId}` | 获取用户订阅列表 | 无 |
| `DELETE` | `/api/subscribe/{id}` | 取消订阅 | 无 |

---

## 7. HTTP 状态码

| 状态码 | 含义 |
|--------|------|
| `200` | 请求成功 |
| `400` | 请求参数错误 |
| `404` | 资源不存在 |
| `500` | 服务器内部错误 |

---

## 8. 技术栈

| 组件 | 技术 |
|------|------|
| 框架 | .NET 10 (ASP.NET Core) |
| 数据库 | SQLite |
| 外部服务 | TMDB API、豆瓣搜索、PanSou |
| 部署 | Docker |
| 网关 | Nginx |

---

## 9. MainSite 代理层 (前端统一入口)

前端**仅与 MainSite 交互**，MainSite 负责转发请求到 Hub 并转换数据格式。

**Base URL:** `http://117.50.201.162:8980` (IP 直连)

### 9.1 MainSite 路由映射表

| 前端调用 (GET) | 转发至 Hub | 说明 |
|----------------|------------|------|
| `GET /api/movies?category=trending&type=movie&page=N` | `GET /api/movie/trending?type=movie&page=N` | 热门电影 |
| `GET /api/movies?category=trending&type=tv&page=N` | `GET /api/movie/trending?type=tv&page=N` | 热门剧集 |
| `GET /api/movies?category=upcoming&type=movie&page=N` | `GET /api/movie/latest?type=movie&page=N` | 最新电影 |
| `GET /api/movies?category=latest&type=movie&page=N` | `GET /api/movie/latest?type=movie&page=N` | 最新电影 (别名) |
| `GET /api/movies/{tmdbId}?type=movie` | `GET /api/movie/{tmdbId}?type=movie` | 电影详情 |
| `POST /api/search` | `POST /api/search` | 搜索网盘资源 |
| `GET /api/search?q=keyword` | `POST /api/search` (自动转) | 搜索 (GET 兼容) |
| `POST /api/subscribe` | `POST /api/subscribe` | 创建订阅 |
| `GET /api/subscribe/{userId}` | `GET /api/subscribe/{userId}` | 获取订阅列表 |
| `DELETE /api/subscribe/{id}?userId=xxx` | `DELETE /api/subscribe/{id}?userId=xxx` | 取消订阅 |

### 9.2 数据格式转换

Hub 返回格式:
```json
{"success": true, "data": [...], "error": null}
```

MainSite 自动转换为前端期望的 TMDB 风格格式:
```json
{"page": 1, "results": [...]}
```

### 9.3 前端新增功能

- **搜索框**: 顶部导航栏新增搜索输入框，支持 POST 搜索网盘资源
- **详情弹窗**: 点击任意影视卡片，调用详情接口展示完整信息（简介、评分、演员、豆瓣数据）
- **资源搜索按钮**: 详情页内可一键搜索该影视的网盘资源
- **加载骨架屏**: 数据加载前显示占位动画，避免空白页面
