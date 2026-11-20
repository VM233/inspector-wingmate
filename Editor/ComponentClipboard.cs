using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;

namespace VM233.InspectorWingmate
{
    // Each entry owns a frozen native preset. Recapturing the same component type
    // updates it in place. Transform hierarchy and component ownership are never copied.
    internal sealed class ComponentClipboard : IDisposable
    {
        sealed class Entry
        {
            internal Type Type;
            internal Preset Data;
            internal int Ordinal;
            internal Vector3 Position, Scale;
            internal Quaternion Rotation;
        }

        readonly List<Entry> entries = new();
        readonly List<Component> destination = new();
        readonly Dictionary<Type, int> ordinals = new();
        internal int Count { get; private set; }

        internal void Capture(List<Component> components, ComponentSelection selection)
        {
            Count = 0;
            ordinals.Clear();
            for (int i = 0; i < components.Count; i++)
            {
                var component = components[i];
                if (!component || !selection.Includes(i)) continue;
                Type type = component.GetType();
                if (Count == entries.Count) entries.Add(new Entry());
                var entry = entries[Count];
                if (entry.Type != type)
                {
                    Release(entry);
                    entry.Type = type;
                }
                if (component is Transform transform && !(component is RectTransform))
                {
                    entry.Position = transform.localPosition;
                    entry.Rotation = transform.localRotation;
                    entry.Scale = transform.localScale;
                }
                else if (entry.Data)
                {
                    if (!entry.Data.UpdateProperties(component))
                        throw new InvalidOperationException("Unity could not capture this component preset.");
                }
                else
                {
                    entry.Data = new Preset(component) { hideFlags = HideFlags.HideAndDontSave };
                    if (!entry.Data.IsValid()) { Release(entry); continue; }
                }
                ordinals.TryGetValue(type, out entry.Ordinal);
                ordinals[type] = entry.Ordinal + 1;
                Count++;
            }
            // Empty clipboard slots must not keep references to a previously copied scene.
            for (int i = Count; i < entries.Count; i++) Release(entries[i]);
        }

        internal void Paste(GameObject target)
        {
            if (!target || EditorUtility.IsPersistent(target) || Count == 0) return;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Paste components");
            try
            {
                // Required components can be inserted implicitly by AddComponent.
                for (int i = 0; i < Count; i++)
                {
                    var entry = entries[i];
                    target.GetComponents(entry.Type, destination);
                    Component component = entry.Ordinal < destination.Count ? destination[entry.Ordinal] :
                        Undo.AddComponent(target, entry.Type);
                    if (!component) throw new InvalidOperationException("Unity could not attach the requested component.");
                    Undo.RegisterCompleteObjectUndo(component, "Paste component values");
                    if (entry.Data)
                    {
                        if (!entry.Data.ApplyTo(component)) throw new InvalidOperationException("Unity could not apply the component preset.");
                    }
                    else
                    {
                        var transform = (Transform)component;
                        transform.SetLocalPositionAndRotation(entry.Position, entry.Rotation);
                        transform.localScale = entry.Scale;
                    }
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
            finally { destination.Clear(); }
        }

        static void Release(Entry entry)
        {
            if (entry.Data) UnityEngine.Object.DestroyImmediate(entry.Data);
            entry.Data = null;
            entry.Type = null;
        }

        public void Dispose()
        {
            foreach (var entry in entries) Release(entry);
            entries.Clear();
            ordinals.Clear();
            Count = 0;
        }
    }
}
