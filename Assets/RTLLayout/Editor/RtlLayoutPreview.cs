using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RTLLayout.Editor
{
    /// <summary>编辑器临时预览：通过 AnimationMode 还原属性，不修改已烘焙数据。</summary>
    [InitializeOnLoad]
    public static class RtlLayoutPreview
    {
        static RtlLayoutRoot active;
        static bool ownsAnimationMode;
        static readonly Dictionary<RtlLayoutRoot, bool> directions = new Dictionary<RtlLayoutRoot, bool>();
        /// <summary>是否由本预览器持有 AnimationMode；用于锁定 Inspector 的配置与烘焙操作。</summary>
        public static bool IsActive => ownsAnimationMode;
        /// <summary>判断当前预览是否属于指定根，避免关闭无关 Inspector 时结束其他预览。</summary>
        public static bool IsPreviewing(RtlLayoutRoot root) => ownsAnimationMode && active == root;
        /// <summary>保存、切换 Play、关闭 Prefab 或脚本重载前恢复，防止临时属性写入资产。</summary>
        static RtlLayoutPreview()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Stop(); };
            EditorSceneManager.sceneSaving += (scene, path) => Stop();
            PrefabStage.prefabSaving += root => Stop();
            PrefabStage.prefabStageClosing += stage => Stop();
        }
        /// <summary>仅允许已烘焙的编辑期根预览；登记临时属性后应用方向，不调用 Capture/Bake。</summary>
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
        /// <summary>把进入预览前的属性值登记到 AnimationMode，而不是覆盖序列化的 LTR 缓存。</summary>
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
        /// <summary>先还原方向状态，再让 AnimationMode 恢复进入预览前的实际编辑值，包括未烘焙修改。</summary>
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
    // 普通资产保存前还原临时值，包含直接选中的 Prefab 资产。
    public sealed class RtlPreviewSaveGuard : AssetModificationProcessor
    {
        /// <summary>普通资产保存也先结束预览，确保磁盘文件不会包含临时镜像值。</summary>
        static string[] OnWillSaveAssets(string[] paths) { RtlLayoutPreview.Stop(); return paths; }
    }
}
