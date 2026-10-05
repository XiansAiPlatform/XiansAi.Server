#!/usr/bin/env bash
# Full suite: smoke gate, then unit, Mongo integration, and Temporal shards in parallel.
#
# One `dotnet test` invocation has a single filter and cannot run a gate before the
# rest, or split one collection across processes. This script is that sequence. Each
# step is an ordinary `dotnet test --filter`.
#
#   ./XiansAi.Server.Tests/run-suite.sh
#
# Temporal classes share one collection, so they run one at a time inside a process.
# TEMPORAL_SHARDS (default 2) starts that many processes, each with its own local
# Temporal CLI. Raise it only if the machine can spare the CPU and memory.
#
#   TEMPORAL_SHARDS=3 ./XiansAi.Server.Tests/run-suite.sh

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/XiansAi.Server.Tests/XiansAi.Server.Tests.csproj"
RESULTS="$ROOT/XiansAi.Server.Tests/test-results/suite"
SHARDS="${TEMPORAL_SHARDS:-2}"

if ! [[ "$SHARDS" =~ ^[1-9][0-9]*$ ]]; then
  echo "TEMPORAL_SHARDS must be a positive integer (got '$SHARDS')." >&2
  exit 1
fi

mkdir -p "$RESULTS"
cd "$ROOT"

echo "Building test project"
dotnet build "$PROJECT" --nologo -v q

echo "Smoke gate (host, auth, one tenant round-trip)"
if ! dotnet test "$PROJECT" --no-build --nologo --filter "Category=Smoke"; then
  echo "Smoke gate failed. The unit, integration, and Temporal lanes were not started." >&2
  exit 1
fi

echo "Discovering Temporal test classes"
list_file="$RESULTS/temporal-tests.txt"
dotnet test "$PROJECT" --no-build --nologo --list-tests \
  --filter "FullyQualifiedName~AdminApiTemporal" >"$list_file"

classes=()
while IFS= read -r class_name; do
  classes+=("$class_name")
done < <(grep -o 'AdminApiTemporal[A-Za-z0-9_]*Tests' "$list_file" | sort -u)

class_count="${#classes[@]}"
if [[ "$class_count" -eq 0 ]]; then
  echo "No AdminApiTemporal tests were discovered. See $list_file" >&2
  exit 1
fi

if [[ "$SHARDS" -gt "$class_count" ]]; then
  SHARDS="$class_count"
fi

filters=()
shard_index=0
while [[ "$shard_index" -lt "$SHARDS" ]]; do
  filters[$shard_index]=""
  shard_index=$((shard_index + 1))
done

class_index=0
for class_name in "${classes[@]}"; do
  shard=$((class_index % SHARDS))
  piece="FullyQualifiedName~$class_name"
  if [[ -z "${filters[$shard]}" ]]; then
    filters[$shard]="$piece"
  else
    filters[$shard]="${filters[$shard]}|$piece"
  fi
  class_index=$((class_index + 1))
done

echo "Running lanes in parallel ($class_count Temporal classes across $SHARDS shards)"

lane_names=()
lane_pids=()

start_lane() {
  local name="$1"
  local filter="$2"
  local log="$RESULTS/$name.log"
  echo "  $name -> $log"
  dotnet test "$PROJECT" --no-build --nologo --filter "$filter" >"$log" 2>&1 &
  lane_names+=("$name")
  lane_pids+=("$!")
}

start_lane "unit" "FullyQualifiedName~UnitTests"
start_lane "integration" "FullyQualifiedName~IntegrationTests&FullyQualifiedName!~AdminApiTemporal"

shard_index=0
while [[ "$shard_index" -lt "$SHARDS" ]]; do
  shard_number=$((shard_index + 1))
  start_lane "temporal-$shard_number" "${filters[$shard_index]}"
  shard_index=$((shard_index + 1))
done

fail=0
lane_index=0
while [[ "$lane_index" -lt "${#lane_pids[@]}" ]]; do
  name="${lane_names[$lane_index]}"
  pid="${lane_pids[$lane_index]}"
  if wait "$pid"; then
    echo "Passed $name"
  else
    fail=1
    echo "Failed $name. Last lines of $RESULTS/$name.log:" >&2
    tail -n 40 "$RESULTS/$name.log" >&2 || true
  fi
  lane_index=$((lane_index + 1))
done

if [[ "$fail" -ne 0 ]]; then
  echo "Suite failed. Logs: $RESULTS" >&2
  exit 1
fi

echo "Suite passed. Logs: $RESULTS"
