# Blazor WebAssembly Dockerfile
# 阶段1: 构建和发布 Blazor WASM 应用
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# 复制项目文件
COPY ["src/Zhuiying.MainSite/Zhuiying.MainSite.csproj", "src/Zhuiying.MainSite/"]
COPY ["src/Zhuiying.Shared/Zhuiying.Shared.csproj", "src/Zhuiying.Shared/"]

# 还原依赖
RUN dotnet restore "src/Zhuiying.MainSite/Zhuiying.MainSite.csproj"

# 复制所有源代码
COPY . .

# 发布 Blazor WASM 应用
RUN dotnet publish "src/Zhuiying.MainSite/Zhuiying.MainSite.csproj" -c Release -o /app/publish

# 阶段2: 使用 nginx 托管静态文件
FROM nginx:alpine AS final
WORKDIR /usr/share/nginx/html

# 复制发布的 Blazor WASM 文件
COPY --from=build /app/publish/wwwroot .

# 复制 nginx 配置（处理 Blazor WASM 路由）
COPY src/Zhuiying.MainSite/nginx.conf /etc/nginx/conf.d/default.conf

# 复制启动脚本
COPY src/Zhuiying.MainSite/docker-entrypoint.sh /docker-entrypoint.sh
RUN chmod +x /docker-entrypoint.sh

EXPOSE 80

ENTRYPOINT ["/docker-entrypoint.sh"]
