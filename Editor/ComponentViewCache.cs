using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace VM233.InspectorWingmate
{
    // Only the active object's controls are attached to the panel. Recent controls
    // keep their serialized bindings and search index while the user visits another object.
    internal sealed class ComponentViewCache
    {
        const int Capacity = 4;
        sealed class View
        {
            internal GameObject Target;
            internal readonly List<ComponentPanel> Rows = new();
        }

        readonly List<View> recent = new(Capacity);
        View active;
        internal int Count => active?.Rows.Count ?? 0;
        internal ComponentPanel this[int index] => active.Rows[index];

        internal void Activate(GameObject target, InspectorWingmateWindow window, List<Component> components,
            VisualElement chips, VisualElement details, bool compact)
        {
            if (active == null || !target || active.Target != target)
            {
                Detach();
                if (!target) return;
                int index = Find(target);
                if (index < 0 && recent.Count == Capacity) index = 0;
                if (index >= 0)
                {
                    active = recent[index];
                    recent.RemoveAt(index);
                }
                else active = new View();
                recent.Add(active);
                bool returning = active.Target == target;
                foreach (var row in active.Rows)
                {
                    if (returning) row.RefreshBinding();
                    else row.Bind(null);
                    chips.Add(row.Chip);
                    details.Add(row.Body);
                }
                active.Target = target;
            }
            var rows = active.Rows;
            while (rows.Count < components.Count)
            {
                var row = new ComponentPanel(window, rows.Count);
                if (compact) row.SetCompact(true);
                rows.Add(row);
                chips.Add(row.Chip);
                details.Add(row.Body);
            }
            for (int i = 0; i < rows.Count; i++)
                rows[i].Bind(i < components.Count ? components[i] : null);
        }

        int Find(GameObject target)
        {
            for (int i = recent.Count - 1; i >= 0; i--)
                if (recent[i].Target == target) return i;
            return -1;
        }

        void Detach()
        {
            if (active == null) return;
            foreach (var row in active.Rows)
            {
                if (row.Chip.parent != null) row.Chip.RemoveFromHierarchy();
                if (row.Body.parent != null) row.Body.RemoveFromHierarchy();
            }
            active = null;
        }

        internal void SetCompact(bool compact)
        {
            foreach (var view in recent)
                foreach (var row in view.Rows) row.SetCompact(compact);
        }

        internal void ReleaseScene(Scene scene)
        {
            for (int i = recent.Count - 1; i >= 0; i--)
            {
                var view = recent[i];
                if (view.Target && view.Target.scene != scene) continue;
                if (active == view) Detach();
                foreach (var row in view.Rows) row.Dispose();
                recent.RemoveAt(i);
            }
        }

        internal void Clear()
        {
            Detach();
            foreach (var view in recent)
                foreach (var row in view.Rows) row.Dispose();
            recent.Clear();
        }
    }
}
