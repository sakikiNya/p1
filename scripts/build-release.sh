#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
mkdir -p .cache/release
(cd backend && GOCACHE="$PWD/../.cache/go-build" CGO_ENABLED=0 GOOS="${TARGET_OS:-linux}" GOARCH="${TARGET_ARCH:-amd64}" go build -trimpath -o ../.cache/release/inspection .)
(cd frontend && npm ci --cache "$PWD/../.cache/npm" && npm run build)
mkdir -p .cache/release/frontend
cp -R frontend/dist/. .cache/release/frontend/
printf '%s\n' '已构建 .cache/release/inspection 与 frontend/dist；部署操作见 deploy/README.md。'
