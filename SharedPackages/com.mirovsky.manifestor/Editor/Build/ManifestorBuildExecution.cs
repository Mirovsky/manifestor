namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEditor;

    internal static class ManifestorBuildExecution
    {
        public static ManifestorResult TryCreatePlan(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            ManifestorBuildStepTargets targets,
            out ManifestorBuildPipelineState state)
        {
            return TryCreatePlan(profile, operation, outputFolderPath, options, targets, null, out state);
        }

        public static ManifestorResult TryCreatePlan(
            ManifestProfileSO profile,
            BuildPlayerOptions buildPlayerOptions,
            out ManifestorBuildPipelineState state)
        {
            return TryCreatePlan(
                profile,
                ManifestorBuildOperation.Build,
                string.Empty,
                BuildOptions.None,
                ManifestorBuildStepTargets.Standard,
                buildPlayerOptions,
                out state);
        }

        private static ManifestorResult TryCreatePlan(
            ManifestProfileSO profile,
            ManifestorBuildOperation operation,
            string outputFolderPath,
            BuildOptions options,
            ManifestorBuildStepTargets targets,
            BuildPlayerOptions? initialBuildPlayerOptions,
            out ManifestorBuildPipelineState state)
        {
            state = null;

            var validation = ManifestorProfileValidator.Validate(profile);
            if (!validation.success)
            {
                return validation;
            }

            if (operation == ManifestorBuildOperation.Build &&
                (initialBuildPlayerOptions.HasValue
                    ? string.IsNullOrWhiteSpace(initialBuildPlayerOptions.Value.locationPathName)
                    : string.IsNullOrWhiteSpace(outputFolderPath)))
            {
                return ManifestorResult.Error("Build output location cannot be empty.");
            }

            var profilePath = AssetDatabase.GetAssetPath(profile);
            var profileGuid = AssetDatabase.AssetPathToGUID(profilePath);
            if (string.IsNullOrEmpty(profilePath) || string.IsNullOrEmpty(profileGuid))
            {
                return ManifestorResult.Error("Manifest profile must be saved as a project asset before building.");
            }

            if (!ManifestorBuildStepOrderResolver.TryResolve(targets, out var allSteps, out var graphError))
            {
                return ManifestorResult.Error(graphError);
            }

            var orderedSteps = FilterForOperation(allSteps, operation);
            if (orderedSteps.Count == 0)
            {
                return ManifestorResult.Error($"No custom build steps are configured for the {operation} operation.");
            }

            try
            {
                var buildPlayerOptions = initialBuildPlayerOptions ??
                    (operation == ManifestorBuildOperation.Build
                        ? BuildPlayerOptionsFactory.Create(profile, outputFolderPath, options)
                        : default);
                if (operation == ManifestorBuildOperation.Build)
                {
                    var optionsValidation = NormalizeBuildPlayerOptions(profile, ref buildPlayerOptions);
                    if (!optionsValidation.success)
                    {
                        return optionsValidation;
                    }
                }

                state = new ManifestorBuildPipelineState
                {
                    isActive = true,
                    status = ManifestorBuildPipelineStatus.Waiting,
                    operation = operation,
                    targets = targets,
                    message = operation switch
                    {
                        ManifestorBuildOperation.Apply => "Manifest apply queued.",
                        _ => "Custom build queued."
                    },
                    profileGuid = profileGuid,
                    profileFingerprint = ManifestorProfileFingerprint.Calculate(profile),
                    buildPlayerOptions = SerializableBuildPlayerOptions.From(buildPlayerOptions),
                    actions = CreateActions(orderedSteps, operation),
                    resumeAfterUtcTicks = DateTime.UtcNow.Ticks
                };
                return ManifestorResult.Ok();
            }
            catch (Exception exception)
            {
                return ManifestorResult.Error($"Failed to create custom build plan: {exception.Message}");
            }
        }

        internal static ManifestorResult NormalizeBuildPlayerOptions(
            ManifestProfileSO profile,
            ref BuildPlayerOptions buildPlayerOptions)
        {
            var expectedTarget = BuildProfileUtility.GetBuildTarget(profile.buildProfile);
            var expectedGroup = BuildPipeline.GetBuildTargetGroup(expectedTarget);
            if (buildPlayerOptions.target is 0 or BuildTarget.NoTarget)
            {
                buildPlayerOptions.target = expectedTarget;
                buildPlayerOptions.subtarget = BuildProfileUtility.GetSubtarget(profile.buildProfile);
            }
            else if (buildPlayerOptions.target != expectedTarget)
            {
                return ManifestorResult.Error(
                    $"Build target '{buildPlayerOptions.target}' does not match manifest profile target '{expectedTarget}'.");
            }

            if (buildPlayerOptions.targetGroup == BuildTargetGroup.Unknown)
            {
                buildPlayerOptions.targetGroup = expectedGroup;
            }
            else if (buildPlayerOptions.targetGroup != expectedGroup)
            {
                return ManifestorResult.Error(
                    $"Build target group '{buildPlayerOptions.targetGroup}' does not match manifest profile group '{expectedGroup}'.");
            }

            return ManifestorResult.Ok();
        }

        public static ManifestorBuildStepResult ExecuteStep(Type stepType, ManifestorBuildContext context)
        {
            try
            {
                var step = (IManifestorBuildStep)Activator.CreateInstance(stepType);
                return step.Tick(context);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                return ManifestorBuildStepResult.Failed(
                    $"Build step '{stepType.FullName}' threw an exception: {exception.Message}");
            }
        }

        public static ManifestorBuildStepResult RunCategorySynchronously(
            ManifestProfileSO profile,
            ManifestorBuildStepCategory category,
            ManifestorBuildStepTargets targets,
            BuildPlayerOptions buildPlayerOptions,
            IReadOnlyDictionary<string, string> userData,
            out BuildPlayerOptions updatedBuildPlayerOptions,
            out IReadOnlyDictionary<string, string> updatedUserData)
        {
            updatedBuildPlayerOptions = buildPlayerOptions;
            updatedUserData = userData ?? new Dictionary<string, string>();
            if (!ManifestorBuildStepOrderResolver.TryResolve(targets, out var orderedSteps, out var orderError))
            {
                return ManifestorBuildStepResult.Failed(orderError);
            }

            var expectedFingerprint = ManifestorProfileFingerprint.Calculate(profile);
            var context = new ManifestorBuildContext(
                profile,
                category == ManifestorBuildStepCategory.Apply
                    ? ManifestorBuildOperation.Apply
                    : ManifestorBuildOperation.Build,
                buildPlayerOptions,
                false,
                string.Empty,
                null,
                userData,
                null);
            foreach (var stepType in FilterForCategory(orderedSteps, category))
            {
                var profileValidation = ValidateProfile(profile, expectedFingerprint, stepType);
                if (!profileValidation.success)
                {
                    return profileValidation;
                }

                var result = ExecuteStep(stepType, context);
                if (result.outcome == ManifestorBuildStepOutcome.Waiting)
                {
                    return ManifestorBuildStepResult.Failed(
                        $"Build step '{stepType.FullName}' returned Waiting in a synchronous build hook.");
                }

                if (!result.success)
                {
                    return result;
                }
            }

            if (!string.Equals(
                    ManifestorProfileFingerprint.Calculate(profile),
                    expectedFingerprint,
                    StringComparison.Ordinal))
            {
                return ManifestorBuildStepResult.Failed(
                    $"Manifest profile changed while running {category} steps.");
            }

            updatedBuildPlayerOptions = context.buildPlayerOptions;
            updatedUserData = new Dictionary<string, string>(context.userData, StringComparer.Ordinal);
            return ManifestorBuildStepResult.Succeeded();
        }

        internal static List<Type> FilterForOperation(
            IEnumerable<Type> orderedSteps,
            ManifestorBuildOperation operation)
        {
            return operation == ManifestorBuildOperation.Apply
                ? FilterForCategory(orderedSteps, ManifestorBuildStepCategory.Apply)
                : orderedSteps.ToList();
        }

        internal static List<Type> FilterForCategory(
            IEnumerable<Type> orderedSteps,
            ManifestorBuildStepCategory category)
        {
            return orderedSteps
                .Where(stepType => ManifestorBuildStepOrderResolver.GetCategory(stepType) == category)
                .ToList();
        }

        private static List<ManifestorBuildAction> CreateActions(
            IReadOnlyList<Type> orderedSteps,
            ManifestorBuildOperation operation)
        {
            var actions = new List<ManifestorBuildAction>();
            var playerBuildAdded = operation != ManifestorBuildOperation.Build;
            foreach (var stepType in orderedSteps)
            {
                if (!playerBuildAdded &&
                    ManifestorBuildStepOrderResolver.GetCategory(stepType) == ManifestorBuildStepCategory.PostBuild)
                {
                    actions.Add(ManifestorBuildAction.PlayerBuild());
                    playerBuildAdded = true;
                }

                actions.Add(ManifestorBuildAction.Step(stepType));
            }

            if (!playerBuildAdded)
            {
                actions.Add(ManifestorBuildAction.PlayerBuild());
            }

            return actions;
        }

        private static ManifestorBuildStepResult ValidateProfile(
            ManifestProfileSO profile,
            string expectedFingerprint,
            Type nextStepType)
        {
            return string.Equals(
                ManifestorProfileFingerprint.Calculate(profile),
                expectedFingerprint,
                StringComparison.Ordinal)
                ? ManifestorBuildStepResult.Succeeded()
                : ManifestorBuildStepResult.Failed(
                    $"Manifest profile changed before build step '{nextStepType.FullName}'.");
        }
    }
}
