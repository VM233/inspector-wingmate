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
