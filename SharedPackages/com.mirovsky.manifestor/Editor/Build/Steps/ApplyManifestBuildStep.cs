namespace Manifestor.Build
{
    [ManifestorBuildStep(runDuringApply = true)]
    public sealed class ApplyManifestBuildStep : IManifestorBuildStep, IManifestorBuildStepInterruptionHandler
    {
        public ManifestorBuildStepResult Tick(ManifestorBuildContext context)
        {
            if (context?.profile == null)
            {
                return ManifestorBuildStepResult.Failed("Manifest profile is required.");
            }

            return ManifestorApplicator.Tick(context);
        }

        public ManifestorBuildStepResult HandleInterruption(ManifestorBuildContext context)
        {
            return ManifestorApplicator.HandleInterruption(context);
        }
    }
}
