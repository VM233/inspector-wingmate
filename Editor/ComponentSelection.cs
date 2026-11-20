using System;
using System.Collections.Generic;

namespace VM233.InspectorWingmate
{
    internal sealed class ComponentSelection
    {
        readonly List<bool> selected = new();
        int anchor = -1;
        int count;

        internal bool All => count == 0;
        internal bool Only(int index) => count == 1 && selected[index];
        internal bool Includes(int index) => All || selected[index];
        internal bool IsSelected(int index) => selected[index];
        internal int Anchor { get => anchor; set => anchor = value; }

        internal void Reset(int size)
        {
            selected.Clear();
            for (int i = 0; i < size; i++) selected.Add(false);
            count = 0;
            anchor = -1;
        }

        internal void Select(int index, bool toggle, bool range)
        {
            int start = range && anchor >= 0 ? anchor : index;
            if (!toggle)
            {
                for (int i = 0; i < selected.Count; i++) selected[i] = false;
                count = 0;
            }
            if (range)
            {
                for (int i = Math.Min(start, index); i <= Math.Max(start, index); i++)
                    Set(i, true);
            }
            else Set(index, !toggle || !selected[index]);
            if (!range || anchor < 0) anchor = index;
        }

        void Set(int index, bool value)
        {
            if (selected[index] == value) return;
            selected[index] = value;
            count += value ? 1 : -1;
        }

        internal static bool Matches(string label, string path, string query, bool fuzzy = true) =>
            string.IsNullOrEmpty(query) || label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            path.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
            fuzzy && (InOrder(label, query) || InOrder(path, query));

        static bool InOrder(string text, string query)
        {
            int next = 0;
            for (int i = 0; i < query.Length; i++)
            {
                char wanted = query[i];
                if (char.IsWhiteSpace(wanted)) continue;
                wanted = char.ToUpperInvariant(wanted);
                while (next < text.Length && char.ToUpperInvariant(text[next]) != wanted) next++;
                if (next == text.Length) return false;
                next++;
            }
            return true;
        }
    }
}
