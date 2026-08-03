namespace Manifestor.UI
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Build;
    using UnityEngine.UIElements;

    [UxmlElement]
    public partial class BuildStepsList : VisualElement
    {
        public const string ussClassName = "manifestor-build-steps-list";
        public const string errorUssClassName = ussClassName + "__error";
        public const string rowUssClassName = ussClassName + "__row";
        public const string titleUssClassName = ussClassName + "__title";
        public const string sequenceUssClassName = ussClassName + "__sequence";
        public const string entryUssClassName = ussClassName + "__entry";
        public const string separatorUssClassName = ussClassName + "__separator";
        public const string stepUssClassName = ussClassName + "__step";
        public const string emptyUssClassName = ussClassName + "__empty";

        private readonly HelpBox _errorBox;
        private readonly VisualElement _applyRow;
        private readonly VisualElement _applySequence;
        private readonly VisualElement _buildRow;
        private readonly VisualElement _buildSequence;

        public BuildStepsList()
        {
            AddToClassList(ussClassName);

            _errorBox = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            _errorBox.AddToClassList(errorUssClassName);
            _errorBox.style.display = DisplayStyle.None;
            Add(_errorBox);

            _applyRow = CreateRow("Apply Steps:", out _applySequence);
            Add(_applyRow);

            _buildRow = CreateRow("Build Steps:", out _buildSequence);
            Add(_buildRow);
        }

        public void SetSteps(bool isValid, IReadOnlyList<Type> steps, string error)
        {
            if (!isValid)
            {
                ShowError(error);
                return;
            }

            _errorBox.text = string.Empty;
            _errorBox.style.display = DisplayStyle.None;
            _applyRow.style.display = DisplayStyle.Flex;
            _buildRow.style.display = DisplayStyle.Flex;

            PartitionSteps(steps, out var applySteps, out var buildSteps);
            PopulateSequence(_applySequence, applySteps);
            PopulateSequence(_buildSequence, buildSteps);
        }

        private static VisualElement CreateRow(string title, out VisualElement sequence)
        {
            var row = new VisualElement();
            row.AddToClassList(rowUssClassName);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList(titleUssClassName);
            row.Add(titleLabel);

            sequence = new VisualElement();
            sequence.AddToClassList(sequenceUssClassName);
            row.Add(sequence);

            return row;
        }

        private static void PartitionSteps(
            IReadOnlyList<Type> steps,
            out List<Type> applySteps,
            out List<Type> buildSteps)
        {
            applySteps = new List<Type>();
            buildSteps = new List<Type>();

            if (steps == null)
            {
                return;
            }

            foreach (var step in steps)
            {
                if (step == null)
                {
                    continue;
                }

                var runsDuringApply = step
                    .GetCustomAttributes(typeof(CustomBuildStepAttribute), false)
                    .Cast<CustomBuildStepAttribute>()
                    .Any(attribute => attribute.runDuringApply);
                (runsDuringApply ? applySteps : buildSteps).Add(step);
            }
        }

        private static void PopulateSequence(VisualElement sequence, IReadOnlyList<Type> steps)
        {
            sequence.Clear();
            if (steps.Count == 0)
            {
                var emptyLabel = new Label("None");
                emptyLabel.AddToClassList(emptyUssClassName);
                sequence.Add(emptyLabel);
                return;
            }

            for (var index = 0; index < steps.Count; index++)
            {
                var step = steps[index];
                var entry = new VisualElement();
                entry.AddToClassList(entryUssClassName);

                if (index > 0)
                {
                    var separator = new Label("\u203A");
                    separator.AddToClassList(separatorUssClassName);
                    entry.Add(separator);
                }

                var stepLabel = new Label(StringUtils.ToDisplayName(step.Name))
                {
                    tooltip = step.FullName ?? step.Name
                };
                stepLabel.AddToClassList(stepUssClassName);
                entry.Add(stepLabel);
                sequence.Add(entry);
            }
        }

        private void ShowError(string error)
        {
            _errorBox.text = string.IsNullOrWhiteSpace(error)
                ? "The custom build step order is invalid."
                : error;
            _errorBox.style.display = DisplayStyle.Flex;
            _applyRow.style.display = DisplayStyle.None;
            _buildRow.style.display = DisplayStyle.None;
        }
    }
}
