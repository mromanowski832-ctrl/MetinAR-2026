# Owner-only repository security checklist

**MetinAR-2026 | updated 2026-10-23**

## Read permissions and account safety

1. In **Settings → Collaborators**, inspect every user, role and pending invitation. Revoke unknown/unnecessary access. The GitHub connection cannot enumerate every collaborator.
2. In **Settings → Deploy keys** and **Settings → GitHub Apps**, review keys and installations. Limit each app to the required repositories. An installation with "All repositories" will also cover repositories created later.
3. In personal account settings, review PATs, SSH keys, authorized OAuth apps, passkeys/2FA and security logs. Never copy keys into an issue.
4. Check the repository's **Insights → Traffic**. Traffic has limited historical coverage and shows counts, not reliable clone identities; it cannot establish that no one copied source.

## GitHub Pro main protection — prepared for import

**Repository owner must activate the ruleset once through the GitHub Settings UI.**
The GitHub connector used for code reviews cannot write repository rulesets or manage third-party app installations.

1. Download the verified [MetinAR protected-main JSON](../tools/security/metinar-main-ruleset.json) onto your device (open the file, use **Raw / Download**).
2. Open [MetinAR Rulesets settings](https://github.com/mromanowski832-ctrl/MetinAR-2026/settings/rules), select **New ruleset → Import a ruleset**, and select the downloaded JSON.
3. Confirm the target is **refs/heads/main**, enforcement is **Active**, and there are no bypass entries.
4. Check that deletion and force-push are blocked, all changes require a pull request, and review threads must be resolved.
5. Confirm **zero required approving reviews** while working as a solo developer. This is not a bypass: PR + successful automated checks are still mandatory. Add a real independent reviewer before increasing the approval requirement.
6. Verify the exact required check names below appear for a PR on the latest commit. Do not create the rule if any check name remains unknown or duplicated; check the current Actions runs.
7. Click **Create**, then verify that [Rulesets](https://github.com/mromanowski832-ctrl/MetinAR-2026/settings/rules) shows the new **active** ruleset. A JSON committed in the repository is **not** an active ruleset until imported and created.

**Eight required GitHub Actions check names** in the import:

- `Geo Core`
- `Gameplay Core`
- `Asset Pipeline`
- `pure .NET tests (all test projects)`
- `QA tooling (PowerShell lint + tests)`
- `repo hygiene + docs links`
- `PR metadata`
- `Secret patterns in Git history and changes`

Do **not** require the path-filtered `server-tests` workflow: it does not run on every PR. The three core test jobs used to all report generic `test`; their check names have been made unique in the accompanying CI change.

The imported ruleset enables strict up-to-date checks, so a PR can require another run after `main` moves. It prevents force-push and deletion of `main` but does **not** stop authorized users or apps from reading/cloning project code. Keep app and collaborator access minimal.

## Verification matrix

| Gate | What it proves | What it does not |
| --- | --- | --- |
| Geo Core | Pure-C# coordinate calculations against test cases | Correct GPS positioning on a physical phone |
| Gameplay Core | Deterministic game rules | Real AR collisions, animations or networked gameplay |
| Asset Pipeline | Statically verifiable asset rules | Visual quality, complete original asset provenance or runtime compatibility |
| QA Static | Repository hygiene, tests, scripts and documentation | Unity Editor or physical Android device PASS |
| Secret Scan | Selected secret patterns in the scanned Git scope | That every secret or historical copy is absent |
| Unity/Android gates | Locally collected build/runtime evidence | A universally compatible commercial release |

Always state the tested commit SHA and link the exact successful workflow run.

## Pending manual controls

- [ ] Inspect collaborators, GitHub Apps, PATs and deploy keys.
- [ ] Limit Codex connector to only repositories actually needed, especially within RomanowskiTech.
- [ ] Configure and verify the private-main ruleset.
- [ ] Validate Android/AR APK on a physical device before claiming gameplay readiness.
