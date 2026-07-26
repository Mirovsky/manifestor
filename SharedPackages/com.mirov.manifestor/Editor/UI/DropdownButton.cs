namespace Mirov.Manifestor.Editor
{
    using System;
    using System.Collections.Generic;
    using UnityEngine.UIElements;

    [UxmlElement]
    public partial class DropdownButton : VisualElement
    {
        public const string ussClassName = "manifestor-dropdown-button";
        public const string mainButtonUssClassName = ussClassName + "__main-button";
        public const string arrowButtonUssClassName = ussClassName + "__arrow-button";

        private readonly List<DropdownItem> _dropdownItems = new();
        private readonly Button _mainButton;
        private readonly Button _arrowButton;

        [UxmlAttribute]
        public string text
        {
            get => _mainButton.text;
            set => _mainButton.text = value ?? string.Empty;
        }

        public event Action clicked
        {
            add => _mainButton.clicked += value;
            remove => _mainButton.clicked -= value;
        }

        public DropdownButton()
        {
            AddToClassList(ussClassName);
            style.flexDirection = FlexDirection.Row;

            _mainButton = new Button();
            _mainButton.AddToClassList(mainButtonUssClassName);
            _mainButton.style.flexGrow = 1;
            _mainButton.style.marginRight = 0;
            _mainButton.style.borderTopRightRadius = 0;
            _mainButton.style.borderBottomRightRadius = 0;
            Add(_mainButton);

            _arrowButton = new Button(ShowDropdown)
            {
                text = "\u25BE",
                tooltip = "More options"
            };
            _arrowButton.AddToClassList(arrowButtonUssClassName);
            _arrowButton.style.flexGrow = 0;
            _arrowButton.style.flexShrink = 0;
            _arrowButton.style.width = 20;
            _arrowButton.style.marginLeft = -1;
            _arrowButton.style.borderTopLeftRadius = 0;
            _arrowButton.style.borderBottomLeftRadius = 0;
            _arrowButton.SetEnabled(false);
            Add(_arrowButton);
        }

        public void AddDropdownItem(string label, Action action)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                throw new ArgumentException("Dropdown item label cannot be empty.", nameof(label));
            }

            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _dropdownItems.Add(new DropdownItem(label, action));
            _arrowButton.SetEnabled(true);
        }

        public void ClearDropdownItems()
        {
            _dropdownItems.Clear();
            _arrowButton.SetEnabled(false);
        }

        private void ShowDropdown()
        {
            if (_dropdownItems.Count == 0 || panel == null)
            {
                return;
            }

            var menu = new GenericDropdownMenu();
            foreach (var item in _dropdownItems)
            {
                menu.AddItem(item.label, false, item.action);
            }

            menu.DropDown(_arrowButton.worldBound, _arrowButton, DropdownMenuSizeMode.Auto);
        }

        private readonly struct DropdownItem
        {
            public readonly string label;
            public readonly Action action;

            public DropdownItem(string label, Action action)
            {
                this.label = label;
                this.action = action;
            }
        }
    }
}
