namespace Manifestor.UI
{
    using System.Collections.Generic;
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
                    flexDirection = FlexDirection.Row
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

            for (var index = 0; index < steps.Count; index++)
            {
                var text = StringUtils.ToDisplayName(steps[index].ToString());
                if (index < steps.Count - 1)
                {
                    text += " > ";
                }

                _contentContainer.Add(new Label(text));
            }
        }
    }
}
