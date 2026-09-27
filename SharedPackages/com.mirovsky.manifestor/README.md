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

Build entry points are `ManifestorUnityEditorPipeline` for CustomBuild and Editor code, `ManifestorHeadlessBuild.BuildFromCommandLine` for terminal builds, and `ManifestorUnityBuildAutomation` for UBA Apply/PreBuild/PostBuild phases.

## Build Profiles Build button

To run the full Manifestor pipeline from Unity's **Build Profiles > Build** or **Build and Run** button, enable **Use Build Profiles Build button** in **Project Settings > Manifestor**, then reload scripts or restart the Editor. The setting is off by default and is stored in `ProjectSettings/ManifestorBuildProfilesSettings.asset`.

When the active Unity Build Profile is referenced by one Manifestor profile, the button applies that profile, runs its build steps, and builds with Unity's selected output location and options. If several Manifestor profiles reference it, apply the desired Manifestor profile first; the last applied matching profile is used. Without a matching Manifestor profile, Unity's normal build runs. Build and Run preserves Unity's launch option.

Unity allows only one registered build player handler. Another package can replace Manifestor's handler, and this integration does not intercept builds started directly through `BuildPipeline.BuildPlayer` or Multiplayer Play Mode virtual players. The setting takes effect after the next script reload or Editor restart.

When `Packages/manifest.json` changes outside Manifestor, use **Tools > Manifestor > Manifest Migration** to synchronize those changes back into package-list assets.

## Extending Manifestor

The package assembly has **Auto Referenced** disabled. Put extensions in an Editor assembly and explicitly reference `com.mirovsky.manifestor`.

- Subclass `ManifestProfileSO` and mark one concrete type with `[CustomManifestProfile]` to add project-specific settings.
- Implement `IManifestorBuildStep`, assign an Apply, PreBuild, or PostBuild category with `[ManifestorBuildStep]`, and add optional `[ManifestorBuildStepOrder]` constraints.
- Use `ManifestorUnityEditorPipeline.Apply` or `.Build` for queued Editor operations, or `ManifestorHeadlessBuild.BuildFromCommandLine` from a terminal.

## Addressables

Manifestor leaves Addressables builds to Unity. For Standard Editor and headless builds, set the project's **Build Addressables on Player Build** option to **Build Addressables content on Player Build**; Unity then builds the active Addressables player data builder during Manifestor's `BuildPipeline.BuildPlayer` action. Use a UBA-enabled `PreBuild` step for shared preparation, but do not call `CleanPlayerContent` or `BuildPlayerContent` from that step.

For Unity Build Automation, enable **Yes, build Addressables** on the UBA target. UBA runs its Addressables stage after Manifestor PreBuild and before PostBuild. Disable content-update and content-only options when producing a full player build.

## Unity Build Automation

Unity Build Automation must update package and compile-time define state before its normal Unity process starts. Configure a repository pre-build shell script to launch Unity with `Manifestor.Build.ManifestorUnityBuildAutomation.Apply`, and set `MANIFESTOR_PROFILE_PATH` to the profile asset path. Forward UBA's pre-export and post-export hooks to `ManifestorUnityBuildAutomation.PreBuild` and `.PostBuild`. Pre-Export runs the same player-build preparation as Standard builds and temporarily applies its finalized scenes to Editor Build Settings; Post-Export restores the preceding scene list. Configure UBA to use project build-settings scenes. Steps can opt out of UBA with `ManifestorBuildStepTargets.Standard`.

See the repository README for complete shell, assembly definition, forwarding-hook, and UBA configuration examples.

See the [repository README](https://github.com/Mirovsky/manifestor#readme) for detailed usage and API examples.

## License

Manifestor is available under the [MIT License](LICENSE.md).
