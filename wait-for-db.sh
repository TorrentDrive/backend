#!/bin/bash
# Wait for PostgreSQL to be ready before starting a service

PG_ISREADY="/usr/lib/postgresql/15/bin/pg_isready"

echo "[wait-for-db] Waiting for PostgreSQL to be ready..."
for i in $(seq 1 60); do
  if $PG_ISREADY -h 127.0.0.1 -p 5432 -q 2>/dev/null; then
    echo "[wait-for-db] PostgreSQL is ready!"
    exec "$@"
  fi
  echo "[wait-for-db] PostgreSQL not ready yet (attempt $i/60)..."
  sleep 1
done

echo "[wait-for-db] ERROR: PostgreSQL did not become ready in 60s"
exit 1
