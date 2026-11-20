using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
