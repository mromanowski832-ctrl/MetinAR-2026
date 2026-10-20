## Linked issue

Closes #

## Goal

<!-- One measurable result. -->

## Changes

- 

## Merge gate (METINAR-011 — see docs/METINAR-011-QA-GATES.md)

Mark every line PASS / FAIL / NOT REQUIRED / UNVERIFIED. Never mark a gate that was not run.

| Gate | Result | Evidence |
|---|---|---|
| Exact head SHA tested | `<sha>` | |
| Pure .NET tests (all `tests/*` projects) | | |
| GitHub checks (`geo-core-tests`, `qa-static`) | | |
| Unity `6000.6.4f1` batch compile (exit 0, 0 `error CS`) | | |
| Missing scripts + build scenes/indices (`UnityQaChecks`) | | |
| Play Mode smoke (WorldMap / AR) | | |
| Android ARM64/IL2CPP dev build + APK size/SHA-256 | | |
| Manifest permissions (no `ACCESS_BACKGROUND_LOCATION`, no new sensitive permission) | | |
| Privacy (no serial/IP/exact coordinates/keys in commits, logs or screenshots) | | |
| Physical device (`Invoke-MetinARDeviceSmoke.ps1` + owner checklist) | VERIFIED / UNVERIFIED | |
| Prototype performance gate (if runtime changed) | PASS / FAIL / UNVERIFIED | |

Local runner: `powershell -ExecutionPolicy Bypass -File tools\qa\Invoke-MetinARQa.ps1` (attach `Builds/qa/<run>/qa-report.md`).

## Known limitations

- 

## Recommendation

<!-- MERGE / DO NOT MERGE, and why (blocking items first). -->

## Agent declaration

- [ ] I did not claim tests that were not run.
- [ ] I did not modify `main` directly.
- [ ] I stayed within the linked issue scope.
- [ ] No generated Unity cache/build folders, APKs, keystores or secrets are tracked.
- [ ] No paid dependency, destructive operation, new privacy-sensitive permission or unapproved gameplay decision was introduced.
- [ ] I stopped rather than inventing an owner-level product decision.
