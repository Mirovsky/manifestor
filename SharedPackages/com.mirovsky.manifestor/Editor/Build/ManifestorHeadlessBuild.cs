namespace Manifestor.Build
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;

    [InitializeOnLoad]
    public static class ManifestorHeadlessBuild
    {
        private const string ActiveKey = "Manifestor.HeadlessBuild.Active";
        private const string ProfileArgument = "-manifestorProfile";
        private const string OutputArgument = "-manifestorOutput";
        private const string OptionsArgument = "-manifestorBuildOptions";

        static ManifestorHeadlessBuild()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Subscribe();
            }
        }

        public static void BuildFromCommandLine()
        {
            if (!Application.isBatchMode)
            {
                Fail("BuildFromCommandLine can only run from a batch-mode Unity process.");
                return;
            }

            if (!TryParseArguments(out var profilePath, out var outputPath, out var options, out var error))
            {
                Fail(error);
                return;
            }

            var profile = AssetDatabase.LoadAssetAtPath<ManifestProfileSO>(profilePath);
            if (profile == null)
            {
                Fail($"Manifest profile could not be loaded from '{profilePath}'.");
                return;
            }

            SessionState.SetBool(ActiveKey, true);
            Subscribe();
            var result = ManifestorUnityEditorPipeline.Start(
                profile,
                ManifestorBuildOperation.Build,
                outputPath,
                options,
                ManifestorBuildStepTargets.Standard);
            if (!result.success)
            {
                Fail(result.message);
            }
        }

        private static void Subscribe()
        {
            ManifestorUnityEditorPipeline.completed -= HandleCompleted;
            ManifestorUnityEditorPipeline.completed += HandleCompleted;
        }

        private static void HandleCompleted(
            ManifestorBuildOperation operation,
            ManifestorBuildPipelineStatus status)
        {
            if (!SessionState.GetBool(ActiveKey, false) || operation != ManifestorBuildOperation.Build)
            {
                return;
            }

            SessionState.EraseBool(ActiveKey);
            ManifestorUnityEditorPipeline.completed -= HandleCompleted;
            var exitCode = status switch
            {
                ManifestorBuildPipelineStatus.Succeeded => 0,
                ManifestorBuildPipelineStatus.Cancelled => 2,
                _ => 1
            };
            EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
        }

        private static bool TryParseArguments(
            out string profilePath,
            out string outputPath,
            out BuildOptions options,
            out string error)
        {
            profilePath = string.Empty;
            outputPath = string.Empty;
            options = BuildOptions.None;
            error = string.Empty;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var arguments = Environment.GetCommandLineArgs();
            for (var index = 0; index < arguments.Length; index++)
            {
                var argument = arguments[index];
                if (argument != ProfileArgument && argument != OutputArgument && argument != OptionsArgument)
                {
                    continue;
                }

                if (values.ContainsKey(argument) || index + 1 >= arguments.Length)
                {
                    error = $"Command-line argument '{argument}' must be provided exactly once with a value.";
                    return false;
                }

                values.Add(argument, arguments[++index]);
            }

            if (!values.TryGetValue(ProfileArgument, out profilePath) || string.IsNullOrWhiteSpace(profilePath))
            {
                error = $"Command-line argument '{ProfileArgument}' is required.";
                return false;
            }

            profilePath = profilePath.Trim().Replace('\\', '/');
            if (!profilePath.StartsWith("Assets/", StringComparison.Ordinal) ||
                !profilePath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{ProfileArgument} must be a project-relative .asset path under Assets.";
                return false;
            }

            if (!values.TryGetValue(OutputArgument, out outputPath) || string.IsNullOrWhiteSpace(outputPath))
            {
                error = $"Command-line argument '{OutputArgument}' is required.";
                return false;
            }

            if (!values.TryGetValue(OptionsArgument, out var optionsText) || string.IsNullOrWhiteSpace(optionsText))
            {
                return true;
            }

            foreach (var optionName in optionsText.Split(','))
            {
                var trimmedName = optionName.Trim();
                if (int.TryParse(trimmedName, out _) ||
                    !Enum.TryParse(trimmedName, ignoreCase: true, out BuildOptions parsedOption) ||
                    !Enum.IsDefined(typeof(BuildOptions), parsedOption))
                {
                    error = $"Unknown BuildOptions value '{trimmedName}'.";
                    return false;
                }

                options |= parsedOption;
            }

            return true;
        }

        private static void Fail(string message)
        {
            SessionState.EraseBool(ActiveKey);
            Debug.LogError($"Manifestor headless build failed: {message}");
            EditorApplication.Exit(1);
        }
    }
}
