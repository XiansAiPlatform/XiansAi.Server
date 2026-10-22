#!/usr/bin/env bash
# Benchmarks the conversation read-status backfill against a local MongoDB limited to 2 CPUs,
# similar to the dev Cosmos DB for MongoDB vCore M30 (2 vCores).
#
#   ./run.sh                  seed (first time only) and run every scenario
#   DAYS=7 PER_DAY=100000 RESEED=1 ./run.sh
#
# Scenarios:
#   1-old      the v3.40.5 startup backfill: one UpdateMany, 3 retries, 60 s socket timeout
#   2-new      the batched background backfill, run to completion
#   3a/3b/3c   the batched backfill killed mid-run, a replacement pod inside the 15 minute
#              claim window, and a pod after the claim went stale (resumes from last_id)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONTAINER="${CONTAINER:-xians-backfill-bench}"
PORT="${PORT:-27099}"
DB="${DB:-xians_bench}"
DAYS="${DAYS:-14}"
PER_DAY="${PER_DAY:-150000}"
CPUS="${CPUS:-2}"
KILL_AFTER="${KILL_AFTER:-45}"
CONN="mongodb://localhost:$PORT"
RESULTS="$SCRIPT_DIR/results/$(date +%Y%m%d-%H%M%S)"
HARNESS=(dotnet run --no-build --project "$SCRIPT_DIR/ReadStatusBackfillBenchmark.csproj" --)

mongo_eval() {
  docker exec "$CONTAINER" mongosh --quiet "$DB" --eval "$1"
}

mongo_script() {
  docker cp "$SCRIPT_DIR/$1" "$CONTAINER:/tmp/$1" >/dev/null
  mongo_eval "${2:-}; load('/tmp/$1')"
}

start_mongo() {
  if ! docker ps --format '{{.Names}}' | grep -qx "$CONTAINER"; then
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    docker run -d --name "$CONTAINER" --memory=8g -p "$PORT:27017" mongo:7 --wiredTigerCacheSizeGB 3.5 >/dev/null
    until mongo_eval 'db.runCommand({ ping: 1 }).ok' >/dev/null 2>&1; do sleep 1; done
  fi
}

seed_if_needed() {
  local count
  count=$(mongo_eval 'db.conversation_message.estimatedDocumentCount()')
  if [[ "${RESEED:-0}" == "1" || "$count" -lt $((DAYS * PER_DAY)) ]]; then
    docker update --cpus "$(docker info --format '{{.NCPU}}')" "$CONTAINER" >/dev/null
    mongo_script seed.js "var DAYS=$DAYS, PER_DAY=$PER_DAY"
  fi
}

start_cpu_monitor() {
  (while true; do docker stats --no-stream --format '{{.CPUPerc}}' "$CONTAINER"; done) >"$1" 2>/dev/null &
  CPU_MONITOR_PID=$!
}

stop_cpu_monitor() {
  kill "$CPU_MONITOR_PID" 2>/dev/null || true
  wait "$CPU_MONITOR_PID" 2>/dev/null || true
  awk -v cpus="$CPUS" '
    { gsub("%", "", $1); value = $1 / cpus; sum += value; samples++; if (value > max) max = value; if (value >= 90) hot++ }
    END { if (samples) printf "mongo CPU (%% of %d cores): avg %.0f%%, max %.0f%%, samples at or above 90%%: %d of %d\n", cpus, sum / samples, max, hot, samples }
  ' "$1"
}

bench() {
  local name=$1
  shift
  echo
  echo "################ $name ################"
  start_cpu_monitor "$RESULTS/$name.cpu"
  "${HARNESS[@]}" "$@" --conn "$CONN" --db "$DB" 2>&1 | tee "$RESULTS/$name.log" || true
  stop_cpu_monitor "$RESULTS/$name.cpu" | tee -a "$RESULTS/$name.log"
}

reset_data() {
  mongo_script reset.js
}

verify_data() {
  mongo_script verify.js | tee -a "$RESULTS/$1.log"
}

mkdir -p "$RESULTS"
dotnet build "$SCRIPT_DIR/ReadStatusBackfillBenchmark.csproj" --nologo -v q
start_mongo
seed_if_needed
docker update --cpus "$CPUS" "$CONTAINER" >/dev/null
mongo_eval 'const s = db.conversation_message.stats(); print(`dataset: ${s.count} messages, data ${(s.size / 1e9).toFixed(2)} GB, indexes ${(s.totalIndexSize / 1e9).toFixed(2)} GB`)' | tee "$RESULTS/dataset.txt"

reset_data
bench 1-old old
verify_data 1-old

reset_data
bench 2-new new
verify_data 2-new

reset_data
bench 3a-new-killed new --kill-after "$KILL_AFTER" --owner pod-a
verify_data 3a-new-killed
bench 3b-replacement-pod-within-15-min new --owner pod-b
mongo_eval "db.data_migrations.updateOne({ _id: 'conversation-message-non-unread-backfill' }, { \$set: { claimed_at: new Date(Date.now() - 16 * 60 * 1000) } })" >/dev/null
echo "claim aged by 16 minutes to simulate the next deploy"
bench 3c-pod-after-claim-went-stale new --owner pod-c
verify_data 3c-pod-after-claim-went-stale

echo
echo "results in $RESULTS"
