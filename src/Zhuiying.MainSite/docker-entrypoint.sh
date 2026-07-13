#!/bin/sh
# 用环境变量替换 appsettings.json 中的占位符
if [ -n "$HUB_URL" ]; then
  sed -i "s|\${HUB_URL}|${HUB_URL}|g" /usr/share/nginx/html/appsettings.json
fi

# Cache-busting: 给所有 _framework 引用加版本号
TIMESTAMP=$(date +%Y%m%d%H%M)
# 1. index.html 中的 blazor.webassembly.js 引用
sed -i "s|_framework/blazor.webassembly.js|_framework/blazor.webassembly.js?v=${TIMESTAMP}|g" /usr/share/nginx/html/index.html
# 2. blazor.webassembly.js 中动态加载的 dotnet.js 引用
sed -i "s|\"./dotnet.js\"|\"./dotnet.js?v=${TIMESTAMP}\"|g" /usr/share/nginx/html/_framework/blazor.webassembly.js
# 3. 删除所有压缩版本（防止提供旧的压缩缓存）
find /usr/share/nginx/html/ -name "*.br" -delete
find /usr/share/nginx/html/ -name "*.gz" -delete

# 启动 nginx
exec nginx -g "daemon off;"
