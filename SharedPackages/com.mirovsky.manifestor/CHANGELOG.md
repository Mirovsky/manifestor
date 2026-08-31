# Changelog

## [0.2.0] - 2026-08-31

- Split custom build steps into explicit Apply, PreBuild, and Build categories.
- Added `ManifestorBuildAutomation.Bootstrap` for applying package and define state before Unity Build Automation starts its build process.
- Added a strict synchronous `ManifestorBuildAutomation.PreExport` verifier and PreBuild-step runner.
- Replaced inline ordering on `[ManifestorBuildStep]` with repeatable `[ManifestorBuildStepOrder]` constraints.
- Added category-aware ordering and build-step visualization.

## [0.1.0] - 2026-08-13

Initial public pre-release.

- Added reusable manifest profiles composed from package-list assets.
- Added profile application for dependencies, scoped registries, testables, scripting defines, and Unity Build Profiles.
- Added a resumable custom build pipeline with extensible, ordered build steps.
- Added migration tooling for synchronizing package lists with manual changes to `Packages/manifest.json`.
- Added Editor windows for applying profiles, running builds, and reviewing migrations.

[0.2.0]: https://github.com/Mirovsky/manifestor/releases/tag/v0.2.0
[0.1.0]: https://github.com/Mirovsky/manifestor/releases/tag/v0.1.0
