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
        // Presets can only adopt values of a compatible type. Retain a small number of
        // native buffers for alternating clipboard types, up to the next scene close.
        const int SpareCapacity = 16;
        readonly List<(Type type, Preset preset)> spares = new(SpareCapacity);
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
                    var reusable = TakeSpare(type);
                    Recycle(entry);
                    entry.Type = type;
                    entry.Data = reusable;
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
                    if (!entry.Data.IsValid()) { Destroy(entry); continue; }
                }
                ordinals.TryGetValue(type, out entry.Ordinal);
                ordinals[type] = entry.Ordinal + 1;
                Count++;
            }
            for (int i = Count; i < entries.Count; i++) Recycle(entries[i]);
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

        Preset TakeSpare(Type type)
        {
            for (int i = spares.Count - 1; i >= 0; i--)
            {
                if (spares[i].type != type) continue;
                var preset = spares[i].preset;
                spares.RemoveAt(i);
                return preset;
            }
            return null;
        }

        void Recycle(Entry entry)
        {
            if (entry.Data)
            {
                if (spares.Count == SpareCapacity)
                {
                    UnityEngine.Object.DestroyImmediate(spares[0].preset);
                    spares.RemoveAt(0);
                }
                spares.Add((entry.Type, entry.Data));
            }
            entry.Data = null;
            entry.Type = null;
        }

        internal void ReleaseSparePresets()
        {
            foreach (var item in spares)
                if (item.preset) UnityEngine.Object.DestroyImmediate(item.preset);
            spares.Clear();
        }

        static void Destroy(Entry entry)
        {
            if (entry.Data) UnityEngine.Object.DestroyImmediate(entry.Data);
            entry.Data = null;
            entry.Type = null;
        }

        public void Dispose()
        {
            foreach (var entry in entries) Destroy(entry);
            ReleaseSparePresets();
            entries.Clear();
            ordinals.Clear();
            Count = 0;
        }
    }
}
