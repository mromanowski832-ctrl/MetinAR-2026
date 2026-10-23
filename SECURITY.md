# MetinAR Security Policy

**This repository contains a private, in-development game.** No reviewer, freelancer or external service receives access to the full repository by default.

## Report a security issue privately

Use GitHub's private vulnerability reporting feature if it is enabled for this repository, or contact the repository owner via an already established private channel. Never post passwords, API tokens, secret values, private locations or exploitable details in public issues.

## Source confidentiality

- Do not change the repository visibility to Public for a portfolio or a demonstration.
- Do not grant access to the full MetinAR source or assets for a recruiting review. Use the separate private reviewer repository with independently authored examples.
- Audit collaborator access, deploy keys, GitHub Apps, OAuth apps and personal access tokens regularly.
- Do not commit local environment files, signing credentials, game-service tokens or production configurations.
- Keep copyrighted or unlicensed third-party assets out of this repository.

## Automated safeguards

- The existing QA Static workflow checks tracked credential-file names and several high-confidence token patterns.
- The Secret Scan workflow adds Gitleaks pattern checks on pushes, pull requests and manual dispatch. The action is pinned to a commit and granted **read-only** repository permissions.
- The scan is a safety gate, **not a guarantee** that there are no secrets in blobs, older history, CI artifacts, external services or collaborators' copies.
- When investigating potential leaked data, never paste the matched secret itself into PR comments or issue logs.

## If exposure is suspected

1. Revoke affected application access and **rotate active credentials with their providers**; removing a file from HEAD does not revoke an exposed secret.
2. Check relevant access logs and preserve evidence without sharing secret values.
3. Inspect all refs, tags, release artifacts and historical commits before proposing a history rewrite.
4. Coordinate history rewriting with collaborators because it changes commit SHAs and does not erase previously copied content.

See [repository branch and access review](docs/SECURITY_REVIEW_PROTOCOL.md).
