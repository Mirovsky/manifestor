using Manifestor.Build;

[CustomBuildStep(typeof(BuildPlayerStep), CustomBuildStepOrder.Before, runDuringApply = true)]
public class TestCustomBuild : ICustomBuildStep
{
    public CustomBuildStepResult Execute(CustomBuildContext context)
    {
        return CustomBuildStepResult.Succeeded();
    }
}
