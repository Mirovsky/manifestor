namespace Manifestor.Build
{
    [CustomBuildStep(runDuringApply = true)]
    public sealed class ApplyManifestBuildStep : ICustomBuildStep, ICustomBuildStepInterruptionHandler
    {
        public CustomBuildStepResult Tick(CustomBuildContext context)
        {
            if (context?.profile == null)
            {
                return CustomBuildStepResult.Failed("Manifest profile is required.");
            }

            return ManifestorApplicator.Tick(context);
        }

        public CustomBuildStepResult HandleInterruption(CustomBuildContext context)
        {
            return ManifestorApplicator.HandleInterruption(context);
        }
    }
}
