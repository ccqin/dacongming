-- 网盘转存功能数据库迁移
-- 创建时间: 2026-07-29

-- 1. 网盘账号表
CREATE TABLE IF NOT EXISTS cloud_drives (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    type TEXT NOT NULL,              -- 123/115
    name TEXT,                       -- 账号名称（可选）
    encrypted_cookie TEXT NOT NULL,  -- AES 加密的 Cookie
    status TEXT DEFAULT 'active',    -- active/expired/invalid
    expires_at DATETIME,             -- Cookie 过期时间
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE INDEX IF NOT EXISTS idx_cloud_drives_user ON cloud_drives(user_id);
CREATE INDEX IF NOT EXISTS idx_cloud_drives_type ON cloud_drives(type);

-- 2. 转存记录表
CREATE TABLE IF NOT EXISTS transfers (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL,
    drive_id INTEGER NOT NULL,
    tmdb_id INTEGER NOT NULL,
    media_type TEXT NOT NULL,        -- movie/tv
    season INTEGER,                  -- 季（剧集）
    episode INTEGER,                 -- 集（剧集）
    source_url TEXT NOT NULL,        -- 原始分享链接
    source_title TEXT,               -- 资源标题
    file_size INTEGER,               -- 文件大小（字节）
    quality TEXT,                    -- 4K/1080p/720p
    target_path TEXT NOT NULL,       -- 网盘目标路径
    status TEXT DEFAULT 'pending',   -- pending/transferring/completed/failed
    error_message TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    completed_at DATETIME,
    FOREIGN KEY (user_id) REFERENCES users(id),
    FOREIGN KEY (drive_id) REFERENCES cloud_drives(id)
);

CREATE INDEX IF NOT EXISTS idx_transfers_user ON transfers(user_id);
CREATE INDEX IF NOT EXISTS idx_transfers_tmdb ON transfers(tmdb_id, media_type);
CREATE INDEX IF NOT EXISTS idx_transfers_status ON transfers(status);

-- 3. 已转存季集表（用于增量转存和去重）
CREATE TABLE IF NOT EXISTS transferred_episodes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    tmdb_id INTEGER NOT NULL,
    media_type TEXT NOT NULL,
    season INTEGER NOT NULL,
    episode INTEGER NOT NULL,
    drive_id INTEGER NOT NULL,
    file_path TEXT,                  -- 网盘文件路径
    file_size INTEGER,
    quality TEXT,
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(tmdb_id, media_type, season, episode, drive_id)
);

CREATE INDEX IF NOT EXISTS idx_episodes_tmdb ON transferred_episodes(tmdb_id, media_type);

-- 4. 存储配置表（per-user 路径模板）
CREATE TABLE IF NOT EXISTS storage_configs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    user_id INTEGER NOT NULL UNIQUE,
    config_json TEXT NOT NULL,       -- JSON 格式的路径模板配置
    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME,
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE INDEX IF NOT EXISTS idx_storage_configs_user ON storage_configs(user_id);
