# Manifestor

Manifestor is an Editor-only Unity package for defining reusable package manifests and applying them together with Unity Build Profiles. Profiles can control package dependencies, scoped registries, testable packages, scripting define symbols, and custom build steps.

> Manifestor `0.2.0` is an early public release. Review and commit `Packages/manifest.json` before first applying a profile.

## Requirements

- Unity 6000.4 or newer
- Git available to Unity Package Manager when installing from a Git URL

## Installation

Install from Git with **Window > Package Management > Package Manager > + > Install package from git URL**:

```text
https://github.com/Mirovsky/manifestor.git?path=/SharedPackages/com.mirovsky.manifestor#v0.2.0
```

For embedded development, copy this directory to `Packages/com.mirovsky.manifestor` in the target project.

## Quick start

1. Create one or more package lists with **Assets > Create > Manifestor > Packages List**.
2. Open **Tools > Manifestor > Custom Build** and select **New Manifest**.
3. Assign a saved Unity Build Profile and the package lists to the manifest profile.
4. Select **Apply Manifest** to update the project, or **Build** to apply it and build the player.

Manifestor replaces the managed dependencies, scoped registries, testables, and target scripting defines with the selected profile's configuration. If application fails, it attempts to restore the previous manifest, active Build Profile, and define symbols.

When `Packages/manifest.json` changes outside Manifestor, use **Tools > Manifestor > Manifest Migration** to synchronize those changes back into package-list assets.

## Extending Manifestor

The package assembly has **Auto Referenced** disabled. Put extensions in an Editor assembly and explicitly reference `com.mirovsky.manifestor`.

- Subclass `ManifestProfileSO` and mark one concrete type with `[CustomManifestProfile]` to add project-specific settings.
- Implement `IManifestorBuildStep`, assign an Apply, PreBuild, or Build category with `[ManifestorBuildStep]`, and add optional `[ManifestorBuildStepOrder]` constraints.
- Use `ManifestorBuildPipeline.Apply` or `ManifestorBuildPipeline.Build` for local queued operations and subscribe to `ManifestorBuildPipeline.completed` for the final result.

## Unity Build Automation

Unity Build Automation must update package and compile-time define state before its normal Unity process starts. Configure a repository pre-build shell script to launch Unity with `Manifestor.Build.ManifestorBuildAutomation.Bootstrap`, and set `MANIFESTOR_PROFILE_PATH` to the profile asset path. Configure an `Assets/Editor` forwarding method to call `ManifestorBuildAutomation.PreExport` after UBA compilation. The pre-export call verifies the bootstrap state, activates the Unity Build Profile, and runs PreBuild-category steps synchronously.

See the repository README for complete shell, assembly definition, forwarding-hook, and UBA configuration examples.

See the [repository README](https://github.com/Mirovsky/manifestor#readme) for detailed usage and API examples.

## License

Manifestor is available under the [MIT License](LICENSE.md).
