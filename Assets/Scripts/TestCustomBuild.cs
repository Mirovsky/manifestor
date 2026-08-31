using Manifestor.Build;

[ManifestorBuildStep(ManifestorBuildStepCategory.PreBuild)]
[ManifestorBuildStepOrder(typeof(BuildPlayerStep), ManifestorBuildStepOrder.Before)]
public class TestManifestorBuild : IManifestorBuildStep
{
    public ManifestorBuildStepResult Tick(ManifestorBuildContext context)
    {
        return ManifestorBuildStepResult.Succeeded();
    }
}
