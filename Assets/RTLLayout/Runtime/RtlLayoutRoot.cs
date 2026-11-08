using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RTLLayout
{
    /// <summary>Applies horizontal UGUI layout from editor-baked LTR data, with a dynamic-node fallback.</summary>
    [DisallowMultipleComponent]
    public sealed class RtlLayoutRoot : MonoBehaviour
    {
        public List<Component> CollectedComponents = new List<Component>();
        public List<Transform> IgnoredNodes = new List<Transform>();
        public List<Transform> MirroredNodes = new List<Transform>();
        [SerializeField] private bool hasConfiguredSelection;
        [SerializeField] private List<Component> knownComponents = new List<Component>();
        public void BakeConfiguredLayout()
        {
            EnsureCanCapture();
            CaptureValidated(false);
        }
        [SerializeField] private List<LayoutData> bakedLayout = new List<LayoutData>();
        // Baselines follow Transform identity across root creation, reparenting and object pools.
        private static readonly Dictionary<Transform, Snapshot> Snapshots = new Dictionary<Transform, Snapshot>();
        private static readonly List<Transform> DeadTransforms = new List<Transform>();
        public bool IsRightToLeft { get; private set; }
        public int BakedNodeCount => bakedLayout.Count;

        /// <summary>Editor authoring operation. Save the prefab after calling; never call during play.</summary>
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

        private bool IsSelected(Component component)
        {
            return component != null && (!hasConfiguredSelection || CollectedComponents.Contains(component) || !knownComponents.Contains(component));
        }

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

        public void Collect()
        {
            PruneDestroyedTransforms();
            LoadBakedLayout();
            Visit(transform, true);
        }

        public void SetRightToLeft(bool value)
        {
            IsRightToLeft = value;
            Collect();
        }

        private void LoadBakedLayout()
        {
            foreach (var data in bakedLayout)
            {
                if (data == null || data.target == null) continue;
                // The serialized hierarchy can be older than the live root boundaries.
                // Check each property domain before allowing old baked data into the shared cache.
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

        private static bool IsLayoutOwned(Transform current)
        {
            if (current.parent == null) return false;
            var group = current.parent.GetComponent<LayoutGroup>();
            if (group == null || !group.isActiveAndEnabled) return false;
            // Matches Unity LayoutGroup: any false includes the child, even on disabled components.
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
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSnapshots() { Snapshots.Clear(); DeadTransforms.Clear(); }
        private static TextAnchor MirrorAlignment(TextAnchor value)
        {
            var alignment = (int)value;
            return (TextAnchor)(alignment / 3 * 3 + 2 - alignment % 3);
        }

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
                // Even an unowned domain has a fallback in case this Transform later moves to another root.
                geometry = data;
                components = data;
                if (ownsGeometry) loadedGeometryBakes.Add(data);
                if (ownsComponents) loadedComponentBakes.Add(data);
            }
            public void LoadBaked(LayoutData data, bool ownsGeometry, bool ownsComponents)
            {
                if (ownsGeometry && loadedGeometryBakes.Add(data)) geometry = data;
                if (ownsComponents && loadedComponentBakes.Add(data)) components = data;
            }
            public void CaptureNewComponents(Transform target)
            {
                var text = target.GetComponent<Text>();
                var group = target.GetComponent<LayoutGroup>();
                if (text == components.text && group == components.layoutGroup) return;
                // Keep the existing component baseline if only another component was added/replaced.
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
            public void ApplyScale(bool rtl)
            {
                var target = geometry.target;
                if (target == null) return;
                var scale = target.localScale;
                scale.x = rtl ? -geometry.localScale.x : geometry.localScale.x;
                target.localScale = scale;
                scaleMirrored = rtl;
            }
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
