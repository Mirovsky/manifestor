namespace Manifestor.UI
{
    using System;
    using System.Collections.Generic;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.UIElements;

    [UxmlElement]
    public partial class DropdownButton : VisualElement
    {
        public const string ussClassName = "manifestor-dropdown-button";
        public const string mainButtonUssClassName = ussClassName + "__main-button";
        public const string arrowButtonUssClassName = ussClassName + "__arrow-button";

        private readonly Button _mainButton;
        private readonly Button _arrowButton;

        private List<string> _choices = new();

        [UxmlAttribute]
        public string text
        {
            get => _mainButton.text;
            set => _mainButton.text = value ?? string.Empty;
        }

        [UxmlAttribute]
        public List<string> choices
        {
            get => _choices;
            set
            {
                if (!ReferenceEquals(value, _choices))
                {
                    _choices = value;
                    return;
                }

                UpdateArrowButtonState();
            }
        }

        public event Action clicked
        {
            add => _mainButton.clicked += value;
            remove => _mainButton.clicked -= value;
        }

        public event Action<string> choiceSelected;

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

        private void ShowDropdown()
        {
            if (_choices.Count == 0 || panel == null)
            {
                return;
            }

            var menu = new GenericMenu();
            foreach (var choice in _choices)
            {
                menu.AddItem(new GUIContent(choice), false, () => choiceSelected?.Invoke(choice));
            }

            menu.DropDown(_mainButton.worldBound);
        }

        private void UpdateArrowButtonState()
        {
            _arrowButton?.SetEnabled(_choices.Count > 0);
        }
    }
}
