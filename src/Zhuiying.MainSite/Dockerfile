# Blazor WebAssembly Dockerfile
# 策略：先本地 publish，再 COPY 到 nginx 容器
# 避免 Docker 内编译导致 SDK 版本差异产生不兼容的 WASM

FROM nginx:alpine
WORKDIR /usr/share/nginx/html

# 从本地 publish 目录拷贝（需要先执行 dotnet publish）
COPY publish/wwwroot/ .

# nginx 配置（处理 Blazor WASM SPA 路由）
COPY src/Zhuiying.MainSite/nginx.conf /etc/nginx/conf.d/default.conf

# 启动脚本（替换 appsettings 中的环境变量）
COPY src/Zhuiying.MainSite/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 80
ENTRYPOINT ["/docker-entrypoint.sh"]
