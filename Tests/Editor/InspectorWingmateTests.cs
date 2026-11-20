using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace VM233.InspectorWingmate.Tests
{
    public sealed class InspectorWingmateTests
    {
        [Test]
        public void RepeatedRangeToggleAndFieldSearchAllocateZeroBytes()
        {
            var selection = new ComponentSelection();
            selection.Reset(16);
            Exercise(selection);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool matched = true;
            for (int i = 0; i < 1024; i++) matched &= Exercise(selection);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(matched, Is.True);
            Assert.That(bytes, Is.Zero);
        }

        static bool Exercise(ComponentSelection selection)
        {
            selection.Reset(16);
            selection.Select(2, false, false);
            selection.Select(9, false, true);
            selection.Select(4, true, false);
            return selection.Includes(2) && !selection.Includes(4) && selection.Includes(9) &&
                !selection.Includes(10) && ComponentSelection.Matches("Angular Drag", "body.angularDrag", "angular");
        }

        [Test]
        public void ReturningToAnObjectRestoresTheSameComponentAndItsSearch()
        {
            var previous = Selection.activeObject;
            var a = new GameObject("First view");
            var b = new GameObject("Second view");
            var window = ScriptableObject.CreateInstance<InspectorWingmateWindow>();
            try
            {
                var removed = a.AddComponent<BoxCollider>();
                var kept = a.AddComponent<BoxCollider>();
                Selection.activeGameObject = a;
                window.CreateGUI();
                var query = window.rootVisualElement.Q<ToolbarSearchField>();
                var views = window.rootVisualElement.Q<ScrollView>().contentContainer;
                window.Inspect(kept);
                query.SetValueWithoutNotify("c n t r");
                window.InvalidateSelection();
                window.Inspect(b.transform);
                Assert.That(query.value, Is.Empty);
                UnityEngine.Object.DestroyImmediate(removed);
                Selection.activeGameObject = a;
                window.Refresh();
                Assert.That(query.value, Is.EqualTo("c n t r"));
                Assert.That(views[0].style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(views[1].style.display.value, Is.EqualTo(DisplayStyle.Flex));
                var options = window.rootVisualElement.Q<ToolbarMenu>().menu.MenuItems();
                foreach (var item in options)
                    if (item is DropdownMenuAction action && action.name == "Fuzzy field search") action.Execute();
                Assert.That(views[1].style.display.value, Is.EqualTo(DisplayStyle.None));
                window.Inspect(a.transform);
                Assert.That(query.value, Is.Empty);
                Assert.That(views[0].style.display.value, Is.EqualTo(DisplayStyle.Flex));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                Selection.activeObject = previous;
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void ReturningToAWarmObjectReusesItsBoundInspectorControls()
        {
            var a = new GameObject("Bound object A");
            var b = new GameObject("Bound object B");
            var window = ScriptableObject.CreateInstance<InspectorWingmateWindow>();
            var cache = new ComponentViewCache();
            try
            {
                a.AddComponent<BoxCollider>();
                b.AddComponent<SphereCollider>();
                var first = new List<Component>();
                var second = new List<Component>();
                a.GetComponents(first);
                b.GetComponents(second);
                var chips = new VisualElement();
                var views = new VisualElement();
                cache.Activate(a, window, first, chips, views, false);
                var box = cache[1];
                box.Show(true, true, "Center");
                cache.Activate(b, window, second, chips, views, false);
                var sphere = cache[1];
                bool Cycle()
                {
                    cache.Activate(a, window, first, chips, views, false);
                    bool valid = ReferenceEquals(box, cache[1]) && ReferenceEquals(views[1], box.Body);
                    cache.Activate(b, window, second, chips, views, false);
                    return valid && ReferenceEquals(sphere, cache[1]) && ReferenceEquals(views[1], sphere.Body);
                }
                Cycle();
                Cycle();
                long before = GC.GetAllocatedBytesForCurrentThread();
                bool reused = true;
                for (int i = 0; i < 128; i++) reused &= Cycle();
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(reused, Is.True);
                Assert.That(views.childCount, Is.EqualTo(2));
                TestContext.WriteLine($"256 object reactivations with UI Toolkit controls: {bytes} managed bytes.");
                Assert.That(bytes, Is.Zero);
                cache.ReleaseScene(a.scene);
                Assert.That(cache.Count, Is.Zero);
            }
            finally
            {
                cache.Clear();
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void WarmedObjectViewSwitchAndBoundFieldFilterAllocateZeroBytes()
        {
            var a = new GameObject("Saved view A");
            var b = new GameObject("Saved view B");
            var window = ScriptableObject.CreateInstance<InspectorWingmateWindow>();
            try
            {
                var box = a.AddComponent<BoxCollider>();
                var first = new List<Component>();
                var second = new List<Component>();
                a.GetComponents(first);
                b.GetComponents(second);
                var history = new ObjectViewHistory();
                var selection = new ComponentSelection();
                selection.Reset(first.Count);
                selection.Select(1, false, false);
                history.Save(a, first, selection, "c n t r");
                selection.Reset(second.Count);
                history.Save(b, second, selection, "pos");
                using var panel = new ComponentPanel(window, 0);
                panel.Bind(box);
                bool Cycle()
                {
                    string left = history.Restore(a, first, selection);
                    bool found = selection.Only(1) && panel.Show(true, true, left);
                    history.Save(a, first, selection, left);
                    string right = history.Restore(b, second, selection);
                    history.Save(b, second, selection, right);
                    return found && panel.Show(true, true, "s i z e") && right == "pos";
                }
                Cycle();
                Cycle();
                long before = GC.GetAllocatedBytesForCurrentThread();
                bool matched = true;
                for (int i = 0; i < 256; i++) matched &= Cycle();
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(matched, Is.True);
                Assert.That(bytes, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(a);
                UnityEngine.Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void StableInspectorSelectionAndRepeatedIsolationAllocateZeroBytes()
        {
            var previous = Selection.activeObject;
            var target = new GameObject("Inspection target");
            var window = ScriptableObject.CreateInstance<InspectorWingmateWindow>();
            try
            {
                target.AddComponent<BoxCollider>();
                Selection.activeGameObject = target;
                window.CreateGUI();
                window.Choose(1, false, false);
                window.ShowSelection();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1024; i++)
                {
                    window.Choose(1, false, false);
                    window.ShowSelection();
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(bytes, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                Selection.activeObject = previous;
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void RepeatedClipboardCaptureReusesTheNativePresetAndStoresTheLatestValue()
        {
            var target = new GameObject("Clipboard allocation target");
            try
            {
                var source = target.AddComponent<BoxCollider>();
                var components = new List<Component>();
                target.GetComponents(components);
                var selection = new ComponentSelection();
                selection.Reset(components.Count);
                selection.Select(1, false, false);
                using var clipboard = new ComponentClipboard();
                clipboard.Capture(components, selection);
                clipboard.Capture(components, selection);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                {
                    source.center = new Vector3(i, 1, 2);
                    clipboard.Capture(components, selection);
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                source.center = Vector3.zero;
                clipboard.Paste(target);
                Assert.That(source.center, Is.EqualTo(new Vector3(127, 1, 2)));
                Assert.That(bytes, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
        }

        [Test]
        public void AlternatingClipboardTypesReusePresetsAndStillPasteAfterSceneCleanup()
        {
            var target = new GameObject("Mixed component clipboard");
            try
            {
                var box = target.AddComponent<BoxCollider>();
                var sphere = target.AddComponent<SphereCollider>();
                var components = new List<Component>();
                target.GetComponents(components);
                var selection = new ComponentSelection();
                selection.Reset(components.Count);
                using var clipboard = new ComponentClipboard();
                void CopyPair()
                {
                    selection.Select(1, false, false);
                    clipboard.Capture(components, selection);
                    selection.Select(2, false, false);
                    clipboard.Capture(components, selection);
                }
                CopyPair();
                CopyPair();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 128; i++)
                {
                    sphere.radius = i + 1;
                    box.center = new Vector3(i, 1, 2);
                    CopyPair();
                }
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                clipboard.ReleaseSparePresets();
                sphere.radius = 1;
                clipboard.Paste(target);
                Assert.That(sphere.radius, Is.EqualTo(128f));
                Assert.That(bytes, Is.Zero);
            }
            finally { UnityEngine.Object.DestroyImmediate(target); }
        }

        [Test]
        public void ChangingIsolationAndSearchingDisplayTheSelectedComponentsAndFields()
        {
            var previous = Selection.activeObject;
            var target = new GameObject("Inspection changes");
            var window = ScriptableObject.CreateInstance<InspectorWingmateWindow>();
            try
            {
                target.AddComponent<BoxCollider>();
                Selection.activeGameObject = target;
                window.CreateGUI();
                var query = window.rootVisualElement.Q<ToolbarSearchField>();
                var views = window.rootVisualElement.Q<ScrollView>().contentContainer;
                window.Choose(0, false, false);
                window.Choose(1, false, false);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 256; i++)
                {
                    window.Choose(0, false, false);
                    window.Choose(1, false, false);
                }
                long isolationBytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(views[0].style.display.value, Is.EqualTo(DisplayStyle.None));
                Assert.That(views[1].style.display.value, Is.EqualTo(DisplayStyle.Flex));
                query.SetValueWithoutNotify("Center");
                window.InvalidateSelection();
                bool foundCenter = false;
                views[1].Query<PropertyField>().ForEach(field =>
                {
                    if (field.bindingPath == "m_Center" && field.style.display.value == DisplayStyle.Flex)
                        foundCenter = true;
                });
                Assert.That(foundCenter, Is.True);
                query.SetValueWithoutNotify("NoSuchWingmateField");
                window.InvalidateSelection();
                Assert.That(views[1].style.display.value, Is.EqualTo(DisplayStyle.None));
                TestContext.WriteLine($"512 component changes, including UI Toolkit style changes: {isolationBytes} managed bytes.");
                Assert.That(isolationBytes, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                Selection.activeObject = previous;
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void ClipboardFreezesValuesAndPastesMultipleComponentsWithUndo()
        {
            var source = new GameObject("Source");
            var target = new GameObject("Destination");
            try
            {
                var first = source.AddComponent<BoxCollider>();
                var second = source.AddComponent<BoxCollider>();
                first.center = Vector3.left;
                second.center = Vector3.right;
                using var clipboard = new ComponentClipboard();
                var components = new List<Component>();
                source.GetComponents(components);
                var selection = new ComponentSelection();
                selection.Reset(components.Count);
                selection.Select(1, false, false);
                selection.Select(2, true, false);
                clipboard.Capture(components, selection);
                first.center = Vector3.up;
                clipboard.Paste(target);
                var pasted = target.GetComponents<BoxCollider>();
                Assert.That(pasted.Length, Is.EqualTo(2));
                Assert.That(pasted[0].center, Is.EqualTo(Vector3.left));
                Assert.That(pasted[1].center, Is.EqualTo(Vector3.right));
                Undo.PerformUndo();
                Assert.That(target.GetComponent<BoxCollider>(), Is.Null);
                var created = InspectorWingmateWindow.PasteIntoNewObject(clipboard, target.transform);
                Assert.That(created.transform.parent, Is.EqualTo(target.transform));
                Assert.That(created.GetComponents<BoxCollider>().Length, Is.EqualTo(2));
                Undo.PerformUndo();
                Assert.That(created == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
