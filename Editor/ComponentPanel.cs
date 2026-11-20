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
        readonly int index;
        SerializedObject serialized;
        Component bound;
        int fieldCount;
        bool indexed, dragCandidate;
        Vector2 pointerDown;

        internal ComponentPanel(InspectorWingmateWindow window, int index)
        {
            this.window = window;
            this.index = index;
            Chip.clicked += Activate;
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

        internal bool Show(bool visible, bool selected, string search)
        {
            Display(Chip, serialized != null);
            Chip.EnableInClassList("unity-button--checked", selected);
            // Tint also works in editor skins that do not style the checked button class.
            Chip.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
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
                    bool matches = parentDepth < 0 && ComponentSelection.Matches(field.Label, field.Path, search);
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
                if (property.propertyPath == "m_Script") continue;
                if (fieldCount == fields.Count)
                {
                    var created = new Field();
                    fields.Add(created);
                    results.Add(created.Input);
                }
                var field = fields[fieldCount++];
                field.Path = property.propertyPath;
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
