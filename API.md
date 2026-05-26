# Zhuiying.Hub API 接口文档

> **Base URL:** `https://zhuiyinghub.19856789.xyz`
> **状态:** 在线运行

## 1. 影视数据 (TMDB 代理)

统一代理 TMDB 接口，解决跨域及网络访问问题。

| 接口路径 | 方法 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| `/api/tmdb/trending` | GET | 获取近期热门影视 (Trending) | [查看](https://zhuiyinghub.19856789.xyz/api/tmdb/trending) |
| `/api/tmdb/search` | GET | 搜索影视资源 | `/api/tmdb/search?query=流浪地球` |
| `/api/tmdb/discover` | GET | 发现/筛选影视 | `/api/tmdb/discover?with_genres=28` |
| `/api/tmdb/movie/{id}` | GET | 获取电影详情 | `/api/tmdb/movie/687163` |
| `/api/tmdb/tv/{id}` | GET | 获取电视剧详情 | `/api/tmdb/tv/76479` |

## 2. 聚合搜索 (PanSou / Hub)

搜索网盘资源，数据已做扁平化处理，适合前端直接渲染。

| 接口路径 | 方法 | 说明 | 示例 |
| :--- | :--- | :--- | :--- |
| `/api/hub/search` | GET | 全局聚合搜索 | [测试](https://zhuiyinghub.19856789.xyz/api/hub/search?q=测试) |

**返回结构示例:**
```json
{
  "results": [
    {
      "name": "PanSou",
      "items": [
        {
          "name": "资源名称",
          "url": "网盘链接",
          "password": "提取码",
          "cloud_type": "网盘类型 (如阿里云盘)",
          "datetime": "时间戳"
        }
      ]
    }
  ]
}
```

## 3. 后台管理 (Admin)

管理系统内部配置。

| 接口路径 | 方法 | 说明 |
| :--- | :--- | :--- |
| `/api/admin/sources` | GET | 获取已配置的数据源列表 |
| `/api/admin/sources` | POST | 添加新数据源配置 |
| `/api/admin/sources` | DELETE | 移除指定数据源 |
