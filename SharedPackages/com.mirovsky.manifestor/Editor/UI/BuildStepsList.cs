namespace Manifestor.UI
{
    using System.Collections.Generic;
    using System.Linq;
    using Build;
    using UnityEngine.UIElements;

    [UxmlElement]
    public partial class BuildStepsList : VisualElement
    {
        private readonly Label _errorLabel;
        private readonly VisualElement _contentContainer;

        public BuildStepsList()
        {
            _contentContainer = new VisualElement
            {
                style =
                {
                    display = DisplayStyle.Flex,
                    flexDirection = FlexDirection.Column
                }
            };
            _errorLabel = new Label
            {
                style =
                {
                    display = DisplayStyle.None
                }
            };

            _contentContainer.Add(_errorLabel);

            Add(_contentContainer);
        }

        public void SetSteps(bool isValid, IReadOnlyList<System.Type> steps, string error)
        {
            _contentContainer.Clear();

            if (!isValid)
            {
                _errorLabel.text = error;
                _errorLabel.style.display = DisplayStyle.Flex;
                return;
            }

            var previousApply = true;

            var texts = new List<string>();
            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var isApply = step.GetCustomAttributes(typeof(CustomBuildStepAttribute), false)
                    .Cast<CustomBuildStepAttribute>()
                    .Any(a => a.runDuringApply);

                if (previousApply != isApply) {
                    _contentContainer.Add(new Label("<color=#A6A6A6>Apply Steps:</color> " + string.Join(" > ", texts)));
                    texts.Clear();
                }

                texts.Add(StringUtils.ToDisplayName(steps[index].ToString()));
                previousApply = isApply;
            }
            _contentContainer.Add(new Label("<color=#A6A6A6>Build Steps:</color> " + string.Join(" > ", texts)));
        }
    }
}
