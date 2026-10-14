# MetinAR Autonomous Development Workflow

This document is the operating contract for autonomous work on `mromanowski832-ctrl/MetinAR-2026`.

It governs cooperation between:

- **Owner** — decides product/game design and approves high-impact actions.
- **Grok Bot / implementation agent** — edits code, operates Unity on `LuzMen`, runs tests, builds APKs, commits, pushes and opens pull requests.
- **GitHub** — source of truth for issues, branches, commits, checks, reviews and merges.
- **Review controller** — inspects GitHub changes, requests corrections, approves safe changes and advances the queue.

The objective is to minimize owner intervention without allowing agents to invent the game, hide failures or perform high-risk actions.

## 1. Sources of truth

Priority order:

1. Explicit owner decisions recorded in approved game-design documentation.
2. The active GitHub Issue acceptance criteria.
3. Existing production code and tests on `main`.
4. This workflow document.

An agent must not silently replace an owner decision with its own preference.

If a required gameplay/product decision is missing, the task becomes `BLOCKED — OWNER DECISION`. Technical implementation choices may be proposed, but permanent gameplay choices require owner approval.

## 2. GitHub Issues are the work queue

Only issues whose title begins with **`[READY]`** may be started automatically.

Status prefixes:

- `[READY]` — complete acceptance criteria; implementation may start.
- `[IN PROGRESS]` — currently being implemented.
- `[REVIEW]` — branch/PR is ready for controller review.
- `[BLOCKED]` — cannot continue without a stated dependency, local machine, account action or owner decision.
- `[DONE]` — merged and verified to the level required by the issue.
- `[BACKLOG]` — not authorized to start yet.

Only one implementation issue may be `IN PROGRESS` at a time unless an issue explicitly permits parallel work.

## 3. Start protocol for Grok Bot

For the oldest/smallest numbered `[READY]` issue:

1. Read the complete issue and linked documentation.
2. Verify repository and local Windows status.
3. Ensure `main` is clean and synchronized with `origin/main`.
4. Create or continue the exact branch named in the issue.
5. Change the issue title from `[READY]` to `[IN PROGRESS]`.
6. Add an issue comment:

```text
AGENT STARTED
Branch: <branch>
Base: <main SHA>
Local Unity access: YES/NO
Planned verification: <tests/builds>
```

7. Implement only the issue scope.

Do not begin unrelated cleanup while implementing a feature.

## 4. Branch and commit rules

- Never commit directly to `main`.
- Use issue-defined branch names, normally `feat/<number>-...`, `fix/<number>-...` or `chore/<number>-...`.
- Preserve working code unless the issue explicitly replaces it.
- Commit messages must describe the actual change.
- Never claim compilation or tests passed unless they were actually run.
- Never hide failed tests, Unity errors, build errors or device limitations.
- Generated Unity caches (`Library`, `Temp`, `Logs`, `obj`, `UserSettings`, `Builds`, `.vs`) must remain untracked.

## 5. Required verification gates

Use only the gates relevant to the issue, but every applicable gate is mandatory.

### Pure code

- Existing automated tests remain green.
- New pure logic receives real tests where practical.
- Tests reference production source; do not duplicate implementation into tests.

### Unity

- Unity version: `6000.6.4f1` unless an approved migration issue changes it.
- Batch import/compile exits `0`.
- `0` C# compiler errors.
- No blocking package errors.
- No missing-script components in affected scenes/prefabs.

### Android

When the issue changes mobile runtime behavior or explicitly requests a build:

- Development APK/AAB builds successfully.
- ARM64 and IL2CPP remain configured where required.
- Relevant manifest permissions are inspected.
- `ACCESS_BACKGROUND_LOCATION` must not be introduced unless a separate approved issue explicitly authorizes it.
- A successful build is not a physical-device test.

### Physical device

Mark as `UNVERIFIED` unless actually tested on a physical device.

GPS, camera, AR tracking, hand tracking, motion interaction and performance cannot be declared working solely from compilation or emulator output.

## 6. Pull-request contract

After implementation and verification:

1. Push the branch.
2. Open a PR to `main`.
3. Link the issue using `Closes #<issue>` only when merge should complete the issue.
4. Change issue title to `[REVIEW]`.
5. Add an issue comment:

```text
AGENT READY FOR REVIEW
PR: #<number>
Head: <SHA>
Tests: <result>
Unity: <result>
Android build: <result or NOT REQUIRED/NOT VERIFIED>
Physical device: <result or UNVERIFIED>
Known limitations: <list>
```

6. Stop implementation until review feedback appears on the PR or issue.

## 7. Review-controller protocol

The controller:

1. Reads the issue acceptance criteria.
2. Inspects the full PR diff and relevant repository files.
3. Checks GitHub Actions and submitted local Unity/build evidence.
4. Distinguishes blocking defects from optional polish.
5. Submits either:
   - `REQUEST_CHANGES` with exact corrections, or
   - approval/merge when all applicable gates pass.

When requesting changes, the controller posts:

```text
CONTROLLER CHANGES REQUESTED
Blocking items:
1. ...
Required verification after correction:
- ...
```

The implementation agent continues on the same branch, pushes corrections and updates the PR.

## 8. Autonomous merge policy

A PR may be squash-merged autonomously only when all of the following are true:

- It implements a `[REVIEW]` issue with unambiguous acceptance criteria.
- The branch is mergeable and current with its base or can be safely updated.
- All required GitHub checks are green.
- Required Unity compilation and builds passed.
- No unresolved review thread or blocking defect remains.
- The change does not require owner product/design approval.
- The change does not involve a prohibited high-impact action listed below.

Use squash merge with a concise `METINAR-XXX: ...` title.

After merge:

1. Pull/synchronize local `main` when local access is available.
2. Verify `origin/main` contains the merge.
3. Delete the merged feature branch when safe.
4. Change the linked issue title to `[DONE]` and close it.
5. Mark the next authorized backlog item `[READY]` only when dependencies are satisfied.

## 9. Actions that always require owner intervention

Do not perform these autonomously:

- Publishing or releasing to Google Play, App Store, TestFlight production or public channels.
- Creating purchases, subscriptions, advertising, billing or paid cloud services.
- Buying assets, licenses, map quotas or API plans.
- Entering, exposing or rotating passwords, tokens, signing keys, certificates or private credentials.
- Changing account ownership, organization settings or security settings.
- Deleting repositories, production data, user data or remote backups.
- Rewriting shared branch history or force-pushing `main`.
- Introducing a paid dependency.
- Adding code/assets with uncertain licensing or copying proprietary game assets.
- Enabling background location, invasive permissions or privacy-sensitive collection without explicit approval.
- Making unresolved product decisions such as monetization, final combat balance, classes, progression, economy, map provider or final visual identity.
- Claiming physical GPS/AR/hand-tracking success without a physical test.

## 10. Local computer and Grok cloud roles

### Grok cloud computer

May:

- edit source and documentation;
- use Git/GitHub;
- run pure tests;
- prepare branches, commits, issues and PRs.

Must not pretend it has tested Unity graphics, physical GPS, camera or AR.

### `LuzMen` Windows computer

Used for:

- Unity Editor import and compilation;
- Play Mode smoke tests;
- scene generation/screenshots;
- Android builds;
- ADB/device operations when the owner connects and authorizes a phone.

If `LuzMen` is offline or local execution is denied, comment:

```text
AGENT BLOCKED
Reason: LOCAL WINDOWS OFFLINE / APPROVAL REQUIRED / OTHER
Completed: ...
Remaining: ...
```

Do not fabricate completion.

## 11. Communication and notification policy

The owner should be contacted only when:

- a product/game-design decision is required;
- a physical-device action or OS permission is required;
- an account login, 2FA, certificate or store action is required;
- a paid or licensing decision is required;
- a destructive or privacy-sensitive action is proposed;
- a milestone has been merged or a blocker cannot be resolved technically.

Routine coding progress should remain in GitHub Issues and PRs.

## 12. Current architectural guardrails

- Engine: Unity `6000.6.4f1`.
- Target: Android and iOS.
- Use branches and pull requests.
- Preserve `GeoCoordinate`, `GeoMath` and their tests unless a dedicated reviewed issue changes them.
- Foreground GPS only unless explicitly authorized otherwise.
- AR Foundation does not itself provide phone finger landmarks; hand tracking requires a separately approved computer-vision solution.
- No copyrighted Metin2 assets, source, music, UI files, maps, logos or proprietary content.
- Prototype values such as aggro ranges, stats and labels are not automatically final game-design decisions.

## 13. Definition of autonomous success

The autonomous loop is working when:

```text
READY issue
→ Grok starts and comments
→ branch + implementation
→ real tests / Unity build
→ PR
→ controller review
→ corrections if needed
→ squash merge
→ issue DONE
→ next dependency-safe issue READY
```

The loop stops only for an owner-level decision, a local/device blocker, or a prohibited high-impact action.
