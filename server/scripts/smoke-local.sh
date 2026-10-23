#!/usr/bin/env bash
# METINAR-010/012/071 local smoke test: builds and starts the real server process on localhost against a
# temporary SQLite file, exercises every endpoint, restarts the process and checks persistence.
# Needs only the .NET 8 SDK, curl and python3. No external network or service is contacted.
set -euo pipefail

cd "$(dirname "$0")/.."
PORT="${PORT:-5089}"
BASE="http://localhost:${PORT}"
WORK="$(mktemp -d)"
DB="${WORK}/smoke.db"
LOG="${WORK}/server.log"
PID=""

cleanup() { [[ -n "${PID}" ]] && kill "${PID}" 2>/dev/null || true; rm -rf "${WORK}"; }
trap cleanup EXIT

fail() { echo "SMOKE FAIL: $*" >&2; echo "--- server log ---" >&2; cat "${LOG}" >&2 || true; exit 1; }
json() { python3 -c "import json,sys; d=json.load(sys.stdin); print(eval(sys.argv[1], {'d': d}))" "$1"; }

dotnet build src/MetinAR.Server -c Release -v q -nologo >/dev/null

start_server() {
  # Run from the project directory so appsettings*.json are the content root, like `dotnet run`.
  (cd src/MetinAR.Server && exec env ASPNETCORE_ENVIRONMENT=Development \
    ConnectionStrings__MetinAR="Data Source=${DB}" \
    dotnet bin/Release/net8.0/MetinAR.Server.dll --urls "${BASE}") >>"${LOG}" 2>&1 &
  PID=$!
  for _ in $(seq 1 60); do
    curl -fsS "${BASE}/health" >/dev/null 2>&1 && return 0
    sleep 0.5
  done
  fail "server did not become healthy"
}

stop_server() { kill "${PID}"; wait "${PID}" 2>/dev/null || true; PID=""; }

check() { # check <description> <expected> <actual>
  [[ "$2" == "$3" ]] || fail "$1: expected '$2', got '$3'"
  echo "ok  - $1"
}

start_server
check "health status"            "ok"  "$(curl -fsS "${BASE}/health" | json "d['status']")"
check "swagger json"             "200" "$(curl -s -o /dev/null -w '%{http_code}' "${BASE}/swagger/v1/swagger.json")"
check "swagger ui"               "200" "$(curl -s -o /dev/null -w '%{http_code}' "${BASE}/swagger/index.html")"
check "nearby ids"               "['seed-mob-0001', 'seed-npc-0001', 'seed-mob-0002']" \
  "$(curl -fsS "${BASE}/api/v1/world/nearby?lat=0&lon=0&radiusMeters=300" | json "[e['id'] for e in d['entities']]")"
check "nearby radius limit"      "400" "$(curl -s -o /dev/null -w '%{http_code}' "${BASE}/api/v1/world/nearby?lat=0&lon=0&radiusMeters=5000")"
check "create player"            "201" "$(curl -s -o /dev/null -w '%{http_code}' -X PUT "${BASE}/api/v1/player/smoke-player" \
  -H 'Content-Type: application/json' -d '{"displayName":"Smoke","lastKnownPosition":{"latitude":0.0123,"longitude":0.0456}}')"
NEAR='{"latitude":0.0005,"longitude":0.00048,"horizontalAccuracyMeters":5}'
FAR='{"latitude":0.0050,"longitude":0.0005,"horizontalAccuracyMeters":5}'
START="$(curl -fsS -X POST "${BASE}/api/v1/encounters/start" -H 'Content-Type: application/json' \
  -d "{\"playerId\":\"smoke-player\",\"worldEntityId\":\"seed-mob-0001\",\"clientPosition\":${NEAR}}")"
ENC="$(echo "${START}" | json "d['encounter']['id']")"
NONCE="$(echo "${START}" | json "d['nonce']")"
check "distant start rejected"   "422 ENCOUNTER_TOO_FAR" "$(curl -s -X POST "${BASE}/api/v1/encounters/start" -H 'Content-Type: application/json' \
  -d "{\"playerId\":\"smoke-player\",\"worldEntityId\":\"seed-mob-0002\",\"clientPosition\":${FAR}}" | json "str(d['status']) + ' ' + d['code']")"
check "inactive entity rejected" "409" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "${BASE}/api/v1/encounters/start" \
  -H 'Content-Type: application/json' -d "{\"playerId\":\"smoke-player\",\"worldEntityId\":\"seed-mob-0003\",\"clientPosition\":{\"latitude\":0.0003,\"longitude\":0,\"horizontalAccuracyMeters\":5}}")"
check "wrong nonce rejected"     "403" "$(curl -s -o /dev/null -w '%{http_code}' -X POST "${BASE}/api/v1/encounters/complete" -H 'Content-Type: application/json' \
  -d "{\"encounterId\":\"${ENC}\",\"playerId\":\"smoke-player\",\"nonce\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"}")"
COMPLETE="{\"encounterId\":\"${ENC}\",\"playerId\":\"smoke-player\",\"nonce\":\"${NONCE}\"}"
check "first completion"         "True" "$(curl -fsS -X POST "${BASE}/api/v1/encounters/complete" -H 'Content-Type: application/json' \
  -d "${COMPLETE}" | json "d['firstCompletion']")"
check "repeat completion"        "False" "$(curl -fsS -X POST "${BASE}/api/v1/encounters/complete" -H 'Content-Type: application/json' \
  -d "${COMPLETE}" | json "d['firstCompletion']")"
# #71 SaveGame contract: initial save, server-side reward by encounterId (idempotent), optimistic-concurrency sync.
SAVE_URL="${BASE}/api/v1/player/smoke-player/savegame"
check "initial savegame revision" "0 server" "$(curl -fsS "${SAVE_URL}" | json "str(d['revision']) + ' ' + d['saveGame']['mode']")"
CLAIM="{\"encounterId\":\"${ENC}\",\"playerId\":\"smoke-player\",\"nonce\":\"${NONCE}\",\"claim\":{\"exp\":30,\"talers\":12,\"stacks\":[{\"definitionId\":\"potion_hp_s\",\"quantity\":2}],\"items\":[],\"materials\":[]}}"
check "reward applied"           "True 1" "$(curl -fsS -X POST "${BASE}/api/v1/encounters/reward" -H 'Content-Type: application/json' \
  -d "${CLAIM}" | json "str(d['applied']) + ' ' + str(d['revision'])")"
check "reward replay idempotent" "True 12" "$(curl -fsS -X POST "${BASE}/api/v1/encounters/reward" -H 'Content-Type: application/json' \
  -d "${CLAIM}" | json "str(d['alreadyApplied']) + ' ' + str(d['saveGame']['currency']['talers'])")"
SAVE="$(curl -fsS "${SAVE_URL}" | python3 -c "import json,sys; print(json.dumps(json.load(sys.stdin)['saveGame']))")"
check "sync without If-Match"    "428" "$(curl -s -o /dev/null -w '%{http_code}' -X PUT "${SAVE_URL}" -H 'Content-Type: application/json' -d "${SAVE}")"
STALE="$(echo "${SAVE}" | python3 -c "import json,sys; d=json.load(sys.stdin); d['revision']=0; print(json.dumps(d))")"
check "sync stale revision"      "412" "$(curl -s -o /dev/null -w '%{http_code}' -X PUT "${SAVE_URL}" -H 'Content-Type: application/json' -H 'If-Match: "0"' -d "${STALE}")"
check "sync current revision"    "200 \"2\"" "$(curl -s -D - -o /dev/null -X PUT "${SAVE_URL}" -H 'Content-Type: application/json' -H 'If-Match: "1"' -d "${SAVE}" \
  | tr -d '\r' | awk 'NR==1{c=$2} tolower($1)=="etag:"{e=$2} END{print c " " e}')"
stop_server

echo "--- restarting server process ---"
start_server
check "player persisted"         "Smoke" "$(curl -fsS "${BASE}/api/v1/player/smoke-player" | json "d['displayName']")"
check "coarse position persisted" "[0.012, 0.046]" "$(curl -fsS "${BASE}/api/v1/player/smoke-player" | json "[d['lastKnownPosition']['latitude'], d['lastKnownPosition']['longitude']]")"
check "completion still idempotent after restart" "False" "$(curl -fsS -X POST "${BASE}/api/v1/encounters/complete" -H 'Content-Type: application/json' \
  -d "${COMPLETE}" | json "d['firstCompletion']")"
check "savegame persisted"       "2 12" "$(curl -fsS "${SAVE_URL}" | json "str(d['revision']) + ' ' + str(d['saveGame']['currency']['talers'])")"
check "reward still idempotent after restart" "True" "$(curl -fsS -X POST "${BASE}/api/v1/encounters/reward" -H 'Content-Type: application/json' \
  -d "${CLAIM}" | json "d['alreadyApplied']")"
check "schema version"           "0004_savegame_and_reward_ledger" "$(curl -fsS "${BASE}/health" | json "d['schemaVersion']")"
check "log has no client coordinates" "0" "$(grep -c '0\.00048\|0\.0050' "${LOG}" || true)"
check "seed not duplicated"      "3" "$(curl -fsS "${BASE}/api/v1/world/nearby?lat=0&lon=0&radiusMeters=1000" | json "d['totalInRadius']")"
grep -q "Now listening on: ${BASE}" "${LOG}" || fail "server did not report listening on localhost"
check "listening only on localhost" "0" "$(grep -c 'Now listening on: http://\(0\.0\.0\.0\|\[::\]\|\*\)' "${LOG}" || true)"
stop_server
echo "SMOKE PASS"
