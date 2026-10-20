# MetinAR backend (METINAR-010 prototype)

Local-development backend for MetinAR: .NET 8 ASP.NET Core Minimal API + SQLite.
Full design, API contract and trust boundaries: [`docs/METINAR-010-BACKEND.md`](../docs/METINAR-010-BACKEND.md);
server-side encounter authority and spawn schedules: [`docs/METINAR-012-SERVER-AUTHORITY.md`](../docs/METINAR-012-SERVER-AUTHORITY.md).

> Prototype only. No authentication, no secrets, no public deployment. Kestrel binds to `localhost` only.

## Requirements

- .NET SDK 8.0.x (free, MIT). Nothing else: no cloud service, account, API key or external network at runtime or in tests.

## Layout

```text
server/
  MetinAR.Server.sln
  Directory.Build.props        # net8.0, nullable, warnings-as-errors, analyzers
  src/MetinAR.Server/          # API, domain, SQLite persistence, tracked SQL migrations
  tests/MetinAR.Server.Tests/  # xUnit: unit + in-process HTTP integration tests
  scripts/smoke-local.sh       # starts the real process on localhost, restarts it, checks persistence
```

## Run locally

```bash
cd server
dotnet run --project src/MetinAR.Server --launch-profile MetinAR.Server
```

- API: <http://localhost:5080>
- Swagger UI (Development only): <http://localhost:5080/swagger>
- OpenAPI JSON: <http://localhost:5080/swagger/v1/swagger.json>
- SQLite file: `server/src/MetinAR.Server/data/metinar-dev.db` (git-ignored, created on first start; schema + synthetic seed applied automatically)

Reset local data: stop the server and delete `server/src/MetinAR.Server/data/`.

Override settings with environment variables, e.g. `ConnectionStrings__MetinAR="Data Source=/tmp/other.db"`, `ApiLimits__MaxRadiusMeters=500`.

Windows (PowerShell): same commands; delete the data folder with `Remove-Item -Recurse server\src\MetinAR.Server\data`.

## Verify

```bash
cd server
dotnet build MetinAR.Server.sln -c Release                 # warnings are errors
dotnet format MetinAR.Server.sln --verify-no-changes        # style/lint
dotnet test  MetinAR.Server.sln -c Release                  # 100 tests, no network
dotnet list  MetinAR.Server.sln package --vulnerable --include-transitive
./scripts/smoke-local.sh                                    # Linux/macOS/WSL/Git Bash: real process + restart
```

## Quick API examples

```bash
curl http://localhost:5080/health
curl "http://localhost:5080/api/v1/world/nearby?lat=0&lon=0&radiusMeters=300"
curl -X PUT http://localhost:5080/api/v1/player/demo-player -H "Content-Type: application/json" \
     -d '{"displayName":"Demo","lastKnownPosition":{"latitude":0.0004,"longitude":0.0004}}'
curl -X POST http://localhost:5080/api/v1/encounters/start -H "Content-Type: application/json" \
     -d '{"playerId":"demo-player","worldEntityId":"seed-mob-0001","clientPosition":{"latitude":0.0005,"longitude":0.00048,"horizontalAccuracyMeters":5}}'
curl -X POST http://localhost:5080/api/v1/encounters/complete -H "Content-Type: application/json" \
     -d '{"encounterId":"<id from start>","playerId":"demo-player","nonce":"<nonce from start>"}'
```

Encounter authority limits (distance, accuracy, fix age, TTL) live in the `EncounterRules` section of `appsettings.json`;
see [`docs/METINAR-012-SERVER-AUTHORITY.md`](../docs/METINAR-012-SERVER-AUTHORITY.md).
