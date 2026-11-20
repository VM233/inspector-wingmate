using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VM233.InspectorWingmate
{
    public sealed class InspectorWingmateWindow : EditorWindow
    {
        const string DragKey = "com.vm233.inspector-wingmate.components";
        static readonly GUIContent windowTitle = new("Inspector Wingmate");
        static readonly ComponentClipboard clipboard = new(), drag = new();
        static readonly DragAndDrop.HierarchyDropHandlerV2 drop = Drop;
        readonly List<Component> components = new(), scratch = new();
        readonly List<ComponentPanel> panels = new();
        readonly ComponentSelection selection = new();
        [SerializeField] GameObject pinned;
        [SerializeField] bool locked;
        GameObject shown;
        ToolbarSearchField search;
        VisualElement chips;
        ScrollView details;
        Label status;
        ToolbarToggle pin;
        ToolbarButton copy, paste;
        bool presentationDirty = true;
        static event System.Action ClipboardChanged;

        [InitializeOnLoadMethod]
        static void Register()
        {
            DragAndDrop.AddDropHandlerV2(drop);
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
            EditorApplication.quitting += Unregister;
        }

        static void Unregister()
        {
            DragAndDrop.RemoveDropHandlerV2(drop);
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
            EditorApplication.quitting -= Unregister;
            clipboard.Dispose();
            drag.Dispose();
        }

        [MenuItem("Window/General/Inspector Wingmate")]
        static void OpenFromMenu() => Open();

        public static InspectorWingmateWindow Open()
        {
            var window = GetWindow<InspectorWingmateWindow>();
            if (window.titleContent.text != windowTitle.text) window.titleContent = windowTitle;
            return window;
        }

        public void CreateGUI()
        {
            Disconnect();
            rootVisualElement.Clear();
            presentationDirty = true;
            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(ShowAll) { text = "All" });
            pin = new ToolbarToggle { text = "Lock" };
            pin.SetValueWithoutNotify(locked);
            pin.RegisterValueChangedCallback(ChangeLock);
            toolbar.Add(pin);
            copy = new ToolbarButton(Copy) { text = "Copy" };
            paste = new ToolbarButton(Paste) { text = "Paste" };
            toolbar.Add(copy);
            toolbar.Add(paste);
            search = new ToolbarSearchField { tooltip = "Find serialized fields" };
            search.style.flexGrow = 1;
            search.RegisterValueChangedCallback(SearchChanged);
            toolbar.Add(search);
            rootVisualElement.Add(toolbar);
            chips = new VisualElement();
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            rootVisualElement.Add(chips);
            status = new Label();
            rootVisualElement.Add(status);
            details = new ScrollView();
            details.style.flexGrow = 1;
            rootVisualElement.Add(details);
            Selection.selectionChanged += Refresh;
            EditorApplication.hierarchyChanged += Refresh;
            Undo.undoRedoPerformed += OnUndo;
            ClipboardChanged += UpdateCommands;
            Refresh();
        }

        void OnDisable() => Disconnect();

        void Disconnect()
        {
            Selection.selectionChanged -= Refresh;
            EditorApplication.hierarchyChanged -= Refresh;
            Undo.undoRedoPerformed -= OnUndo;
            ClipboardChanged -= UpdateCommands;
            foreach (var panel in panels) panel.Dispose();
            panels.Clear();
            components.Clear();
            shown = null;
        }

        void ChangeLock(ChangeEvent<bool> change)
        {
            locked = change.newValue && shown;
            pinned = locked ? shown : null;
            Refresh();
        }
        void OnUndo() { Refresh(); }
        void SearchChanged(ChangeEvent<string> change) => InvalidateSelection();
        internal bool Searching => !string.IsNullOrEmpty(search.value);
        internal void InvalidateSelection()
        {
            presentationDirty = true;
            ShowSelection();
        }

        void Refresh()
        {
            if (locked && !pinned) locked = false;
            pin.SetValueWithoutNotify(locked);
            var target = locked ? pinned : Selection.activeGameObject;
            scratch.Clear();
            if (target) target.GetComponents(scratch);
            bool changed = shown != target || components.Count != scratch.Count;
            if (!changed)
                for (int i = 0; i < scratch.Count; i++)
                    if (components[i] != scratch[i]) { changed = true; break; }
            if (changed)
            {
                presentationDirty = true;
                shown = target;
                components.Clear();
                components.AddRange(scratch);
                selection.Reset(components.Count);
                while (panels.Count < components.Count)
                {
                    var panel = new ComponentPanel(this, panels.Count);
                    panels.Add(panel);
                    chips.Add(panel.Chip);
                    details.Add(panel.Body);
                }
                for (int i = 0; i < panels.Count; i++)
                    panels[i].Bind(i < components.Count ? components[i] : null);
            }
            scratch.Clear();
            ShowSelection();
            UpdateCommands();
        }

        internal void ShowSelection()
        {
            if (!presentationDirty) return;
            presentationDirty = false;
            string query = search.value;
            bool any = false;
            for (int i = 0; i < panels.Count; i++)
            {
                bool used = i < components.Count;
                any |= panels[i].Show(used && selection.Includes(i), used && selection.IsSelected(i), query);
            }
            status.text = !shown ? "Select a GameObject." : !any ? "No matching fields." : string.Empty;
            ComponentPanel.Display(status, !any);
        }

        internal void Choose(int index, bool toggle, bool range)
        {
            bool clearSearch = Searching;
            if (!toggle && !range && !clearSearch && selection.Only(index)) return;
            selection.Select(index, toggle, range);
            if (clearSearch) search.SetValueWithoutNotify(string.Empty);
            InvalidateSelection();
        }
        void ShowAll()
        {
            bool clearSearch = Searching;
            if (selection.All && !clearSearch) return;
            selection.Reset(components.Count);
            if (clearSearch) search.SetValueWithoutNotify(string.Empty);
            InvalidateSelection();
        }
        void Copy()
        {
            clipboard.Capture(components, selection);
            ClipboardChanged?.Invoke();
        }
        void Paste() { clipboard.Paste(shown); Refresh(); }
        void UpdateCommands()
        {
            copy.SetEnabled(shown);
            paste.SetEnabled(shown && !EditorUtility.IsPersistent(shown) && clipboard.Count != 0);
            pin.SetEnabled(shown);
        }

        internal void BeginDrag(int index)
        {
            if (!selection.IsSelected(index)) Choose(index, false, false);
            drag.Capture(components, selection);
            if (drag.Count == 0) return;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DragKey, drag);
            DragAndDrop.StartDrag("Copy components");
        }

        static DragAndDropVisualMode Drop(EntityId id, HierarchyDropFlags flags, Transform forcedParent, bool perform)
        {
            if (!ReferenceEquals(DragAndDrop.GetGenericData(DragKey), drag)) return DragAndDropVisualMode.None;
            var target = EditorUtility.EntityIdToObject(id) as GameObject;
            if (target && ((flags & HierarchyDropFlags.DropUpon) == 0 || EditorUtility.IsPersistent(target)))
                return DragAndDropVisualMode.Rejected;
            if (!perform) return DragAndDropVisualMode.Copy;
            if (target) drag.Paste(target);
            else target = PasteIntoNewObject(drag, forcedParent);
            DragAndDrop.AcceptDrag();
            Selection.activeGameObject = target;
            return DragAndDropVisualMode.Copy;
        }

        internal static GameObject PasteIntoNewObject(ComponentClipboard source, Transform parent)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            var target = new GameObject("Components");
            try
            {
                Undo.RegisterCreatedObjectUndo(target, "Create component target");
                StageUtility.PlaceGameObjectInCurrentStage(target);
                if (parent) target.transform.SetParent(parent, false);
                source.Paste(target);
                Undo.CollapseUndoOperations(group);
                return target;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                throw;
            }
        }
    }
}
