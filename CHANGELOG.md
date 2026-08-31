# Changelog

All notable changes to Manifestor are documented in this file.

## [0.2.0] - 2026-08-31

- Split custom build steps into explicit Apply, PreBuild, and Build categories.
- Added a two-process Unity Build Automation integration that materializes packages and defines before UBA starts Unity, then verifies state and runs synchronous PreBuild steps at pre-export.
- Added `ManifestorBuildAutomation.Bootstrap` and `ManifestorBuildAutomation.PreExport` automation entry points.
- Separated step categorization from repeatable ordering constraints.
- Enforced category order and rejected constraints that contradict it.

## [0.1.0] - 2026-08-13

Initial public pre-release.

- Added reusable manifest profiles composed from package-list assets.
- Added profile application for dependencies, scoped registries, testables, scripting defines, and Unity Build Profiles.
- Added a resumable custom build pipeline with extensible, ordered build steps.
- Added migration tooling for synchronizing package lists with manual changes to `Packages/manifest.json`.
- Added Editor windows for applying profiles, running builds, and reviewing migrations.

[0.2.0]: https://github.com/Mirovsky/manifestor/releases/tag/v0.2.0
[0.1.0]: https://github.com/Mirovsky/manifestor/releases/tag/v0.1.0
