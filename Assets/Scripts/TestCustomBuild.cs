using Manifestor.Build;

[ManifestorBuildStep(typeof(BuildPlayerStep), ManifestorBuildStepOrder.Before, runDuringApply = true)]
public class TestManifestorBuild : IManifestorBuildStep
{
    public ManifestorBuildStepResult Tick(ManifestorBuildContext context)
    {
        return ManifestorBuildStepResult.Succeeded();
    }
}
