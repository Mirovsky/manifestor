namespace Manifestor.Build
{
    [CustomBuildStep(runDuringApply = true)]
    public sealed class ApplyManifestBuildStep : ICustomBuildStep
    {
        public CustomBuildStepResult Execute(CustomBuildContext context)
        {
            if (context?.profile == null)
            {
                return CustomBuildStepResult.Failed("Manifest profile is required.");
            }

            return ManifestorApplicator.Execute(context.profile);
        }
    }
}
