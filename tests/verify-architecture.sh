#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR=$(cd "$(dirname "$0")/.." && pwd)

if grep -R --exclude-dir=node_modules --exclude-dir=dist --exclude-dir=bin --exclude-dir=obj \
  -E 'platform:[[:space:]]*linux/(amd64|arm64)|amd64|arm64v8|C:\\|D:\\|IIS|windowsservercore' \
  "$ROOT_DIR/backend" "$ROOT_DIR/frontend" "$ROOT_DIR/docker-compose.yml"; then
  echo "Architecture verification failed: found a forbidden Windows/AMD64-only setting."
  exit 1
fi

grep -q 'mysql:8.4' "$ROOT_DIR/docker-compose.yml"
grep -q 'mcr.microsoft.com/dotnet/sdk:8.0' "$ROOT_DIR/backend/Dockerfile"
grep -q 'node:22-alpine' "$ROOT_DIR/frontend/Dockerfile"
grep -q 'ai-service:' "$ROOT_DIR/docker-compose.yml"
grep -q '/ocr/analyze' "$ROOT_DIR/ai-service/app/main.py"
echo "Architecture configuration is multi-architecture compatible (no forced amd64/arm64 image)."
