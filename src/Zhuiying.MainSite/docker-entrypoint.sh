#!/bin/sh
# 用环境变量替换 appsettings.json 中的占位符
if [ -n "$HUB_URL" ]; then
  sed -i "s|\${HUB_URL}|${HUB_URL}|g" /usr/share/nginx/html/appsettings.json
fi

# 启动 nginx
exec nginx -g "daemon off;"
