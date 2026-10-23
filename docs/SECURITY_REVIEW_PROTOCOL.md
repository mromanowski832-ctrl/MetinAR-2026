# Owner-only repository security checklist

**MetinAR-2026 | updated 2026-10-23**

## Read permissions and account safety

1. In **Settings → Collaborators**, inspect every user, role and pending invitation. Revoke unknown/unnecessary access. The GitHub connection cannot enumerate every collaborator.
2. In **Settings → Deploy keys** and **Settings → GitHub Apps**, review keys and installations. Limit each app to the required repositories. An installation with "All repositories" will also cover repositories created later.
3. In personal account settings, review PATs, SSH keys, authorized OAuth apps, passkeys/2FA and security logs. Never copy keys into an issue.
4. Check the repository's **Insights → Traffic**. Traffic has limited historical coverage and shows counts, not reliable clone identities; it cannot establish that no one copied source.

## GitHub Pro branch protection

GitHub Pro on a personal account supports private-repository rulesets. Configure the ruleset through the GitHub web UI (the linked app cannot write administrative rulesets):

- **Settings → Rules → Rulesets → New branch ruleset**
- Rule targets the exact branch **main**.
- Keep enforcement active after verifying the status-check names in a successful run.
- Block **force pushes** and **branch deletion**.
- Require pull requests and successful status checks for code changes.
- Select checks that run on every PR: Geo Core, Gameplay Core, Asset Pipeline, QA Static and Secret Scan. Inspect their exact GitHub check names before selecting them.
- **Do not require a path-filtered workflow** (e.g. Server Tests) on PRs where that workflow does not run, or it will block merges forever.
- If working alone, do not enable mandatory approval by a second reviewer until a second trusted reviewer is available; a PR author cannot approve their own PR.

Protecting main does not retroactively remove secrets from Git history or prevent an authorized collaborator from cloning source.

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
