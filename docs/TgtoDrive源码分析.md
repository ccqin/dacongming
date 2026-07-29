# TgtoDrive 源码详细分析

> 分析日期：2026-07-28  
> 版本：v6.6.4  
> 源码位置：`/opt/codex/dacongming/docs/TgtoDrive-source/`

---

## 一、项目结构

```
v6.6.4/
├── tgto123.py          # 123网盘核心逻辑 (4190行，主入口)
├── tgto115.py          # 115网盘处理 (613行)
├── tgto189.py          # 天翼云盘处理 (1129行)
├── quark.py            # 夸克网盘SDK (384行)
├── quark_export_share.py  # 夸克分享导出 (127行)
├── share.py            # 分享链接创建与论坛发帖 (1060行)
├── server.py           # Flask Web服务 (164行)
├── get_download_url_by_path.py  # 302播放直链获取 (528行)
├── add_mag.py          # 磁力链离线下载 (99行)
├── ptto123.py          # 本地文件秒传123 (160行)
├── ptto115.py          # 本地文件秒传115 (371行)
├── content_check.py    # AI内容审核 (138行)
├── danmu.py            # 弹幕下载 (176行)
├── zhuli115.py         # 115助力码处理 (95行)
├── templete.env        # 配置模板
├── Dockerfile
├── templates/
│   ├── index.html      # 配置页面
│   └── login.html      # 登录页面
└── static/
    ├── styles.css
    └── script.js
```

---

## 二、核心模块分析

### 2.1 tgto123.py - 123网盘主模块

#### 关键依赖
```python
from p123client.client import P123Client, check_response
import telebot  # Telegram Bot
import schedule  # 定时任务
import sqlite3   # 数据库
```

#### 核心功能

**1. 客户端初始化**
```python
def init_123_client(retry: bool = False) -> P123Client:
    # Token持久化到 db/config.txt
    # 自动检测token过期并重新获取
    client = P123Client(CLIENT_ID, CLIENT_SECRET)
    with open(token_path, "w") as f:
        f.write(client.token)
```

**2. 分享链接转存**
```python
def transfer_shared_link_optimize(client, target_url, UPLOAD_TARGET_PID):
    # 1. 解析分享链接获取 share_key 和 share_pwd
    # 2. 递归获取分享中的文件列表
    # 3. 构建 fileList 批量转存
    url = "https://www.123pan.com/b/api/restful/goapi/v1/file/copy/save"
    payload = {
        "fileList": fileList,
        "shareKey": share_key,
        "sharePwd": share_pwd,
        "currentLevel": 0
    }
```

**3. 秒传链接解析**
```python
def parse_share_link(message, share_link, up_load_pid):
    # 支持格式：
    # 123FSLinkV1$etag#size#filename$...
    # 123FSLinkV2$etag#size#filename$...
    # 123FLCPV1$commonPath%etag#size#filename$...
    # 123FLCPV2$commonPath%etag#size#filename$...
```

**4. TG频道监控**
```python
def get_latest_messages():
    # 爬取 https://t.me/s/channel_name 页面
    # 解析 div.tgme_widget_message 获取消息
    # 提取123分享链接并转存
```

**5. 数据库设计**
```sql
CREATE TABLE messages (
    msg_id INTEGER PRIMARY KEY AUTOINCREMENT,
    id TEXT,
    date TEXT,
    message_url TEXT,
    target_url TEXT,
    transfer_status TEXT,    -- 待转存/转存成功/转存失败
    transfer_time TEXT,
    transfer_result TEXT
)
```

### 2.2 tgto115.py - 115网盘模块

#### 关键依赖
```python
from p115client.client import P115Client, check_response, normalize_attr_simple
from p115client.exception import P115OSError, AuthenticationError
```

#### 核心功能

**1. 客户端初始化（Cookie认证）**
```python
def init_115_client():
    client_115 = P115Client(cookies=COOKIES)
    client_115.user_info()  # 验证Cookie有效性
```

**2. 分享链接转存**
```python
class Fake115Client:
    def share_link_parser(self, link):
        # 解析 https://115.com/s/xxxxx?password=xxxxx
        match = re.search(r'https?:\/\/(115|115cdn|anxia)\.com\/s\/(\w+)\?password\=(\w+)', link)
        share_code = match.group(2)
        receive_code = match.group(3)
        return (share_code, receive_code)
    
    def post_save(self, share_code, receive_code, file_ids, pid):
        # 调用 https://webapi.115.com/share/receive
        payload = {
            'user_id': self.user_id,
            'share_code': share_code,
            'receive_code': receive_code,
            'file_id': file_id_str,
            'cid': pid
        }
```

**3. 文件转移与清理**
```python
def transfer_and_clean():
    # 递归遍历目录
    # 移动文件到目标目录
    client.fs_move_app({"ids": file_id, "to_cid": UPLOAD_TARGET_PID})
    # 删除空目录
    client.fs_delete_app(dir_id)
```

### 2.3 tgto189.py - 天翼云盘模块

#### 核心功能

**1. 登录认证**
```python
class Cloud189:
    def login(self, username, password):
        # RSA加密密码
        encryptKey = self.getEncrypt()
        usernameEncrypt = rsaEncrpt(username, keyData)
        passwordEncrypt = rsaEncrpt(password, keyData)
        # 提交登录
        self.session.post('https://open.e.189.cn/api/logbox/oauth2/loginSubmit.do', data=data)
```

**2. 分享链接转存**
```python
class Cloud189ShareInfo:
    def getAllShareFiles(self, folder_id=None):
        # 获取分享文件列表
        result = self.session.get(
            "https://cloud.189.cn/api/open/share/listShareDir.action",
            params={"shareId": self.shareId, "fileId": folder_id}
        )
    
    def saveShareFiles(self, tasksInfos, targetFolderId):
        # 批量保存文件
        response = self.session.post(
            "https://cloud.189.cn/api/open/batch/createBatchTask.action",
            data={"type": "SHARE_SAVE", "taskInfos": str(tasksInfos)}
        )
```

**3. 批量转存任务**
```python
class BatchSaveTask:
    def run(self, checkInterval=1):
        # 多线程批量转存
        self.threadPool.submit(self.__batchSave, self.targetFolderId, self.shareFolderId)
        while self.getTaskNum() > 0:
            time.sleep(checkInterval)
```

### 2.4 quark.py - 夸克网盘SDK

#### 核心类
```python
class QuarkUcSDK:
    BASE_URL = "https://pc-api.uc.cn"
    
    async def get_share_info(self, share_id, password):
        # 获取分享token
        url = f"{self.BASE_URL}/1/clouddrive/share/sharepage/token"
        data = {"pwd_id": share_id, "passcode": password}
    
    async def get_share_file_list(self, code, passcode, stoken, dir_id=0):
        # 递归获取分享文件列表
        resp = await self.share_file_list(code, passcode, stoken, dir_id)
    
    async def save_share_files(self, share_id, pwd, token, file_ids, file_tokens, target_dir_id):
        # 保存分享文件
        url = f"{self.BASE_URL}/1/clouddrive/share/sharepage/save"
    
    async def _create_download_request(self, code, pwd, stoken, fids, fids_tokens):
        # 获取下载直链
        url = f"{self.BASE_URL}/1/clouddrive/file/download"
```

### 2.5 share.py - 分享链接创建

#### TMDB元数据获取
```python
class TMDBHandler:
    def parse_metadata(self, folder_name):
        # 使用 guessit 解析文件名
        guess = guessit.guessit(cleaned_name)
        title = guess.get('title')
        year = guess.get('year')
        # 提取TMDB ID: {tmdb-12345} 或 [tmdbid=12345]
        tmdb_id_pattern = r'[{\[](?:tmdb(?:id)?)(?:=|-)(\d+)[}\]]'
    
    def get_metadata(self, folder_name, media_type="tv"):
        # 优先使用TMDB ID查询
        # 回退到标题+年份搜索
```

#### 论坛自动发帖
```python
class ForumPoster:
    def post(self, title, content, media_type, video_info):
        # 自动获取标签：画质、来源、地区、类型
        # 生成帖子内容并提交
        url = f"{self.base_url}/forum.php?mod=post&action=newthread&fid={fid}"
```

### 2.6 get_download_url_by_path.py - 302播放

#### 核心逻辑
```python
def get_download_url_by_path(file_path):
    # 1. 从文件路径提取文件名
    # 2. 搜索123网盘获取 file_id
    search_url = f"https://www.123pan.com/b/api/file/list/new?SearchData={file_name}"
    # 3. 获取下载直链
    url = f"https://open-api.123pan.com/api/v1/file/download_info?fileId={file_id}"
    # 4. 缓存直链（30分钟有效期）
    url_cache[file_name] = (file_id, download_url, current_time)
    # 5. 异步预缓存同目录其他文件
    threading.Thread(target=precache_parent_directory_files, args=(...))
```

### 2.7 server.py - Web服务

#### Flask路由
```python
@app.route('/api/env')          # GET: 获取配置 / POST: 保存配置
@app.route('/api/login')        # POST: 登录验证
@app.route('/api/logout')       # GET: 登出
@app.route('/d/<path:file_path>')  # GET: 302重定向到下载直链
```

---

## 三、配置项详解 (templete.env)

### 3.1 123网盘配置
```env
ENV_123_CLIENT_ID=           # 必填：123账号
ENV_123_CLIENT_SECRET=       # 必填：123密码
ENV_DIY_LINK_PWD=            # 自定义提取码
ENV_AUTO_MAKE_JSON=1         # 自动生成JSON秒传
ENV_MAKE_NEW_LINK=1          # 复用旧链接
ENV_TMDB_API_KEY=            # TMDB API密钥

ENV_123_LINK_UPLOAD_PID=0    # 分享链接转存目录ID
ENV_123_MAGNET_UPLOAD_PID=0  # 磁力链离线目录ID
ENV_123_JSON_UPLOAD_PID=0    # JSON秒传目录ID
ENV_FILE_PER_SECOND=5        # 每秒保存文件数

# 频道监控
ENV_AUTHORIZATION=0          # 是否开启监控
ENV_TG_CHANNEL=              # 监控的TG频道（多个用|分隔）
ENV_123_UPLOAD_PID=0         # 监控转存目录ID
ENV_FILTER=                  # 包含关键词（多个用|分隔）
ENV_EXCLUDE_FILTER=          # 排除关键词
ENV_SECOND_FILTER=           # 二次过滤（格式：DV:1,DOLBY:2）
ENV_CHECK_INTERVAL=5         # 检查间隔（分钟）
```

### 3.2 115网盘配置
```env
ENV_115_COOKIES=             # 115 Cookie
ENV_115_LINK_UPLOAD_PID=0    # 分享链接转存目录ID
ENV_115_TGMONITOR_SWITCH=0   # 是否开启监控
ENV_115_TG_CHANNEL=          # 监控的TG频道
ENV_115_UPLOAD_PID=0         # 监控转存目录ID
ENV_115_CLEAN_PID=           # 自动清理目录ID
ENV_115_TRASH_PASSWORD=0     # 回收站密码
```

### 3.3 天翼云盘配置
```env
ENV_189_CLIENT_ID=           # 天翼账号
ENV_189_CLIENT_SECRET=       # 天翼密码
ENV_189_LINK_UPLOAD_PID=0    # 分享链接转存目录ID
ENV_189_TGMONITOR_SWITCH=0   # 是否开启监控
ENV_189_TG_CHANNEL=          # 监控的TG频道
ENV_189_UPLOAD_PID=0         # 监控转存目录ID
ENV_189_CLEAR_PID=           # 自动清理目录ID
ENV_189_CLEAR_PERIOD=6       # 清理周期（小时）
```

### 3.4 夸克网盘配置
```env
ENV_KUAKE_COOKIE=            # 夸克Cookie
ENV_123_KUAKE_UPLOAD_PID=5   # 夸克转存123的目录ID
```

### 3.5 302播放配置
```env
MAX_CACHE_302LINK=100        # 最大缓存直链数
DANMAKU_API_URL=             # 弹幕API地址
DANMAKU_API_KEY=             # 弹幕API密钥
```

### 3.6 AI内容审核
```env
AI_API_URL=                  # API地址（如 https://api.openai.com）
AI_API_KEY=                  # API密钥
AI_API_MODEL=                # 模型名称（如 gpt-4o-mini）
```

### 3.7 本地秒传配置
```env
ENV_PTTO123_SWITCH=0         # 123秒传开关
ENV_PTTO115_SWITCH=0         # 115秒传开关
ENV_PTTO123_UPLOAD_PID=0     # 123秒传目录ID
ENV_PTTO115_UPLOAD_PID=0     # 115秒传目录ID
```

---

## 四、API调用汇总

### 4.1 123网盘API

| 功能 | API地址 | 方法 |
|------|---------|------|
| 获取Token | `https://open-api.123pan.com/api/v1/access_token` | POST |
| 分享链接转存 | `https://www.123pan.com/b/api/restful/goapi/v1/file/copy/save` | POST |
| 搜索文件 | `https://www.123pan.com/b/api/file/list/new` | GET |
| 获取下载直链 | `https://open-api.123pan.com/api/v1/file/download_info` | GET |
| 创建分享链接 | `https://open-api.123pan.com/api/v1/share/create` | POST |
| 离线下载解析 | `https://www.123pan.com/b/api/v2/offline_download/task/resolve` | POST |
| 离线下载提交 | `https://www.123pan.com/b/api/v2/offline_download/task/submit` | POST |
| 秒传上传 | `client.upload_file_fast()` | SDK |

### 4.2 115网盘API

| 功能 | API地址 | 方法 |
|------|---------|------|
| 获取用户信息 | `https://my.115.com/?ct=ajax&ac=get_user_aq` | GET |
| 分享列表 | `https://webapi.115.com/share/snap` | GET |
| 分享转存 | `https://webapi.115.com/share/receive` | POST |
| 文件列表 | `client.fs_files_app()` | SDK |
| 移动文件 | `client.fs_move_app()` | SDK |
| 删除文件 | `client.fs_delete_app()` | SDK |
| 秒传上传 | `multipart_upload_init()` | SDK |

### 4.3 天翼云盘API

| 功能 | API地址 | 方法 |
|------|---------|------|
| 登录 | `https://open.e.189.cn/api/logbox/oauth2/loginSubmit.do` | POST |
| 分享信息 | `https://cloud.189.cn/api/open/share/getShareInfoByCodeV2.action` | GET |
| 分享文件列表 | `https://cloud.189.cn/api/open/share/listShareDir.action` | GET |
| 批量保存 | `https://cloud.189.cn/api/open/batch/createBatchTask.action` | POST |
| 任务状态 | `https://cloud.189.cn/api/open/batch/checkBatchTask.action` | POST |
| 创建文件夹 | `https://cloud.189.cn/api/open/file/createFolder.action` | POST |
| 删除文件 | `https://cloud.189.cn/api/open/batch/createBatchTask.action` (type=DELETE) | POST |

### 4.4 夸克网盘API

| 功能 | API地址 | 方法 |
|------|---------|------|
| 获取分享Token | `https://pc-api.uc.cn/1/clouddrive/share/sharepage/token` | POST |
| 分享文件列表 | `https://pc-api.uc.cn/1/clouddrive/share/sharepage/detail` | GET |
| 保存分享文件 | `https://pc-api.uc.cn/1/clouddrive/share/sharepage/save` | POST |
| 获取下载直链 | `https://pc-api.uc.cn/1/clouddrive/file/download` | POST |

---

## 五、关键实现细节

### 5.1 123网盘Token管理
```python
# Token持久化路径
token_path = os.path.join(DB_DIR, "config.txt")

# 自动检测过期
if "token is expired" in str(e).lower():
    os.remove(token_path)
    return init_123_client(retry=True)
```

### 5.2 115网盘Cookie认证
```python
# 从浏览器获取Cookie格式
COOKIES = "UID=xxxx; CID=xxxx; SEID=xxxx"
client = P115Client(cookies=COOKIES)
```

### 5.3 天翼云盘RSA加密
```python
from Crypto.Cipher import PKCS1_v1_5 as Cipher_pksc1_v1_5
from Crypto.PublicKey import RSA

def rsaEncrpt(password, public_key):
    rsakey = RSA.importKey(public_key)
    cipher = Cipher_pksc1_v1_5.new(rsakey)
    return cipher.encrypt(password.encode()).hex()
```

### 5.4 夸克网盘异步处理
```python
async with QuarkUcSDK(cookie=cookie) as quark:
    share_info = await quark.get_share_info(code, password)
    async for file_info in quark.get_share_file_list(...):
        # 异步生成器处理文件列表
```

### 5.5 TG频道监控
```python
# 爬取公开频道页面
channel_url = f'https://t.me/s/{channel_name}'
response = session.get(channel_url, headers=headers)
soup = BeautifulSoup(response.text, 'html.parser')

# 解析消息
message_divs = soup.find_all('div', class_='tgme_widget_message')
for msg in message_divs:
    data_post = msg.get('data-post', '')
    message_url = f"https://t.me/{data_post}"
    # 提取网盘链接
```

### 5.6 秒传链接格式

**123网盘秒传**
```
123FSLinkV2$etag#size#filename$etag#size#filename$...
123FLCPV2$commonPath%etag#size#filename$...
```

**解析逻辑**
```python
parts = s_link.split('#')
etag = parts[0]
size = int(parts[1])
file_path = '#'.join(parts[2:])
```

### 5.7 302播放缓存策略
```python
# 直链缓存30分钟
CACHE_EXPIRATION = 720 * 60  # 30分钟

# 父目录缓存12小时
PARENT_DIR_CACHE_EXPIRATION = 12 * 3600

# 预缓存同目录其他文件
def precache_parent_directory_files(parent_file_id, token, current_file_name):
    # 获取父目录文件列表
    # 批量获取下载直链并缓存
```

### 5.8 文件名解析 (guessit)
```python
import guessit

# 解析文件名
guess = guessit.guessit("长安的荔枝.2025.S01E28.第28集.2160p.DoVi.H.265.mp4")
# 结果:
# title: 长安的荔枝
# year: 2025
# season: 1
# episode: 28
# screen_size: 2160p
# video_codec: H.265
```

### 5.9 内容审核
```python
def check_porn_content(content, api_url, api_key, model_name):
    prompt = f"""
    请以"宁可错杀一千，不可放过一个"的严格标准判断以下内容是否涉及色情。
    ...
    """
    payload = {
        "model": model_name,
        "messages": [
            {"role": "system", "content": "你是严格的内容审核员..."},
            {"role": "user", "content": prompt}
        ]
    }
```

---

## 六、数据库设计

### 6.1 消息记录表 (每个网盘独立)
```sql
-- TG_monitor-123.db / TG_monitor-115.db / TG_monitor-189.db
CREATE TABLE messages (
    msg_id INTEGER PRIMARY KEY AUTOINCREMENT,
    id TEXT,                    -- TG消息ID
    date TEXT,                  -- 消息时间
    message_url TEXT UNIQUE,    -- TG消息链接
    target_url TEXT,            -- 网盘链接
    transfer_status TEXT,       -- 待转存/转存成功/转存失败
    transfer_time TEXT,         -- 转存时间
    transfer_result TEXT        -- 转存结果描述
)
```

### 6.2 用户状态表
```sql
-- user_states.db
CREATE TABLE user_states (
    user_id INTEGER PRIMARY KEY,
    state TEXT,
    data TEXT,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
)
```

### 6.3 发帖记录
```
# db/posted_records.txt - 已发帖的分享链接
# db/blocked_records.txt - 被阻止的分享链接
```

---

## 七、部署架构

### 7.1 Docker容器结构
```
/app/
├── db/
│   ├── config.txt           # 123 Token
│   ├── user.env             # 用户配置
│   ├── TG_monitor-123.db    # 123消息记录
│   ├── TG_monitor-115.db    # 115消息记录
│   ├── TG_monitor-189.db    # 189消息记录
│   ├── user_states.db       # 用户状态
│   ├── posted_records.txt   # 发帖记录
│   └── log/                 # 日志目录
├── upload/                  # 本地秒传目录
├── templates/
├── static/
└── *.pyc                    # 编译后的Python文件
```

### 7.2 启动流程
```python
def main():
    # 1. 启动Flask Web服务 (端口12366)
    flask_thread = threading.Thread(target=lambda: app.run(host='0.0.0.0', port=12366))
    flask_thread.start()
    
    # 2. 初始化123客户端
    client = init_123_client()
    
    # 3. 启动Telegram Bot
    bot_thread = threading.Thread(target=start_bot_thread, daemon=True)
    bot_thread.start()
    
    # 4. 启动本地秒传监控
    threading.Thread(target=ptto123, daemon=True).start()
    
    # 5. 启动天翼云盘监控（如果配置）
    if ENV_189_TGMONITOR_SWITCH:
        client189.login(ENV_189_CLIENT_ID, ENV_189_CLIENT_SECRET)
    
    # 6. 定时任务
    schedule.every(CHECK_INTERVAL).minutes.do(tg_123monitor)
    schedule.every(CHECK_INTERVAL).minutes.do(tg_115monitor)
    schedule.every(CHECK_INTERVAL).minutes.do(tg_189monitor)
```

---

## 八、对追影项目的借鉴

### 8.1 可直接复用的代码

1. **夸克网盘SDK** (`quark.py`)
   - 完整的异步API封装
   - 支持分享列表、转存、下载

2. **天翼云盘客户端** (`tgto189.py`)
   - RSA加密登录
   - 批量转存任务

3. **TMDB解析** (`share.py`)
   - guessit文件名解析
   - TMDB API调用

4. **302播放** (`get_download_url_by_path.py`)
   - 直链缓存策略
   - 预缓存优化

### 8.2 需要适配的部分

1. **数据库设计**
   - 追影使用SQLite，需要添加 `cloud_drives`、`transfers` 表
   - 按 `tmdb_id` 存储转存记录

2. **API封装**
   - 将各网盘操作封装为独立服务
   - 统一错误处理和重试机制

3. **配置管理**
   - 追影使用环境变量
   - 需要添加网盘相关配置项

### 8.3 建议的实现顺序

1. **第一阶段：123网盘转存**
   - 使用 `p123client` 库
   - 实现分享链接转存
   - 添加转存记录表

2. **第二阶段：季集解析**
   - 使用 `guessit` 解析文件名
   - 提取 season/episode 信息
   - 存储到 `transferred_episodes` 表

3. **第三阶段：自动转存**
   - 定时检查收藏的影视
   - 搜索网盘资源
   - 自动转存并记录

4. **第四阶段：其他网盘**
   - 115网盘（需要Cookie）
   - 夸克网盘（使用现有SDK）
   - 天翼云盘（使用现有客户端）

---

## 九、注意事项

### 9.1 法律风险
- 网盘API逆向可能违反服务条款
- 自动转存涉及版权内容需谨慎
- 建议仅用于个人学习研究

### 9.2 技术风险
- 115/夸克需要Cookie，可能失效
- API可能随时变更
- 频繁请求可能被限制

### 9.3 性能考虑
- 批量转存时注意并发控制
- 直链缓存减少API调用
- 异步处理避免阻塞

---

*文档生成时间：2026-07-28*
