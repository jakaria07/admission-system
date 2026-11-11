#!/bin/sh
set -e

API_URL="${VITE_API_BASE_URL:-${API_BASE_URL:-http://localhost:8080}}"

cat <<EOF > /usr/share/nginx/html/env-config.js
window.__ENV__ = {
  VITE_API_BASE_URL: "${API_URL}"
};
EOF

