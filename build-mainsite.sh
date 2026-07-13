#!/bin/bash
# 追影 MainSite 构建部署脚本
# 策略：本地编译 → 拷贝产物 → Docker 打包
# 避免 Docker 内编译导致 SDK 版本差异

set -e

echo "=== 1. 本地编译 MainSite ==="
dotnet publish src/Zhuiying.MainSite/Zhuiying.MainSite.csproj -c Release -o publish/release

echo "=== 2. 拷贝发布产物 ==="
rm -rf publish/wwwroot
cp -r publish/release/wwwroot publish/wwwroot

echo "=== 3. 重建 Docker 镜像 ==="
docker compose build mainsite

echo "=== 4. 重启容器 ==="
docker compose up -d mainsite

echo "=== 5. 验证 ==="
sleep 3
WASM=$(docker exec zhuiying-mainsite ls /usr/share/nginx/html/_framework/ | grep "MainSite.*wasm$")
REF=$(docker exec zhuiying-mainsite grep -o "Zhuiying.MainSite.[^.]*.wasm" /usr/share/nginx/html/_framework/dotnet.js)
echo "WASM 文件: $WASM"
echo "dotnet.js 引用: $REF"

if echo "$REF" | grep -q "$(echo $WASM | sed 's/\.wasm$//' | sed 's/\.gz$//')"; then
    echo "✅ 部署成功 — WASM hash 匹配"
else
    echo "❌ 部署失败 — WASM hash 不匹配"
    exit 1
fi
