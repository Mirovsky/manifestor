namespace Manifestor.UI
{
    using Unity.Properties;
    using UnityEngine.UIElements;

    [UxmlObject]
    public partial class BoolClassBinding : CustomBinding, IDataSourceProvider
    {
        [UxmlAttribute]
        public string className { get; set; } = "is-active";

        [UxmlAttribute]
        public bool invert { get; set; }

        public object dataSource { get; set; }

        [CreateProperty]
        public PropertyPath dataSourcePath { get; set; }

        [UxmlAttribute("data-source-path")]
        private string dataSourcePathString
        {
            get => dataSourcePath.ToString();
            set => dataSourcePath = new PropertyPath(value);
        }

        public BoolClassBinding()
        {
            updateTrigger = BindingUpdateTrigger.OnSourceChanged;
        }

        protected override BindingResult Update(in BindingContext context)
        {
            if (string.IsNullOrWhiteSpace(className))
            {
                return new BindingResult(
                    BindingStatus.Failure,
                    $"{nameof(BoolClassBinding)} requires a class name.");
            }

            var source = context.dataSource;

            if (source == null)
            {
                context.targetElement.RemoveFromClassList(className);

                return new BindingResult(
                    BindingStatus.Failure,
                    "The resolved data source is null.");
            }

            if (!PropertyContainer.TryGetValue(
                    ref source,
                    context.dataSourcePath,
                    out bool value,
                    out var returnCode))
            {
                context.targetElement.RemoveFromClassList(className);

                return new BindingResult(
                    BindingStatus.Failure,
                    $"Could not resolve boolean at '{context.dataSourcePath}'. " +
                    $"Return code: {returnCode}.");
            }

            context.targetElement.EnableInClassList(
                className,
                invert ? !value : value);

            return new BindingResult(BindingStatus.Success);
        }

        protected override void OnDeactivated(in BindingActivationContext context)
        {
            // Prevent the class from remaining after the binding is removed.
            if (!string.IsNullOrWhiteSpace(className))
            {
                context.targetElement.RemoveFromClassList(className);
            }
        }
    }
}
