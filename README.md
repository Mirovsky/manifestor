# Manifestor

Manifestor is a Unity Editor package for defining project package manifests as reusable `ScriptableObject` assets. A manifest profile combines one or more package lists with a Unity Build Profile, allowing package dependencies, scoped registries, testable packages, and scripting define symbols to change together for a target build.

Manifestor also provides a custom build pipeline and a migration window for keeping package-list assets synchronized with manual changes to `Packages/manifest.json`.

## Requirements

- Unity 6000.4 or newer
- Git installed and available to the Unity Package Manager

Manifestor is Editor-only and does not add runtime components to a player build.

## Installation

Install the package from its Git URL through the Unity Package Manager:

1. Open **Window > Package Management > Package Manager**.
2. Select the **+** menu and choose **Install package from git URL...**.
3. Enter:

   ```text
   https://github.com/Mirovsky/manifestor.git?path=/SharedPackages/com.mirovsky.manifestor
   ```

4. Select **Install**.

To pin a release or commit, append a Git revision after the package path, using `#<tag-or-commit>`.

## Usage

### 1. Create package lists

Create one or more assets with **Assets > Create > Manifestor > Packages List**. Package lists can be shared by several profiles and contain:

- **Packages**: package identifiers and their manifest values, such as `com.unity.inputsystem` and `1.14.2`.
- **Defines**: scripting define symbols for the profile's build target.
- **Scoped Registries**: registry names, URLs, and scopes.
- **Testables**: package identifiers to add to the manifest's `testables` array.

A package identifier may only be declared once across the lists assigned to the same profile. Registries and entries must have non-empty values, and each scoped registry must contain at least one scope.

### 2. Create a manifest profile

Open **Tools > Manifestor > Custom Build** and select **New Manifest**, or create an asset with **Assets > Create > Manifestor > Platform Profile**. Configure:

- **Profile Name**: the display name used to identify the profile.
- **Build Profile**: a saved Unity Build Profile for the target platform.
- **Package Lists**: the package-list assets that make up this manifest.

The Custom Build window discovers saved manifest profiles automatically. Use **Refresh** if assets were changed outside the window.

### 3. Apply a profile

Select a profile and choose **Apply Manifest**. Manifestor will:

1. Set the profile's Unity Build Profile as active.
2. Generate `Packages/manifest.json` from its package lists.
3. Replace the target's scripting define symbols with the combined defines from those lists.
4. Ask the Unity Package Manager to resolve the new manifest.

The last successfully applied profile is restored when the editor starts if its configuration has changed. If applying fails, Manifestor attempts to restore the previous manifest, active Build Profile, and define symbols.

> [!IMPORTANT]
> Applying a profile replaces the manifest dependency, scoped registry, testables, and Manifestor-managed settings with the selected profile's data. Commit or otherwise back up `Packages/manifest.json` before first use.

### 4. Build

In **Tools > Manifestor > Custom Build**, select a profile and choose:

- **Build** to apply the manifest and build the player.
- **Clean Build** from the Build dropdown to request a clean Unity build cache.

Choose an output folder when prompted. Manifestor supplies the exact player output path through Unity's `BuildPlayerOptions.locationPathName`, using `PlayerSettings.productName` and the platform extension where applicable.

Build progress and failures are reported in the Unity Console. Only one Manifestor build or apply operation can run at a time.

### Manifest migration

When installed packages change outside Manifestor, the package watches for differences between `Packages/manifest.json` and all `PackagesListSO` assets. If actionable differences are found, the **Manifest Migration** window opens automatically. You can also open it from **Tools > Manifestor > Manifest Migration**.

The window shows packages that were added, removed, or changed in the manifest. Select the package-list assets that should receive each change, create another package list if necessary, and choose **Apply**. Migration updates package-list assets; it does not apply a manifest profile.

## Extending Manifestor

Manifestor's assembly is Editor-only and has **Auto Referenced** disabled. Put extensions in an Editor assembly definition and add an explicit assembly reference to `com.mirovsky.manifestor`.

### Custom manifest profile

Subclass `ManifestProfileSO` to add project-specific serialized settings. Mark exactly one concrete, non-generic subclass with `[CustomManifestProfile]`; the Custom Build window will then create that type instead of the base profile.

```csharp
using Manifestor;
using UnityEngine;

[CustomManifestProfile]
public sealed class GameManifestProfile : ManifestProfileSO
{
    [SerializeField] private string _distributionChannel;

    public string distributionChannel => _distributionChannel;
}
```

If no custom type is marked, Manifestor creates `ManifestProfileSO`. If more than one type is marked, profile creation is rejected with an error.

### Custom build steps

Implement `ICustomBuildStep` and mark the class with `[CustomBuildStep]`. Steps must be concrete, non-generic classes with a public parameterless constructor.

```csharp
using Manifestor.Build;

[CustomBuildStep(
    typeof(ApplyManifestBuildStep),
    CustomBuildStepOrder.Before,
    runDuringApply = true)]
public sealed class ValidateContentStep : ICustomBuildStep
{
    public CustomBuildStepResult Execute(CustomBuildContext context)
    {
        if (context.profile == null)
        {
            return CustomBuildStepResult.Failed("A manifest profile is required.");
        }

        // Validate or prepare project content here.
        return CustomBuildStepResult.Succeeded();
    }
}
```

The attribute can order a step `Before` or `After` another step type. Constraints are combined across all discovered steps; dependency cycles cause the pipeline to reject the operation. Without a constraint, steps are ordered deterministically by their assembly-qualified type names.

Set `runDuringApply = true` for steps that should run when **Apply Manifest** is used. A full build runs every discovered step. The built-in pipeline contains:

1. `ApplyManifestBuildStep`, which applies and resolves the selected profile.
2. `BuildPlayerStep`, which runs after the apply step and invokes Unity's player build.

`CustomBuildContext` provides the selected `profile` and a mutable Unity `BuildPlayerOptions` value. A step can replace `context.buildPlayerOptions` to configure scenes, output location, target, subtarget, build flags, asset-bundle manifest, or extra scripting defines for later steps. Changes are retained when a step succeeds or waits. The final player step fills unset target, target group, subtarget, scenes, and location from the active build profile and Unity's saved build settings.

Because `BuildPlayerOptions` is a struct, copy it, modify the copy, and assign it back:

```csharp
var buildPlayerOptions = context.buildPlayerOptions;
buildPlayerOptions.options |= BuildOptions.Development;
context.buildPlayerOptions = buildPlayerOptions;
```

Return one of:

- `CustomBuildStepResult.Succeeded()` to continue.
- `CustomBuildStepResult.Failed(message)` to stop with an error.
- `CustomBuildStepResult.Cancelled(message)` to stop as cancelled.
- `CustomBuildStepResult.Waiting(message)` to retry the same step after the editor becomes available again.

### Starting the pipeline from code

You can start an apply or build from Editor code:

```csharp
using Manifestor;
using Manifestor.Build;
using UnityEditor;

ManifestorResult applyResult = CustomBuildPipeline.Apply(profile);
ManifestorResult buildResult = CustomBuildPipeline.Build(
    profile,
    new BuildPlayerOptions
    {
        locationPathName = outputPath,
        options = BuildOptions.CleanBuildCache
    });

if (!buildResult.success)
{
    UnityEngine.Debug.LogError(buildResult.message);
}
```

Starting the pipeline queues the work; a successful `ManifestorResult` means the operation started, not that every step has finished. Subscribe to `CustomBuildPipeline.completed` to observe its final status.
