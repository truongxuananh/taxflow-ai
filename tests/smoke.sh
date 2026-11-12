#!/usr/bin/env bash
set -euo pipefail

API_URL="${API_URL:-http://localhost:5001}"
AI_URL="${AI_URL:-http://localhost:8000}"

curl --fail --silent "$AI_URL/health" >/dev/null
curl --fail --silent "$API_URL/health" >/dev/null

TOKEN=$(curl --fail --silent \
  -H 'Content-Type: application/json' \
  -d '{"email":"admin@example.com","password":"Admin@123"}' \
  "$API_URL/api/auth/login" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')

test -n "$TOKEN"
curl --fail --silent -H "Authorization: Bearer $TOKEN" "$API_URL/api/auth/me" >/dev/null
printf 'TaxFlow AI smoke test passed.\n'
