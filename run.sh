#!/usr/bin/env bash
#
# Deprecated. Kept so existing instructions keep working.
#
# run.sh used to ask you to fill in a .env file before starting. That step is
# gone: the container generates its own secrets on first boot and everything
# else is configured in the browser. install.sh does the whole job.
#
# This shim will be removed in a future release. Use install.sh directly:
#
#   curl -fsSL https://raw.githubusercontent.com/TorrentDrive/backend/main/install.sh | bash
#
set -euo pipefail

YELLOW='\033[1;33m'; DIM='\033[2m'; NC='\033[0m'

echo ""
echo -e "${YELLOW}  ⚠ run.sh is deprecated — running install.sh instead.${NC}"
echo -e "${DIM}    No .env file is needed any more. See https://tc.gitnasr.com/docs${NC}"
echo ""

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [ -x "$SCRIPT_DIR/install.sh" ]; then
    exec "$SCRIPT_DIR/install.sh" "$@"
elif [ -f "$SCRIPT_DIR/install.sh" ]; then
    exec bash "$SCRIPT_DIR/install.sh" "$@"
else
    echo "  install.sh is not next to this script; fetching it." >&2
    exec bash -c 'curl -fsSL https://raw.githubusercontent.com/TorrentDrive/backend/main/install.sh | bash'
fi
