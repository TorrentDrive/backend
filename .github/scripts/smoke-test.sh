#!/usr/bin/env bash
#
# Boots the all-in-one image with NO configuration and asserts it comes up.
#
# The product's central claim is that `docker run` with nothing else works.
# Nothing verified it, so a change to entrypoint.sh or supervisord.conf that
# broke first boot would publish green and fail on a user's machine instead.
#
# Deliberately passes no -e and no --env-file. If this needs configuration to
# pass, the claim is false.
#
set -uo pipefail

IMAGE="${IMAGE:-torrentdrive-smoke:test}"
NAME="${NAME:-smoke}"
TIMEOUT_SECONDS="${TIMEOUT_SECONDS:-180}"

cleanup() { docker rm -f "$NAME" >/dev/null 2>&1 || true; }
trap cleanup EXIT

dump_logs() {
    echo "::group::container logs"
    docker logs "$NAME" 2>&1 | tail -200
    echo "::endgroup::"
}

cleanup
docker run -d --name "$NAME" -p 47100:47100 -p 47200:47200 "$IMAGE" >/dev/null

echo "Waiting up to ${TIMEOUT_SECONDS}s for the API to report ready..."
ready=0
elapsed=0
while [ "$elapsed" -lt "$TIMEOUT_SECONDS" ]; do
    if ! docker ps -q -f "name=^${NAME}$" | grep -q .; then
        echo "::error::The container exited during startup."
        dump_logs
        exit 1
    fi
    if curl -sf http://localhost:47200/api/health/ready >/dev/null 2>&1; then
        ready=1
        echo "API ready after ${elapsed}s."
        break
    fi
    sleep 3
    elapsed=$((elapsed + 3))
done

if [ "$ready" -ne 1 ]; then
    echo "::error::The API never became ready within ${TIMEOUT_SECONDS}s."
    dump_logs
    exit 1
fi

fail=0

echo "--- /api/health/ready ---"
curl -sS http://localhost:47200/api/health/ready | head -c 2000 || fail=1
echo

echo "--- frontend on 47100 ---"
status=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 20 http://localhost:47100/ || echo 000)
echo "frontend returned ${status}"
case "$status" in
    200|302|307) ;;
    *) echo "::error::Frontend returned ${status}"; fail=1 ;;
esac

# First boot must generate its own secrets and persist them inside the
# PostgreSQL volume. That location is what lets them survive `docker rm`, which
# every upgrade performs. If this file is missing, upgrades silently rotate the
# signing keys and log every user out.
echo "--- generated secrets ---"
if docker exec "$NAME" test -f /data/postgres/secrets.env; then
    echo "secrets.env present. Keys generated (values redacted):"
    docker exec "$NAME" sed -E 's/=.*/=<redacted>/' /data/postgres/secrets.env
else
    echo "::error::/data/postgres/secrets.env was not created on first boot."
    fail=1
fi

# Every supervisord program should be RUNNING. A worker that crash-loops still
# leaves the API healthy, so the health check alone would not catch it.
echo "--- supervisord programs ---"
if docker exec "$NAME" supervisorctl status; then
    :
else
    # supervisorctl exits non-zero when any program is not RUNNING.
    echo "::error::At least one supervisord program is not running."
    fail=1
fi

if [ "$fail" -ne 0 ]; then
    dump_logs
fi

exit $fail
