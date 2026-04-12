#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/OpenAiResponses.Api"

# Common install roots that cover regular package installs plus snap/flatpak exports.
COMMON_BINARY_DIRS=(
  "/snap/bin"
  "/var/lib/snapd/snap/bin"
  "$HOME/.local/share/flatpak/exports/bin"
  "/var/lib/flatpak/exports/bin"
  "$HOME/.dotnet"
  "$HOME/.local/bin"
  "$HOME/bin"
  "/opt/dotnet"
  "/usr/share/dotnet"
  "/usr/lib/dotnet"
  "/usr/local/bin"
  "/usr/bin"
  "/bin"
)

# Resolve a binary without assuming PATH is complete or correctly configured.
resolve_binary() {
  local binary_name="$1"
  local resolved_path=""
  local candidate=""
  local find_bin=""
  local search_root=""

  resolved_path="$(type -P "$binary_name" || true)"
  if [[ -n "$resolved_path" && -x "$resolved_path" ]]; then
    printf '%s\n' "$resolved_path"
    return 0
  fi

  for candidate in \
    "/snap/bin/$binary_name" \
    "/var/lib/snapd/snap/bin/$binary_name" \
    "$HOME/.local/share/flatpak/exports/bin/$binary_name" \
    "/var/lib/flatpak/exports/bin/$binary_name" \
    "$HOME/.dotnet/$binary_name" \
    "/opt/dotnet/$binary_name" \
    "/usr/share/dotnet/$binary_name" \
    "/usr/lib/dotnet/$binary_name"
  do
    if [[ -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  find_bin="$(type -P find || true)"
  if [[ -z "$find_bin" ]]; then
    return 1
  fi

  for search_root in "${COMMON_BINARY_DIRS[@]}"; do
    [[ -d "$search_root" ]] || continue

    candidate="$("$find_bin" "$search_root" -maxdepth 4 \( -type f -o -type l \) -name "$binary_name" -print -quit 2>/dev/null || true)"
    if [[ -n "$candidate" && -x "$candidate" ]]; then
      printf '%s\n' "$candidate"
      return 0
    fi
  done

  return 1
}

# Fail early if the script is run from an unexpected layout.
if [[ ! -d "$PROJECT_DIR" ]]; then
  echo "Project directory not found: $PROJECT_DIR" >&2
  exit 1
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

# Resolve dotnet once up front so the final exec uses a concrete binary path.
if ! DOTNET_BIN="$(resolve_binary dotnet)"; then
  echo "Required binary 'dotnet' was not found in PATH or common install locations." >&2
  echo "Checked PATH, snap exports, flatpak exports, and standard binary directories." >&2
  exit 1
fi

echo "Starting OpenAiResponses.Api in $ASPNETCORE_ENVIRONMENT mode..."
echo "Using dotnet binary: $DOTNET_BIN"
cd "$PROJECT_DIR"
exec "$DOTNET_BIN" run --project "$PROJECT_DIR/OpenAiResponses.Api.csproj"
