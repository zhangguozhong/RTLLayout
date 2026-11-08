using System;
using UnityEditor;
using UnityEngine;

namespace RTLLayout.Editor
{
    [CustomEditor(typeof(RtlLayoutRoot)), CanEditMultipleObjects]
    public sealed class RtlLayoutRootEditor : UnityEditor.Editor
    {
        private bool showBaselines;
        private readonly System.Collections.Generic.HashSet<int> expandedNodes = new System.Collections.Generic.HashSet<int>();
        public override void OnInspectorGUI()
        {
            DrawPreview();
            serializedObject.Update();
            using (new EditorGUI.DisabledScope(RtlLayoutPreview.IsActive))
            {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CollectedComponents"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("IgnoredNodes"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("MirroredNodes"), true);
            EditorGUILayout.HelpBox("Collected Components supports RectTransform, UGUI Text and LayoutGroup. Ignored Nodes excludes whole subtrees. Mirrored Nodes reverses localScale.x, including the visual effect on descendants: use a dedicated visual node. Out-of-scope references are ignored. Restore LTR before editing/baking.", MessageType.Info);
            serializedObject.ApplyModifiedProperties();
            }
            DrawReadOnlyBaselines();
            using (new EditorGUI.DisabledScope(Application.isPlaying || RtlLayoutPreview.IsActive))
            {
                var discover = GUILayout.Button("Discover Components & Bake LTR (Reset Selection)");
                var configured = GUILayout.Button("Bake Selected Components (Keep Edited Lists)");
                if (!discover && !configured) return;
                foreach (var selected in targets)
                {
                    var root = (RtlLayoutRoot)selected;
                    var roots = root.GetComponentsInChildren<RtlLayoutRoot>(true);
                    Undo.RecordObjects(roots, "Bake LTR Layout");
                    try
                    {
                        if (discover) root.CaptureLayout();
                        else root.BakeConfiguredLayout();
                    }
                    catch (InvalidOperationException exception)
                    {
                        Debug.LogError(exception.Message, root);
                        continue;
                    }
                    foreach (var changed in roots)
                    {
                        EditorUtility.SetDirty(changed);
                        if (PrefabUtility.IsPartOfPrefabInstance(changed))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
                    }
                }
                serializedObject.Update();
            }
        }
        private void DrawPreview()
        {
            var root = (RtlLayoutRoot)target;
            EditorGUILayout.LabelField("Layout Preview", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(Application.isPlaying || targets.Length != 1 || root.BakedNodeCount == 0))
            {
                var value = RtlLayoutPreview.IsPreviewing(root) ? (root.IsRightToLeft ? 1 : 0) : -1;
                var next = GUILayout.Toolbar(value, new[] { "LTR", "RTL" });
                if (next != value)
                {
                    try { RtlLayoutPreview.SetDirection(root, next == 1); }
                    catch (InvalidOperationException exception) { Debug.LogWarning(exception.Message, root); }
                }
            }
            if (RtlLayoutPreview.IsPreviewing(root))
            {
                EditorGUILayout.HelpBox("Temporary preview. Cached LTR data is unchanged. Exit preview before editing; saving or leaving this Inspector restores the original layout.", MessageType.Info);
                if (GUILayout.Button("Exit Preview / Restore Layout")) RtlLayoutPreview.Stop();
            }
            else if (root.BakedNodeCount == 0) EditorGUILayout.HelpBox("Collect and bake the LTR layout to enable preview.", MessageType.Info);
        }
        private void OnDisable()
        {
            foreach (var selected in targets)
                if (selected is RtlLayoutRoot root && RtlLayoutPreview.IsPreviewing(root)) { RtlLayoutPreview.Stop(); break; }
        }
        private void DrawReadOnlyBaselines()
        {
            var baselines = serializedObject.FindProperty("bakedLayout");
            showBaselines = EditorGUILayout.Foldout(showBaselines, "Baked LTR Baselines (Read Only) — " + baselines.arraySize, true);
            if (!showBaselines) return;
            EditorGUI.indentLevel++;
            for (var index = 0; index < baselines.arraySize; index++)
            {
                var entry = baselines.GetArrayElementAtIndex(index);
                var node = entry.FindPropertyRelative("target").objectReferenceValue;
                var expanded = EditorGUILayout.Foldout(expandedNodes.Contains(index), node != null ? node.name : "Missing Node", true);
                if (!expanded) { expandedNodes.Remove(index); continue; }
                expandedNodes.Add(index);
                var property = entry.Copy();
                var end = entry.GetEndProperty();
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(true))
                {
                    if (property.NextVisible(true))
                        do { EditorGUILayout.PropertyField(property, true); }
                        while (property.NextVisible(false) && !SerializedProperty.EqualContents(property, end));
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }
    }
}
