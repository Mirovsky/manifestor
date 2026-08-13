# Manifestor

Manifestor is an Editor-only Unity package for defining reusable package manifests and applying them together with Unity Build Profiles. Profiles can control package dependencies, scoped registries, testable packages, scripting define symbols, and custom build steps.

> Manifestor `0.1.0` is an early public release. Review and commit `Packages/manifest.json` before first applying a profile.

## Requirements

- Unity 6000.4 or newer
- Git available to Unity Package Manager when installing from a Git URL

## Installation

Install from Git with **Window > Package Management > Package Manager > + > Install package from git URL**:

```text
https://github.com/Mirovsky/manifestor.git?path=/SharedPackages/com.mirovsky.manifestor#v0.1.0
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
- Implement `IManifestorBuildStep` and use `[ManifestorBuildStep]` to add and order pipeline steps.
- Use `ManifestorBuildPipeline.Apply` or `ManifestorBuildPipeline.Build` to start operations from Editor code, and subscribe to `ManifestorBuildPipeline.completed` for the final result.

See the [repository README](https://github.com/Mirovsky/manifestor#readme) for detailed usage and API examples.

## License

Manifestor is available under the [MIT License](LICENSE.md).
