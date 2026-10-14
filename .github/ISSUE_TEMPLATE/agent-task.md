---
name: Autonomous agent task
about: Scoped implementation task for Grok Bot and the autonomous review controller
title: '[BACKLOG] METINAR-XXX — '
labels: ''
assignees: ''
---

## Goal

Describe the single measurable result of this issue.

## Owner-confirmed product requirements

List only decisions explicitly confirmed by the owner. Do not add unapproved gameplay decisions.

## Technical scope

### Create / modify

- ...

### Do not change

- ...

## Acceptance criteria

- [ ] ...
- [ ] Existing tests remain green.
- [ ] Unity `6000.6.4f1` compiles with exit code `0` and `0` C# errors when applicable.
- [ ] Android build result is reported when applicable.
- [ ] Physical-device status is explicitly `VERIFIED` or `UNVERIFIED`.
- [ ] No generated Unity cache folders are tracked.
- [ ] Documentation is updated.

## Required verification

- Pure tests: ...
- Unity compile: REQUIRED / NOT REQUIRED
- Play Mode smoke test: REQUIRED / NOT REQUIRED
- Android build: REQUIRED / NOT REQUIRED
- Physical device: REQUIRED / NOT REQUIRED

## Branch

`feat/XXX-description`

## Commit / PR

Suggested commit:

`feat: ...`

Suggested PR title:

`METINAR-XXX: ...`

## Dependencies

- ...

## Stop conditions

Stop and mark `[BLOCKED]` when:

- an owner product decision is missing;
- local Unity/device access is required but unavailable;
- a paid dependency, credential, store action, destructive action or privacy-sensitive permission is required;
- tests fail and the failure cannot be corrected within this issue's scope.

## Agent report

At completion, comment with:

```text
AGENT READY FOR REVIEW
PR: #...
Head: ...
Tests: ...
Unity: ...
Android build: ...
Physical device: ...
Known limitations: ...
```
