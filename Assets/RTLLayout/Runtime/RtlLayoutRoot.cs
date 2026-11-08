using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTLLayout
{
    /// <summary>优先使用编辑期烘焙的 LTR 数据进行横向镜像，运行时只为动态新增节点兜底。</summary>
    [DisallowMultipleComponent]
    public sealed class RtlLayoutRoot : MonoBehaviour
    {
        /// <summary>参与适配的组件引用；编辑选择后通过 BakeConfiguredLayout 保存。</summary>
        public List<Component> CollectedComponents = new List<Component>();
        /// <summary>忽略整个节点子树，优先于收集与缩放反转，也阻止嵌套方向传播。</summary>
        public List<Transform> IgnoredNodes = new List<Transform>();
        /// <summary>显式反转原始 localScale.x 的节点；会影响子节点视觉，应选专用图片节点。</summary>
        public List<Transform> MirroredNodes = new List<Transform>();
        [SerializeField] private bool hasConfiguredSelection;
        [SerializeField] private List<Component> knownComponents = new List<Component>();
        /// <summary>编辑期按当前人工选择重新烘焙 LTR；保留选择列表，拒绝 Play 模式及未恢复的 RTL。</summary>
        public void BakeConfiguredLayout()
        {
            EnsureCanCapture();
            CaptureValidated(false);
        }
        [SerializeField] private List<LayoutData> bakedLayout = new List<LayoutData>();
        // 基准按 Transform 身份共享，新增根、跨根重挂和对象池复用时仍使用同一份原始数据。
        private static readonly Dictionary<Transform, Snapshot> Snapshots = new Dictionary<Transform, Snapshot>();
        private static readonly List<Transform> DeadTransforms = new List<Transform>();
        /// <summary>当前应用方向；在业务完成 LTR 初始布局后主动设置。</summary>
        public bool IsRightToLeft { get; private set; }
        /// <summary>已序列化的布局快照条数，用于判断预收集是否完成。</summary>
        public int BakedNodeCount => bakedLayout.Count;

        /// <summary>编辑期重新发现并烘焙 LTR 布局，调用后需保存 Prefab；Play 模式禁止调用。</summary>
        public void CaptureLayout()
        {
            EnsureCanCapture();
            CaptureValidated(true);
        }

        private void EnsureCanCapture()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Layout baking is an editor authoring operation and cannot run in play mode.");
            ValidateCapture(transform);
        }

        private bool IsIgnored(Transform target)
        {
            foreach (var ignored in IgnoredNodes)
                if (ignored != null && (ignored == transform || ignored.IsChildOf(transform)) &&
                    (target == ignored || target.IsChildOf(ignored))) return true;
            return false;
        }

        /// <summary>已知但被手工移除的组件不重新加入；真正新增组件允许运行时兜底。</summary>
        private bool IsSelected(Component component)
        {
            return component != null && (!hasConfiguredSelection || CollectedComponents.Contains(component) || !knownComponents.Contains(component));
        }

        /// <summary>烘焙前递归检查未镜像状态，防止把 RTL 临时值当作新的 LTR 基准。</summary>
        private void ValidateCapture(Transform current)
        {
            var policy = current.GetComponent<RtlLayoutNode>();
            if (IsIgnored(current) || (policy != null && policy.IgnoreSubtree)) return;
            var root = current.GetComponent<RtlLayoutRoot>();
            if (root != null && root != this)
            {
                root.ValidateCapture(current);
                return;
            }
            if (root != null && root.IsRightToLeft)
                throw new InvalidOperationException("Cannot bake a hierarchy currently in RTL. Restore LTR before capturing.");
            if (Snapshots.TryGetValue(current, out var snapshot) && snapshot.HasMirroredValues)
                throw new InvalidOperationException("Cannot bake currently mirrored nodes as LTR. Restore LTR before capturing.");
            for (var i = 0; i < current.childCount; i++) ValidateCapture(current.GetChild(i));
        }

        /// <summary>替换编辑期基准：发现模式重建选择，配置模式只移除非法或重复引用。</summary>
        private void CaptureValidated(bool discover)
        {
            var captured = new List<LayoutData>();
            var discovered = new List<Component>();
            CaptureNode(transform, true, captured, discovered, discover);
            if (discover) CollectedComponents = new List<Component>(discovered);
            else
            {
                var selected = new HashSet<Component>();
                CollectedComponents.RemoveAll(component => component == null || !discovered.Contains(component) || !selected.Add(component));
            }
            knownComponents = new List<Component>(discovered);
            hasConfiguredSelection = true;
            bakedLayout = captured;
            LoadBakedLayout();
        }

        /// <summary>父根收集嵌套根的外部几何；嵌套根自身组件与内部节点交给子根收集。</summary>
        private void CaptureNode(Transform current, bool isRoot, List<LayoutData> captured, List<Component> discovered, bool discover)
        {
            var policy = current.GetComponent<RtlLayoutNode>();
            if (IsIgnored(current) || (policy != null && policy.IgnoreSubtree)) return;
            var nested = isRoot ? null : current.GetComponent<RtlLayoutRoot>();
            if (policy == null || !policy.IgnoreSelf)
            {
                var data = LayoutData.Capture(current, !isRoot, nested == null);
                captured.Add(data);
                if (!isRoot && data.rect != null) discovered.Add(data.rect);
                if (nested == null)
                {
                    if (data.text != null) discovered.Add(data.text);
                    if (data.layoutGroup != null) discovered.Add(data.layoutGroup);
                }
            }
            if (nested != null)
            {
                nested.CaptureValidated(discover);
                return;
            }
            for (var i = 0; i < current.childCount; i++) CaptureNode(current.GetChild(i), false, captured, discovered, discover);
        }

        /// <summary>加载烘焙基准并为新增节点/组件兜底，随后应用当前方向；重复调用不反复镜像。</summary>
        public void Collect()
        {
            PruneDestroyedTransforms();
            LoadBakedLayout();
            Visit(transform, true);
        }

        /// <summary>从缓存 LTR 基准应用目标方向；不会用已经镜像的当前值再次取反。</summary>
        public void SetRightToLeft(bool value)
        {
            IsRightToLeft = value;
            Collect();
        }

        /// <summary>先按当前层级判定属性所有权，再加载旧 Prefab 快照，避免覆盖后加入子根的基准。</summary>
        private void LoadBakedLayout()
        {
            foreach (var data in bakedLayout)
            {
                if (data == null || data.target == null) continue;
                // 序列化层级可能早于运行时新增的嵌套根。
                // 旧基准进入共享缓存前必须逐属性域校验当前所有权。
                var policy = data.target.GetComponent<RtlLayoutNode>();
                if (IsIgnored(data.target) || (policy != null && (policy.IgnoreSelf || policy.IgnoreSubtree))) continue;
                var geometry = data.ownsGeometry && FindOwningRoot(data.target.parent) == this;
                var components = data.ownsComponents && FindOwningRoot(data.target) == this;
                if (!geometry && !components) continue;
                if (!Snapshots.TryGetValue(data.target, out var snapshot))
                {
                    snapshot = new Snapshot(data, geometry, components);
                    Snapshots.Add(data.target, snapshot);
                }
                else snapshot.LoadBaked(data, geometry, components);
            }
        }

        private static RtlLayoutRoot FindOwningRoot(Transform current)
        {
            var original = current;
            while (current != null)
            {
                var policy = current.GetComponent<RtlLayoutNode>();
                if (policy != null && policy.IgnoreSubtree) return null;
                var root = current.GetComponent<RtlLayoutRoot>();
                if (root != null) return root.IsIgnored(original) ? null : root;
                current = current.parent;
            }
            return null;
        }

        /// <summary>先处理本根所有属性，遇到嵌套根后只传播方向；根自身几何由外部容器管理。</summary>
        private void Visit(Transform current, bool isRoot)
        {
            var policy = current.GetComponent<RtlLayoutNode>();
            if (IsIgnored(current) || (policy != null && policy.IgnoreSubtree)) return;
            var nested = isRoot ? null : current.GetComponent<RtlLayoutRoot>();
            if (policy == null || !policy.IgnoreSelf)
            {
                if (!Snapshots.TryGetValue(current, out var snapshot))
                {
                    snapshot = new Snapshot(LayoutData.Capture(current, true, true));
                    Snapshots.Add(current, snapshot);
                }
                snapshot.CaptureNewComponents(current);
                if (!isRoot)
                {
                    if (IsSelected(current as RectTransform) && (policy == null || policy.MirrorRectTransform) && !IsLayoutOwned(current))
                        snapshot.ApplyGeometry(IsRightToLeft);
                    if (MirroredNodes.Contains(current)) snapshot.ApplyScale(IsRightToLeft);
                }
                if (nested == null) snapshot.ApplyComponents(IsRightToLeft, policy, this);
            }
            if (nested != null)
            {
                nested.SetRightToLeft(IsRightToLeft);
                return;
            }
            for (var i = 0; i < current.childCount; i++) Visit(current.GetChild(i), false);
        }

        /// <summary>已被启用 LayoutGroup 驱动的子节点不再手工镜像位置，避免两个布局所有者争用。</summary>
        private static bool IsLayoutOwned(Transform current)
        {
            if (current.parent == null) return false;
            var group = current.parent.GetComponent<LayoutGroup>();
            if (group == null || !group.isActiveAndEnabled) return false;
            // 与 Unity LayoutGroup 一致：任意忽略器为 false 就参与布局，禁用组件也参与此判定。
            var ignorers = current.GetComponents(typeof(ILayoutIgnorer));
            if (ignorers.Length == 0) return true;
            foreach (var component in ignorers)
                if (!((ILayoutIgnorer)component).ignoreLayout) return true;
            return false;
        }

        private static void PruneDestroyedTransforms()
        {
            DeadTransforms.Clear();
            foreach (var pair in Snapshots) if (pair.Key == null) DeadTransforms.Add(pair.Key);
            foreach (var dead in DeadTransforms) Snapshots.Remove(dead);
        }
        private void OnDestroy() { PruneDestroyedTransforms(); }
        /// <summary>每次运行会话重置共享缓存，也兼容禁用 Domain Reload 的编辑器设置。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSnapshots() { Snapshots.Clear(); DeadTransforms.Clear(); }
        private static TextAnchor MirrorAlignment(TextAnchor value)
        {
            var alignment = (int)value;
            return (TextAnchor)(alignment / 3 * 3 + 2 - alignment % 3);
        }

        /// <summary>可序列化的 LTR 快照；Prefab 实例化时 Unity 自动重映射节点和组件引用。</summary>
        [Serializable]
        private sealed class LayoutData
        {
            public Transform target;
            public bool ownsGeometry;
            public bool ownsComponents;
            public RectTransform rect;
            public Vector2 anchorMin, anchorMax, pivot, anchoredPosition;
            public Vector3 localScale;
            public Text text;
            public TextAnchor textAlignment;
            public LayoutGroup layoutGroup;
            public int paddingLeft, paddingRight, paddingTop, paddingBottom;
            public TextAnchor childAlignment;
            public bool reverseArrangement;
            public GridLayoutGroup.Corner startCorner;
            public bool mirrorRectTransform, mirrorLayoutGroup, mirrorText;

            public static LayoutData Capture(Transform target, bool geometry, bool components)
            {
                var data = new LayoutData { target = target, ownsGeometry = geometry, ownsComponents = components };
                data.localScale = target.localScale;
                data.rect = target as RectTransform;
                if (data.rect != null)
                {
                    data.anchorMin = data.rect.anchorMin; data.anchorMax = data.rect.anchorMax;
                    data.pivot = data.rect.pivot; data.anchoredPosition = data.rect.anchoredPosition;
                }
                data.CaptureComponents(target);
                var policy = target.GetComponent<RtlLayoutNode>();
                data.mirrorRectTransform = policy == null || policy.MirrorRectTransform;
                data.mirrorLayoutGroup = policy == null || policy.MirrorLayoutGroup;
                data.mirrorText = policy == null || policy.MirrorText;
                return data;
            }

            public void CaptureComponents(Transform target)
            {
                text = target.GetComponent<Text>();
                if (text != null) textAlignment = text.alignment;
                layoutGroup = target.GetComponent<LayoutGroup>();
                if (layoutGroup == null) return;
                paddingLeft = layoutGroup.padding.left; paddingRight = layoutGroup.padding.right;
                paddingTop = layoutGroup.padding.top; paddingBottom = layoutGroup.padding.bottom;
                childAlignment = layoutGroup.childAlignment;
                if (layoutGroup is HorizontalLayoutGroup horizontal) reverseArrangement = horizontal.reverseArrangement;
                if (layoutGroup is GridLayoutGroup grid) startCorner = grid.startCorner;
            }
        }

        /// <summary>运行时共享基准按 Transform 身份保存，跨根重挂与对象池复用不从 RTL 状态重采。</summary>
        private sealed class Snapshot
        {
            private readonly HashSet<LayoutData> loadedGeometryBakes = new HashSet<LayoutData>();
            private readonly HashSet<LayoutData> loadedComponentBakes = new HashSet<LayoutData>();
            private LayoutData geometry;
            private LayoutData components;
            private bool geometryMirrored, componentsMirrored, scaleMirrored;
            public bool HasMirroredValues => geometryMirrored || componentsMirrored || scaleMirrored;

            public Snapshot(LayoutData data, bool ownsGeometry = true, bool ownsComponents = true)
            {
                // 暂未拥有的属性域也保留兜底基准，以支持该节点以后移到其他根。
                geometry = data;
                components = data;
                if (ownsGeometry) loadedGeometryBakes.Add(data);
                if (ownsComponents) loadedComponentBakes.Add(data);
            }
            /// <summary>同一份烘焙对象对同一属性域只加载一次，重复 Collect 不覆盖运行时共享状态。</summary>
            public void LoadBaked(LayoutData data, bool ownsGeometry, bool ownsComponents)
            {
                if (ownsGeometry && loadedGeometryBakes.Add(data)) geometry = data;
                if (ownsComponents && loadedComponentBakes.Add(data)) components = data;
            }
            /// <summary>只为新增/替换组件采集基准，保留未变组件原来的对齐与排列值。</summary>
            public void CaptureNewComponents(Transform target)
            {
                var text = target.GetComponent<Text>();
                var group = target.GetComponent<LayoutGroup>();
                if (text == components.text && group == components.layoutGroup) return;
                // 仅其他组件新增或替换时，保留未变化组件的原始基准。
                var updated = LayoutData.Capture(target, false, true);
                if (text == components.text) updated.textAlignment = components.textAlignment;
                if (group == components.layoutGroup)
                {
                    updated.paddingLeft = components.paddingLeft; updated.paddingRight = components.paddingRight;
                    updated.childAlignment = components.childAlignment;
                    updated.reverseArrangement = components.reverseArrangement; updated.startCorner = components.startCorner;
                }
                components = updated;
            }
            /// <summary>只替换横向锚点、轴心与位置，保留实时纵向布局，防止重置列表滚动等业务状态。</summary>
            public void ApplyGeometry(bool rtl)
            {
                var rect = geometry.rect;
                if (rect == null) return;
                rect.anchorMin = new Vector2(rtl ? 1 - geometry.anchorMax.x : geometry.anchorMin.x, rect.anchorMin.y);
                rect.anchorMax = new Vector2(rtl ? 1 - geometry.anchorMin.x : geometry.anchorMax.x, rect.anchorMax.y);
                rect.pivot = new Vector2(rtl ? 1 - geometry.pivot.x : geometry.pivot.x, rect.pivot.y);
                rect.anchoredPosition = new Vector2(rtl ? -geometry.anchoredPosition.x : geometry.anchoredPosition.x, rect.anchoredPosition.y);
                geometryMirrored = rtl;
            }
            /// <summary>相对原始 X 缩放取反，保留其大小和原有符号；Y/Z 使用当前值。</summary>
            public void ApplyScale(bool rtl)
            {
                var target = geometry.target;
                if (target == null) return;
                var scale = target.localScale;
                scale.x = rtl ? -geometry.localScale.x : geometry.localScale.x;
                target.localScale = scale;
                scaleMirrored = rtl;
            }
            /// <summary>镜像 Text 对齐与 LayoutGroup 水平属性；纵向 padding 保留当前值。</summary>
            public void ApplyComponents(bool rtl, RtlLayoutNode policy, RtlLayoutRoot owner)
            {
                var text = components.text;
                var group = components.layoutGroup;
                if (owner.IsSelected(text) && (policy == null || policy.MirrorText))
                    text.alignment = rtl ? MirrorAlignment(components.textAlignment) : components.textAlignment;
                if (owner.IsSelected(group) && (policy == null || policy.MirrorLayoutGroup))
                {
                    group.padding = new RectOffset(rtl ? components.paddingRight : components.paddingLeft,
                        rtl ? components.paddingLeft : components.paddingRight, group.padding.top, group.padding.bottom);
                    group.childAlignment = rtl ? MirrorAlignment(components.childAlignment) : components.childAlignment;
                    if (group is HorizontalLayoutGroup horizontal)
                        horizontal.reverseArrangement = rtl ? !components.reverseArrangement : components.reverseArrangement;
                    if (group is GridLayoutGroup grid)
                        grid.startCorner = rtl ? (GridLayoutGroup.Corner)((int)components.startCorner ^ 1) : components.startCorner;
                    LayoutRebuilder.MarkLayoutForRebuild(group.transform as RectTransform);
                }
                componentsMirrored = rtl;

            }
        }
    }
}
