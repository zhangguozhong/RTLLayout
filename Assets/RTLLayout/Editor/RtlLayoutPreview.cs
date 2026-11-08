using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RTLLayout.Editor
{
    /// <summary>Temporary editor values. AnimationMode restores properties without touching baked data.</summary>
    [InitializeOnLoad]
    public static class RtlLayoutPreview
    {
        static RtlLayoutRoot active;
        static bool ownsAnimationMode;
        static readonly Dictionary<RtlLayoutRoot, bool> directions = new Dictionary<RtlLayoutRoot, bool>();
        public static bool IsActive => ownsAnimationMode;
        public static bool IsPreviewing(RtlLayoutRoot root) => ownsAnimationMode && active == root;
        static RtlLayoutPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Stop(); };
            EditorSceneManager.sceneSaving += (scene, path) => Stop();
            PrefabStage.prefabSaving += root => Stop();
            PrefabStage.prefabStageClosing += stage => Stop();
        }
        public static void SetDirection(RtlLayoutRoot root, bool rtl)
        {
            if (Application.isPlaying || root == null || root.BakedNodeCount == 0)
                throw new InvalidOperationException("Bake the LTR layout before previewing in edit mode.");
            if (!IsPreviewing(root))
            {
                Stop();
                if (AnimationMode.InAnimationMode())
                    throw new InvalidOperationException("Finish the current animation preview before starting RTL preview.");
                if (root.IsRightToLeft)
                    throw new InvalidOperationException("Restore LTR before starting an editor preview.");
                active = root;
                AnimationMode.StartAnimationMode(); ownsAnimationMode = true;
                try
                {
                    foreach (var nested in root.GetComponentsInChildren<RtlLayoutRoot>(true)) directions[nested] = nested.IsRightToLeft;
                    foreach (var node in root.GetComponentsInChildren<Transform>(true))
                    {
                        RegisterVector(node,"m_LocalScale",3); RegisterVector(node,"m_LocalPosition",3);
                    }
                    foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                    {
                        RegisterVector(rect,"m_AnchorMin",2); RegisterVector(rect,"m_AnchorMax",2);
                        RegisterVector(rect,"m_Pivot",2); RegisterVector(rect,"m_AnchoredPosition",2);
                        RegisterVector(rect,"m_SizeDelta",2);
                    }
                    foreach (var text in root.GetComponentsInChildren<Text>(true)) Register(text,"m_FontData.m_Alignment");
                    foreach (var group in root.GetComponentsInChildren<LayoutGroup>(true))
                    {
                        foreach (var field in new[]{"m_Padding.m_Left","m_Padding.m_Right","m_Padding.m_Top","m_Padding.m_Bottom","m_ChildAlignment","m_ReverseArrangement","m_StartCorner"}) Register(group,field);
                    }
                }
                catch { Stop(); throw; }
            }
            try { root.SetRightToLeft(rtl); Canvas.ForceUpdateCanvases(); SceneView.RepaintAll(); }
            catch { Stop(); throw; }
        }
        static void RegisterVector(Component component,string path,int dimensions)
        { for (int i=0;i<dimensions;i++) Register(component,path+"."+"xyz"[i]); }
        static void Register(Component component,string path)
        {
            var property = new SerializedObject(component).FindProperty(path);
            if (property == null) return;
            string value;
            if (property.propertyType == SerializedPropertyType.Float) value=property.floatValue.ToString("R",CultureInfo.InvariantCulture);
            else if(property.propertyType == SerializedPropertyType.Boolean) value=property.boolValue ? "1" : "0";
            else value=property.intValue.ToString(CultureInfo.InvariantCulture);
            var binding=EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(component.transform,active.transform),component.GetType(),path);
            AnimationMode.AddPropertyModification(binding,new PropertyModification { target=component,propertyPath=path,value=value },true);
        }
        public static void Stop()
        {
            if (!ownsAnimationMode) return;
            try
            {
                if(active != null) active.SetRightToLeft(false);
                foreach(var pair in directions) if(pair.Key != null) pair.Key.SetRightToLeft(pair.Value);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                ownsAnimationMode=false; active=null; directions.Clear();
                SceneView.RepaintAll();
            }
        }
    }
    // Restores temporary values before ordinary asset saves, including a selected prefab asset.
    public sealed class RtlPreviewSaveGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths) { RtlLayoutPreview.Stop(); return paths; }
    }
}
