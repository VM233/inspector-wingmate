using System;
using System.Collections.Generic;
using UnityEngine;

namespace VM233.InspectorWingmate
{
    [Serializable]
    internal sealed class ObjectViewHistory
    {
        const int Capacity = 32;

        [Serializable]
        sealed class View
        {
            public GameObject Target;
            public string Search;
            public Component Anchor;
            public List<Component> Selected = new();
        }

        [SerializeField] List<View> recent = new(Capacity);

        internal void Save(GameObject target, List<Component> components, ComponentSelection selection, string query)
        {
            if (!target) return;
            int index = Find(target);
            if (index < 0 && selection.All && selection.Anchor < 0 && string.IsNullOrEmpty(query)) return;
            if (index < 0 && recent.Count == Capacity) index = 0;
            View view;
            if (index >= 0)
            {
                view = recent[index];
                recent.RemoveAt(index);
            }
            else view = new View();
            recent.Add(view);
            view.Target = target;
            view.Search = query;
            view.Anchor = selection.Anchor >= 0 ? components[selection.Anchor] : null;
            view.Selected.Clear();
            for (int i = 0; i < components.Count; i++)
                if (components[i] && selection.IsSelected(i)) view.Selected.Add(components[i]);
        }

        internal string Restore(GameObject target, List<Component> components, ComponentSelection selection)
        {
            selection.Reset(components.Count);
            int index = target ? Find(target) : -1;
            if (index < 0) return string.Empty;
            View view = recent[index];
            foreach (Component component in view.Selected)
            {
                if (!component) continue;
                int position = components.IndexOf(component);
                if (position >= 0) selection.Select(position, true, false);
            }
            selection.Anchor = view.Anchor ? components.IndexOf(view.Anchor) : -1;
            return view.Search ?? string.Empty;
        }

        int Find(GameObject target)
        {
            for (int i = recent.Count - 1; i >= 0; i--)
                if (recent[i].Target == target) return i;
            return -1;
        }

        internal void ReleaseScene(UnityEngine.SceneManagement.Scene scene)
        {
            for (int i = recent.Count - 1; i >= 0; i--)
                if (!recent[i].Target || recent[i].Target.scene == scene) recent.RemoveAt(i);
        }

        internal void Clear() => recent.Clear();
    }
}
