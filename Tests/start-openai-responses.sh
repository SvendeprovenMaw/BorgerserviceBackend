#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_DIR="$SCRIPT_DIR/OpenAiResponses.Api"

if [[ ! -d "$PROJECT_DIR" ]]; then
  echo "Project directory not found: $PROJECT_DIR" >&2
  exit 1
fi

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Development}"

cd "$PROJECT_DIR"
echo "Starting OpenAiResponses.Api in $ASPNETCORE_ENVIRONMENT mode..."
exec dotnet run --project "$PROJECT_DIR/OpenAiResponses.Api.csproj"
