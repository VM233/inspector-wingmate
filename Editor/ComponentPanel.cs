using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VM233.InspectorWingmate
{
    internal sealed class ComponentPanel : IDisposable
    {
        sealed class Field
        {
            internal readonly PropertyField Input = new();
            internal string Label, Path;
            internal int Depth;
        }

        internal readonly Button Chip = new();
        internal readonly Foldout Body = new();
        readonly InspectorElement inspector = new();
        readonly VisualElement results = new();
        readonly List<Field> fields = new();
        readonly HashSet<long> references = new();
        readonly InspectorWingmateWindow window;
        readonly Action<DropdownMenuAction> runMenu;
        readonly int index;
        SerializedObject serialized;
        Component bound;
        int fieldCount;
        bool indexed, dragCandidate, selectionStyled, wasSelected;
        Vector2 pointerDown;

        internal ComponentPanel(InspectorWingmateWindow window, int index)
        {
            this.window = window;
            this.index = index;
            runMenu = RunMenu;
            Chip.clicked += Activate;
            Chip.AddManipulator(new ContextualMenuManipulator(PopulateMenu));
            Chip.RegisterCallback<PointerDownEvent>(PointerDown, TrickleDown.TrickleDown);
            Chip.RegisterCallback<PointerMoveEvent>(PointerMove, TrickleDown.TrickleDown);
            Chip.RegisterCallback<PointerUpEvent>(PointerUp, TrickleDown.TrickleDown);
            Body.Add(inspector);
            Body.Add(results);
            Body.RegisterCallback<SerializedPropertyChangeEvent>(PropertyChanged);
        }

        void PropertyChanged(SerializedPropertyChangeEvent change)
        {
            var property = change.changedProperty;
            if (property == null || !(property.isArray || property.propertyType == SerializedPropertyType.ArraySize ||
                property.propertyType == SerializedPropertyType.ManagedReference)) return;
            indexed = false;
            if (window.Searching) window.InvalidateSelection();
        }

        bool toggle, range;
        void PointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            pointerDown = evt.position;
            dragCandidate = true;
            toggle = evt.ctrlKey || evt.commandKey;
            range = evt.shiftKey;
        }
        void PointerMove(PointerMoveEvent evt)
        {
            if (!dragCandidate || (evt.pressedButtons & 1) == 0 ||
                ((Vector2)evt.position - pointerDown).sqrMagnitude < 25) return;
            dragCandidate = false;
            if (Chip.HasPointerCapture(evt.pointerId)) Chip.ReleasePointer(evt.pointerId);
            window.BeginDrag(index);
            evt.StopImmediatePropagation();
        }
        void PointerUp(PointerUpEvent evt) => dragCandidate = false;
        void Activate()
        {
            window.Choose(index, toggle, range);
            toggle = range = false;
        }

        void PopulateMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Isolate", runMenu);
            evt.menu.AppendAction("Toggle selection", runMenu);
            evt.menu.AppendAction("Show all", runMenu);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Copy components", runMenu);
            evt.menu.AppendAction("Paste to this object", runMenu, window.CanPaste
                ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        void RunMenu(DropdownMenuAction item)
        {
            switch (item.name)
            {
                case "Isolate": window.Choose(index, false, false); break;
                case "Toggle selection": window.Choose(index, true, false); break;
                case "Show all": window.ShowAll(); break;
                case "Copy components": window.CopyFrom(index); break;
                case "Paste to this object": window.Paste(); break;
            }
        }

        internal void SetCompact(bool compact)
        {
            StyleLength padding = compact ? new StyleLength(2) : new StyleLength(StyleKeyword.Null);
            Chip.style.paddingLeft = Chip.style.paddingRight = padding;
        }

        internal void Bind(Component component)
        {
            if (ReferenceEquals(bound, component)) return;
            Unbind();
            if (!component) return;
            serialized = new SerializedObject(component);
            bound = component;
            Chip.text = Body.text = ObjectNames.GetInspectorTitle(component);
            inspector.Bind(serialized);
        }

        internal void RefreshBinding()
        {
            if (serialized != null && bound && serialized.UpdateIfRequiredOrScript()) indexed = false;
        }

        internal bool Show(bool visible, bool selected, string search)
        {
            Display(Chip, serialized != null);
            if (!selectionStyled || wasSelected != selected)
            {
                Chip.EnableInClassList("unity-button--checked", selected);
                Chip.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
                wasSelected = selected;
                selectionStyled = true;
            }
            bool filtering = !string.IsNullOrEmpty(search);
            bool show = serialized != null && visible;
            if (show && filtering)
            {
                if (!indexed) Index();
                show = false;
                int parentDepth = -1;
                for (int i = 0; i < fieldCount; i++)
                {
                    Field field = fields[i];
                    if (field.Depth <= parentDepth) parentDepth = -1;
                    bool matches = parentDepth < 0 && ComponentSelection.Matches(field.Label, field.Path, search, window.FuzzySearch);
                    Display(field.Input, matches);
                    if (matches) { parentDepth = field.Depth; show = true; }
                }
            }
            Display(Body, show);
            Display(inspector, !filtering);
            Display(results, filtering);
            return show;
        }

        void Index()
        {
            results.Unbind();
            fieldCount = 0;
            references.Clear();
            serialized.UpdateIfRequiredOrScript();
            using var property = serialized.GetIterator();
            bool children = true;
            while (property.NextVisible(children))
            {
                children = property.hasVisibleChildren && property.propertyType != SerializedPropertyType.ObjectReference &&
                    property.propertyType != SerializedPropertyType.String;
                if (children && property.propertyType == SerializedPropertyType.ManagedReference)
                    children = property.managedReferenceId >= 0 && references.Add(property.managedReferenceId);
                string path = property.propertyPath;
                if (path == "m_Script") continue;
                if (fieldCount == fields.Count)
                {
                    var created = new Field();
                    fields.Add(created);
                    results.Add(created.Input);
                }
                var field = fields[fieldCount++];
                field.Path = path;
                field.Label = property.displayName;
                field.Depth = property.depth;
                field.Input.bindingPath = field.Path;
            }
            for (int i = fieldCount; i < fields.Count; i++)
            {
                fields[i].Input.bindingPath = null;
                Display(fields[i].Input, false);
            }
            results.Bind(serialized);
            indexed = true;
        }

        internal static void Display(VisualElement element, bool show)
        {
            var display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (element.style.display.value != display || element.style.display.keyword != StyleKeyword.Undefined)
                element.style.display = display;
        }

        void Unbind()
        {
            indexed = false;
            if (serialized == null) return;
            inspector.Unbind();
            results.Unbind();
            serialized.Dispose();
            serialized = null;
            bound = null;
        }

        public void Dispose() => Unbind();
    }
}
