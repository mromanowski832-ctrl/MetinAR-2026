# MetinAR — engineering case studies for code review

These case studies describe decisions visible in the repository. They help a recruiter ask informed questions; they are not proof of individual authorship or production readiness.

## 1. Decouple combat math from the Unity runtime

**Problem:** combat results depend on several chance checks, modifiers, mitigation and event order. Game logic tied directly to MonoBehaviours would be harder to unit test without the editor.

**Implementation:** [DamageResolver.cs](../client/Assets/MetinAR/Scripts/Gameplay/DamageResolver.cs) runs as pure C# with controlled random inputs. [DamageResolverTests.cs](../tests/MetinAR.Gameplay.Tests/DamageResolverTests.cs) exercises hit probability, blocks, parries, critical hits and mitigation using predictable RNG.

**Review questions:** Which operations are clamped? Why is the order of random draws explicit? How would you detect gameplay regressions after adjusting balance coefficients?

**Boundary:** passing logic tests says nothing about real AR blade tracking accuracy, frames per second or animation timing.

## 2. Develop server authority separately from client presentation

**Problem:** relying on client-provided coordinates and encounter outcomes without verification allows unrealistic or dishonest results in a location-based game.

**Implementation:** the .NET 8 [local development backend](../server/README.md) separates API, domain rules and SQLite persistence. Rules for distance, fix accuracy, claim validation and encounter completion are documented in [server-authority design](METINAR-012-SERVER-AUTHORITY.md). Automated in-process HTTP tests exercise the service.

**Review questions:** Which inputs can be trusted? How are expired encounters rejected? What happens when two reward claims arrive? How would the prototype change before internet-facing deployment?

**Boundary:** authentication and production deployment hardening are not implemented in this local prototype. Do not publish this API as a production service.

## 3. Make QA evidence distinguish offline checks from physical testing

**Problem:** pure .NET checks can run quickly in CI, but Unity builds and physical AR/Android behavior depend on an editor, device, ARCore and performance conditions.

**Implementation:** [GitHub Actions workflows](../.github/workflows/) run engine-independent checks; [QA gates](METINAR-011-QA-GATES.md) describe Windows Unity compilation, APK audits and device harnesses.

**Review questions:** What does a passing CI job prove? How is a cached APK rejected? Why can a simulated ADB test not count as a physical phone PASS?

**Boundary:** Android recording and QA results must be attached to the exact tested build; no such verified recording is claimed here.

## What would raise portfolio confidence

- Reproducible Android QA evidence with build hash and device metadata.
- Demonstrated end-to-end GPS/AR/combat flow, or a transparent documented blocker if it fails.
- PR references showing a concrete bug, its fix, the regression test and review.
- A brief explanation of which architectural choices were made by the contributor and which code was AI-assisted.

See the [recording checklist](RECRUITER_DEMO_CHECKLIST.md).
