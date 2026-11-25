#!/usr/bin/env bash
# Zendarr launcher: runs the single unified backend (src/Zendarr.Series),
# which hosts BOTH the Radarr (Movies) and Sonarr (Series) API domains on one
# listener (:6868) and serves the single unified UI (src/Zendarr.Web) from its
# own UI folder. The retired gateway is no longer involved.
#
# The backend exposes both API surfaces on the same origin and selects the
# domain per request from the presented API key, so Prowlarr/Jellyseerr connect
# to this host:6868 with either the Radarr or the Sonarr key.
#
# Usage:
#   ./zendarr.sh            # start in background, print URL + API keys
#   ./zendarr.sh --attach   # start, stream the backend log, Ctrl+C stops all
#   ./zendarr-stop.sh       # stop
set -euo pipefail

ATTACH="${ZENDARR_ATTACH:-0}"
for arg in "$@"; do
  case "$arg" in
    -a | --attach) ATTACH=1 ;;
    *)
      echo "unknown option: $arg (try --attach)" >&2
      exit 2
      ;;
  esac
done

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
RUN="$ROOT/_run"
DATA="${ZENDARR_DATA:-$RUN/data}"
LOGS="$RUN/logs"
PORT="${ZENDARR_PORT:-6868}"
BACKEND_TREE="Zendarr.Series"

export PATH="$PATH:/usr/local/bin"
if [ -d /root/.dotnet ]; then
  export PATH="$PATH:/root/.dotnet"
  export DOTNET_ROOT="${DOTNET_ROOT:-/root/.dotnet}"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1

mkdir -p "$LOGS"

find_dll() {
  ls "$ROOT"/src/"$1"/_output/net*/Sonarr.dll 2>/dev/null | head -1
}

BACKEND_DLL="$(find_dll "$BACKEND_TREE")"
if [ -z "$BACKEND_DLL" ]; then
  echo "Built backend not found. Run ./build.sh first." >&2
  exit 1
fi
FW_DIR="$(dirname "$BACKEND_DLL")"

# Stage the single unified UI into the backend's served UI folder
# (<StartUpFolder>/UI). This replaces the retired gateway's static-server job:
# the backend now serves the SPA at the origin root.
UI_SRC="$ROOT/src/Zendarr.Web/_output/UI"
if [ -d "$UI_SRC" ]; then
  rm -rf "$FW_DIR/UI"
  mkdir -p "$FW_DIR/UI"
  cp -a "$UI_SRC/." "$FW_DIR/UI/"
else
  echo "warning: unified UI not built ($UI_SRC); run ./build.sh" >&2
fi

# Start the detached backend, recording the *real* PID from inside the child.
# `setsid` execs (or forks); either way the child writes its own PID after the
# fork, so the pidfile always matches the live process.
#
# The port is forced to the canonical 6868 and UrlBase to '' through env config
# binding (unified `Zendarr:Server` section, legacy `Sonarr:Server` fallback),
# so a stale config.xml cannot re-introduce the old :7878/:8989 or /movies,
# /series prefixes.
start_backend() {
  local log="$LOGS/zendarr.log"
  local pidfile="$RUN/zendarr.pid"

  # Run with CWD inside the backend tree so the dotnet SDK muxer picks the SDK
  # pinned by that tree's global.json (net10).
  local tree_dir
  tree_dir="$(cd "$(dirname "$BACKEND_DLL")/../.." && pwd)"

  setsid nohup env \
    "Zendarr__Server__Port=$PORT" \
    "Sonarr__Server__Port=$PORT" \
    "Zendarr__Server__UrlBase=" \
    "Sonarr__Server__UrlBase=" \
    bash -c 'cd "$1" && echo $$ > "$2"; exec dotnet "$3" --data="$4" --nobrowser' \
    _ "$tree_dir" "$pidfile" "$BACKEND_DLL" "$DATA/Zendarr" </dev/null > "$log" 2>&1 &
}

is_listening() {
  curl -sS -m 2 -o /dev/null "http://localhost:$1/" 2>/dev/null
}

wait_for_port() {
  local port="$1"
  local tries="${2:-45}"
  while [ "$tries" -gt 0 ]; do
    if is_listening "$port"; then
      return 0
    fi
    sleep 1
    tries=$((tries - 1))
  done
  return 1
}

# One-time migration from the pre-rename Theoriarr data layout. The app was
# renamed to Zendarr, so the data directory and SQLite filenames changed. This
# keeps the owner's existing library/config without a manual move. It runs only
# when the backend is not already listening and only when the new data dir does
# not already contain data; if both dirs exist with content it refuses to merge.
migrate_legacy_data() {
  local old="$DATA/Theoriarr"
  local new="$DATA/Zendarr"

  [ -d "$old" ] || return 0

  if [ -e "$new" ]; then
    if [ -d "$new" ] && [ -z "$(ls -A "$new" 2>/dev/null)" ]; then
      if ! rmdir "$new" 2>/dev/null; then
        echo "error: cannot remove empty $new; refusing to start with empty data" >&2
        exit 1
      fi
    else
      echo "warning: both $old and $new exist; leaving them alone (not merging)" >&2
      return 0
    fi
  fi

  mv "$old" "$new"
  echo "Migrated pre-rename data: $old -> $new"

  # Rename the pre-rename runtime files. Only rename when the target is absent
  # so a partially migrated directory is never overwritten.
  local suffix src dst
  for suffix in "" "-journal" "-wal" "-shm"; do
    src="$new/theoriarr.db$suffix"
    dst="$new/zendarr.db$suffix"
    if [ -e "$src" ] && [ ! -e "$dst" ]; then
      mv "$src" "$dst"
      echo "Renamed $(basename "$src") -> $(basename "$dst")"
    fi
  done

  src="$new/theoriarr.restore"
  dst="$new/zendarr.restore"
  if [ -e "$src" ] && [ ! -e "$dst" ]; then
    mv "$src" "$dst"
    echo "Renamed theoriarr.restore -> zendarr.restore"
  fi

  if [ -e "$new/theoriarr.pid" ]; then
    rm -f "$new/theoriarr.pid"
    echo "Removed stale theoriarr.pid"
  fi

  for src in "$new"/logs/theoriarr*.log; do
    [ -e "$src" ] || continue
    dst="$new/logs/zendarr${src##*/theoriarr}"
    if [ ! -e "$dst" ]; then
      mv "$src" "$dst"
      echo "Renamed logs/$(basename "$src") -> logs/$(basename "$dst")"
    fi
  done
}

if is_listening "$PORT"; then
  echo "Zendarr already listening on :$PORT"
else
  migrate_legacy_data
  mkdir -p "$DATA/Zendarr"
  echo "Starting Zendarr unified backend (:$PORT, UrlBase='')..."
  start_backend
fi

wait_for_port "$PORT" || echo "warning: backend did not respond on :$PORT (see $LOGS/zendarr.log)"

get_key() {
  local tag="$1"
  local cfg="$DATA/Zendarr/config.xml"
  [ -f "$cfg" ] || return 0
  python3 -c "import re,sys;m=re.search(r'<'+sys.argv[2]+r'>([^<]+)</'+sys.argv[2]+r'>',open(sys.argv[1]).read());print(m.group(1) if m else '')" "$cfg" "$tag" || true
}

RADARR_KEY="$(get_key MovieApiKey)"
SONARR_KEY="$(get_key ApiKey)"

echo
echo "=================== Zendarr is running ==================="
echo "Zendarr UI : http://localhost:$PORT/"
echo "APIs         : /api/v3 (Radarr + Sonarr, selected by key), /api/v5 (Sonarr)"
echo
echo "Jellyseerr -> add Radarr : http://<host>:$PORT   API key: $RADARR_KEY"
echo "Jellyseerr -> add Sonarr : http://<host>:$PORT   API key: $SONARR_KEY"
echo
echo "Data: $DATA/Zendarr"
echo "Logs: $LOGS/zendarr.log"
echo "Stop: ./zendarr-stop.sh"
echo "============================================================"

if [ "$ATTACH" = "1" ]; then
  touch "$LOGS/zendarr.log"

  echo
  echo "Streaming backend log (press Ctrl+C to stop the unified service)..."
  echo

  tail -n +1 -F "$LOGS/zendarr.log" &
  TAIL_PID=$!

  shutdown() {
    trap - INT TERM
    kill "$TAIL_PID" 2>/dev/null || true
    wait "$TAIL_PID" 2>/dev/null || true
    echo
    "$ROOT/zendarr-stop.sh"
    exit 0
  }
  trap shutdown INT TERM

  wait "$TAIL_PID" || true
  shutdown
fi
