using Manifestor.Build;

[ManifestorBuildStep(ManifestorBuildStepCategory.PreBuild)]
public class TestManifestorBuild : IManifestorBuildStep
{
    public ManifestorBuildStepResult Tick(ManifestorBuildContext context)
    {
        return ManifestorBuildStepResult.Succeeded();
    }
}
