using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
namespace RTLLayout.Tests
{
    public class RtlLayoutTests
    {
        private GameObject scene;
        [SetUp] public void Setup() { scene = new GameObject("scene", typeof(RectTransform)); }
        [TearDown] public void Teardown() { Object.DestroyImmediate(scene); }
        private RectTransform Rect(string name, Transform parent = null)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent ? parent : scene.transform, false);
            rect.anchorMin = new Vector2(.1f, .2f); rect.anchorMax = new Vector2(.3f, .8f);
            rect.pivot = new Vector2(.2f, .6f); rect.anchoredPosition = new Vector2(17, 23);
            return rect;
        }
        private RtlLayoutRoot Root() { return scene.AddComponent<RtlLayoutRoot>(); }
        private void AssertLtr(RectTransform rect) { Assert.That(rect.anchoredPosition.x, Is.EqualTo(17).Within(.001)); }
        private void AssertRtl(RectTransform rect) { Assert.That(rect.anchoredPosition.x, Is.EqualTo(-17).Within(.001)); }
        [Test] public void RepeatedCollectAndSetUseOriginalSnapshotAndPreserveLiveVerticalGeometry()
        {
            var child = Rect("child"); var root = Root(); root.Collect(); root.SetRightToLeft(true);
            child.anchoredPosition = new Vector2(child.anchoredPosition.x, 91);
            child.anchorMin = new Vector2(child.anchorMin.x, .4f);
            root.Collect(); root.SetRightToLeft(true); AssertRtl(child);
            Assert.That(child.anchorMin.x, Is.EqualTo(.7f).Within(.001));
            Assert.That(child.anchorMax.x, Is.EqualTo(.9f).Within(.001));
            Assert.That(child.pivot.x, Is.EqualTo(.8f).Within(.001));
            root.SetRightToLeft(false); AssertLtr(child);
            Assert.That(child.anchoredPosition.y, Is.EqualTo(91)); Assert.That(child.anchorMin.y, Is.EqualTo(.4f));
            root.SetRightToLeft(true); AssertRtl(child); Assert.That(root.IsRightToLeft, Is.True);
        }
        [Test] public void RootGeometryIsContainerOwnedButRootTextAndLayoutAdapt()
        {
            var rect = (RectTransform)scene.transform; rect.anchoredPosition = new Vector2(31, 9);
            var text = scene.AddComponent<Text>(); text.alignment = TextAnchor.UpperLeft;
            var group = scene.AddComponent<HorizontalLayoutGroup>(); group.padding = new RectOffset(3, 8, 4, 5);
            var root = Root(); root.SetRightToLeft(true);
            Assert.That(rect.anchoredPosition.x, Is.EqualTo(31)); Assert.That(text.alignment, Is.EqualTo(TextAnchor.UpperRight));
            Assert.That(group.padding.left, Is.EqualTo(8)); Assert.That(group.reverseArrangement, Is.True);
            root.SetRightToLeft(false); Assert.That(group.padding.left, Is.EqualTo(3)); Assert.That(group.reverseArrangement, Is.False);
        }
        [TestCase(false)] [TestCase(true)] public void NestedRootsHaveExclusiveOwnershipInEitherInitializationOrder(bool childFirst)
        {
            var nested = Rect("nested"); var leaf = Rect("leaf", nested);
            var child = nested.gameObject.AddComponent<RtlLayoutRoot>(); var root = Root();
            if (childFirst) child.SetRightToLeft(true);
            root.SetRightToLeft(true); root.Collect(); AssertRtl(nested); AssertRtl(leaf);
            Assert.That(child.IsRightToLeft, Is.True); root.SetRightToLeft(false); AssertLtr(leaf);
        }
        [TestCase(false)] [TestCase(true)] public void LateNestedRootTransfersLtrSnapshotEvenWhenAlreadyMirrored(bool childFirst)
        {
            var nested = Rect("nested"); var leaf = Rect("leaf", nested); var root = Root(); root.SetRightToLeft(true);
            var child = nested.gameObject.AddComponent<RtlLayoutRoot>();
            if (childFirst) child.SetRightToLeft(true);
            root.Collect(); AssertRtl(nested); AssertRtl(leaf);
            root.SetRightToLeft(false); AssertLtr(leaf);
        }
        [Test] public void NewLtrNodesJoinCurrentDirectionAndExistingNodesDoNotFlipAgain()
        {
            var old = Rect("old"); var root = Root(); root.SetRightToLeft(true);
            var added = Rect("added"); root.Collect(); AssertRtl(old); AssertRtl(added);
            root.SetRightToLeft(false); AssertLtr(old); AssertLtr(added);
        }
        [Test] public void ReparentBetweenRootsTransfersOriginalSnapshot()
        {
            var a = Rect("a").gameObject.AddComponent<RtlLayoutRoot>();
            var b = Rect("b").gameObject.AddComponent<RtlLayoutRoot>();
            var leaf = Rect("leaf", a.transform); a.SetRightToLeft(true);
            leaf.SetParent(b.transform, false); b.SetRightToLeft(true); AssertRtl(leaf);
            b.SetRightToLeft(false); a.SetRightToLeft(true); AssertLtr(leaf);
        }
        [Test] public void IgnoreSelfStillVisitsChildrenButIgnoreSubtreeBlocksNestedRootPropagation()
        {
            var self = Rect("self"); self.gameObject.AddComponent<RtlLayoutNode>().IgnoreSelf = true;
            var leaf = Rect("leaf", self);
            var barrier = Rect("barrier"); barrier.gameObject.AddComponent<RtlLayoutNode>().IgnoreSubtree = true;
            var nested = Rect("nested", barrier).gameObject.AddComponent<RtlLayoutRoot>(); var blocked = Rect("blocked", nested.transform);
            Root().SetRightToLeft(true); AssertLtr(self); AssertRtl(leaf); AssertLtr(blocked);
            Assert.That(nested.IsRightToLeft, Is.False);
        }
        [Test] public void LayoutOwnedRectsAreSkippedButIgnoredLayoutChildrenMirror()
        {
            var container = Rect("layout"); var group = container.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.padding = new RectOffset(2, 9, 3, 4); group.childAlignment = TextAnchor.LowerLeft;
            var child = Rect("driven", container); var free = Rect("free", container);
            free.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            Root().SetRightToLeft(true); AssertLtr(child); AssertRtl(free);
            Assert.That(group.childAlignment, Is.EqualTo(TextAnchor.LowerRight)); Assert.That(group.padding.left, Is.EqualTo(9));
        }
        [Test] public void DisabledLayoutGroupDoesNotOwnChildGeometry()
        {
            var container = Rect("disabled-layout");
            container.gameObject.AddComponent<HorizontalLayoutGroup>().enabled = false;
            var leaf = Rect("leaf", container); Root().SetRightToLeft(true); AssertRtl(leaf);
        }
        [Test] public void TogglingLayoutGroupOwnershipNeverRecapturesMirroredGeometry()
        {
            var container = Rect("layout");
            var group = container.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.enabled = false;
            var leaf = Rect("leaf", container); var root = Root(); root.SetRightToLeft(true);
            AssertRtl(leaf);
            group.enabled = true; root.Collect(); AssertRtl(leaf);
            group.enabled = false; root.Collect(); root.Collect(); AssertRtl(leaf);
            root.SetRightToLeft(false); AssertLtr(leaf);
        }
        [Test] public void TogglingIgnoreLayoutNeverRecapturesMirroredGeometry()
        {
            var container = Rect("layout"); container.gameObject.AddComponent<HorizontalLayoutGroup>();
            var leaf = Rect("leaf", container); var element = leaf.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true; var root = Root(); root.SetRightToLeft(true); AssertRtl(leaf);
            element.ignoreLayout = false; root.Collect(); AssertRtl(leaf);
            element.ignoreLayout = true; root.Collect(); root.Collect(); AssertRtl(leaf);
            root.SetRightToLeft(false); AssertLtr(leaf);
        }
        [TestCase(false)] [TestCase(true)]
        public void LateNestedRootRetainsOriginalTextAndLayoutSnapshots(bool childFirst)
        {
            var nested = Rect("nested");
            var text = nested.gameObject.AddComponent<Text>(); text.alignment = TextAnchor.LowerLeft;
            var group = nested.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.padding = new RectOffset(3, 11, 2, 4); group.childAlignment = TextAnchor.UpperLeft;
            var root = Root(); root.SetRightToLeft(true);
            var child = nested.gameObject.AddComponent<RtlLayoutRoot>();
            if (childFirst) child.SetRightToLeft(true);
            root.Collect();
            Assert.That(text.alignment, Is.EqualTo(TextAnchor.LowerRight));
            Assert.That(group.padding.left, Is.EqualTo(11));
            Assert.That(group.reverseArrangement, Is.True);
            root.SetRightToLeft(false);
            Assert.That(text.alignment, Is.EqualTo(TextAnchor.LowerLeft));
            Assert.That(group.padding.left, Is.EqualTo(3));
            Assert.That(group.childAlignment, Is.EqualTo(TextAnchor.UpperLeft));
            Assert.That(group.reverseArrangement, Is.False);
        }
        [Test] public void MixedLayoutIgnorersRemainOwnedWhenAnyRequestsLayout()
        {
            var container = Rect("layout"); container.gameObject.AddComponent<HorizontalLayoutGroup>();
            var leaf = Rect("leaf", container); leaf.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            leaf.gameObject.AddComponent<LayoutElement>().ignoreLayout = false;
            Root().SetRightToLeft(true); AssertLtr(leaf);
        }
        [Test] public void DisabledLayoutElementStillOptsOutOfUnityLayoutCollection()
        {
            var container = Rect("layout"); container.gameObject.AddComponent<HorizontalLayoutGroup>();
            var leaf = Rect("leaf", container); var element = leaf.gameObject.AddComponent<LayoutElement>();
            element.ignoreLayout = true; element.enabled = false;
            Root().SetRightToLeft(true); AssertRtl(leaf);
        }
        [Test] public void BakedLayoutOverridesChangedCurrentGeometryAndComponentsAtRuntime()
        {
            var leaf = Rect("leaf"); var text = leaf.gameObject.AddComponent<Text>();
            text.alignment = TextAnchor.UpperLeft; var root = Root(); root.CaptureLayout();
            Assert.That(root.BakedNodeCount, Is.EqualTo(2));
            leaf.anchoredPosition = new Vector2(99, 81); text.alignment = TextAnchor.LowerCenter;
            root.SetRightToLeft(true); AssertRtl(leaf);
            Assert.That(leaf.anchoredPosition.y, Is.EqualTo(81));
            Assert.That(text.alignment, Is.EqualTo(TextAnchor.UpperRight));
        }
        [Test] public void BakedSerializedCloneRemapsReferencesAndUsesLtrBaselines()
        {
            var leaf = Rect("leaf"); var root = Root(); root.CaptureLayout();
            leaf.anchoredPosition = new Vector2(99, 81);
            var clone = Object.Instantiate(scene, scene.transform);
            var cloneRoot = clone.GetComponent<RtlLayoutRoot>();
            var cloneLeaf = (RectTransform)clone.transform.Find("leaf");
            Assert.That(cloneRoot.BakedNodeCount, Is.EqualTo(2));
            cloneRoot.SetRightToLeft(true); AssertRtl(cloneLeaf);
            Assert.That(leaf.anchoredPosition.x, Is.EqualTo(99));
        }
        [Test] public void BakedHierarchyStillCollectsNewLtrNodesAsFallback()
        {
            var leaf = Rect("leaf"); var root = Root(); root.CaptureLayout();
            Assert.That(root.BakedNodeCount, Is.EqualTo(2)); root.SetRightToLeft(true);
            var dynamicLeaf = Rect("dynamic"); root.Collect(); AssertRtl(leaf); AssertRtl(dynamicLeaf);
            Assert.That(root.BakedNodeCount, Is.EqualTo(2)); root.SetRightToLeft(false); AssertLtr(dynamicLeaf);
        }
        [Test] public void RepeatedEditorCaptureBakesNestedRootsWithoutDuplicateOwnership()
        {
            var nested = Rect("nested"); var child = nested.gameObject.AddComponent<RtlLayoutRoot>();
            var leaf = Rect("leaf", nested); var root = Root();
            root.CaptureLayout(); root.CaptureLayout();
            Assert.That(root.BakedNodeCount, Is.EqualTo(2)); Assert.That(child.BakedNodeCount, Is.EqualTo(2));
            nested.anchoredPosition = new Vector2(92, 23); leaf.anchoredPosition = new Vector2(93, 23);
            root.SetRightToLeft(true); AssertRtl(nested); AssertRtl(leaf);
        }
        [Test] public void EditorCaptureRejectsRtlBeforeMutatingAnyBakedRoot()
        {
            var nested = Rect("nested"); var child = nested.gameObject.AddComponent<RtlLayoutRoot>();
            Rect("leaf", nested); var root = Root(); child.SetRightToLeft(true);
            Assert.Throws<System.InvalidOperationException>(() => root.CaptureLayout());
            Assert.That(root.BakedNodeCount, Is.Zero); Assert.That(child.BakedNodeCount, Is.Zero);
        }
        [Test] public void ExplicitEditorRecaptureAfterRestoringLtrReplacesPriorBaseline()
        {
            var leaf = Rect("leaf"); var root = Root(); root.CaptureLayout();
            root.SetRightToLeft(true); root.SetRightToLeft(false);
            leaf.anchoredPosition = new Vector2(35, 23); root.CaptureLayout();
            root.SetRightToLeft(true);
            Assert.That(leaf.anchoredPosition.x, Is.EqualTo(-35));
        }
        [TestCase(false)] [TestCase(true)]
        public void StaleParentBakeCannotOverwriteNewNestedRootAfterSerializedClone(bool childFirst)
        {
            var nested = Rect("nested"); var leaf = Rect("leaf", nested); var root = Root(); root.CaptureLayout();
            var child = nested.gameObject.AddComponent<RtlLayoutRoot>();
            leaf.anchoredPosition = new Vector2(35, 23); child.CaptureLayout();
            var clone = Object.Instantiate(scene, scene.transform);
            var cloneRoot = clone.GetComponent<RtlLayoutRoot>();
            var cloneChild = clone.transform.Find("nested").GetComponent<RtlLayoutRoot>();
            var cloneLeaf = (RectTransform)cloneChild.transform.Find("leaf");
            if (childFirst) cloneChild.Collect();
            cloneRoot.Collect(); cloneRoot.SetRightToLeft(true);
            Assert.That(cloneLeaf.anchoredPosition.x, Is.EqualTo(-35));
            cloneRoot.SetRightToLeft(false); Assert.That(cloneLeaf.anchoredPosition.x, Is.EqualTo(35));
        }
        [Test] public void GridMirrorsCornerAndPaddingAndRestores()
        {
            var group = scene.AddComponent<GridLayoutGroup>(); group.startCorner = GridLayoutGroup.Corner.UpperLeft;
            group.padding = new RectOffset(2, 7, 1, 1); var root = Root(); root.SetRightToLeft(true);
            Assert.That(group.startCorner, Is.EqualTo(GridLayoutGroup.Corner.UpperRight));
            root.SetRightToLeft(false); Assert.That(group.startCorner, Is.EqualTo(GridLayoutGroup.Corner.UpperLeft));
            Assert.That(group.padding.left, Is.EqualTo(2));
        }
        [Test] public void PropertyOptOutLeavesTextLayoutAndRectUntouched()
        {
            var rect = Rect("optout"); var policy = rect.gameObject.AddComponent<RtlLayoutNode>();
            policy.MirrorRectTransform = false; policy.MirrorText = false; policy.MirrorLayoutGroup = false;
            var text = rect.gameObject.AddComponent<Text>(); text.alignment = TextAnchor.MiddleLeft;
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>(); group.padding = new RectOffset(1, 8, 0, 0);
            Root().SetRightToLeft(true); AssertLtr(rect); Assert.That(text.alignment, Is.EqualTo(TextAnchor.MiddleLeft));
            Assert.That(group.padding.left, Is.EqualTo(1));
        }
        [Test] public void ReversalUsesOriginalNonUnitNegativeScaleAndRestoresExactly()
        {
            var rect = Rect("visual"); rect.gameObject.AddComponent<Image>();
            rect.localScale = new Vector3(-2, 3, 4); var root = Root();
            root.MirroredNodes.Add(rect); root.CaptureLayout(); root.SetRightToLeft(true);
            Assert.That(rect.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
            root.Collect(); root.SetRightToLeft(true);
            Assert.That(rect.localScale.x, Is.EqualTo(2));
            Assert.That(rect.GetComponents<BaseMeshEffect>().Length, Is.Zero);
            root.SetRightToLeft(false); Assert.That(rect.localScale, Is.EqualTo(new Vector3(-2, 3, 4)));
        }
        [Test] public void RemovedCollectedComponentsRemainExcludedFromRuntimeFallback()
        {
            var rect = Rect("selected"); var text = rect.gameObject.AddComponent<Text>();
            text.alignment = TextAnchor.UpperLeft; var root = Root(); root.CaptureLayout();
            Assert.That(root.CollectedComponents, Does.Contain(rect));
            Assert.That(root.CollectedComponents, Does.Contain(text));
            root.CollectedComponents.Remove(rect); root.CollectedComponents.Remove(text);
            root.BakeConfiguredLayout(); root.SetRightToLeft(true); root.Collect();
            AssertLtr(rect); Assert.That(text.alignment, Is.EqualTo(TextAnchor.UpperLeft));
            var dynamicNode = Rect("dynamic"); root.Collect(); AssertRtl(dynamicNode);
        }
        [Test] public void AddedCollectedReferenceAndDuplicatesApplyOnce()
        {
            var root = Root(); root.CaptureLayout(); var rect = Rect("added");
            root.CollectedComponents.Add(rect); root.CollectedComponents.Add(rect);
            root.BakeConfiguredLayout();
            rect.anchoredPosition = new Vector2(99, 23); root.SetRightToLeft(true); AssertRtl(rect);
            Assert.That(root.CollectedComponents.FindAll(component => component == rect).Count, Is.EqualTo(1));
        }
        [Test] public void IgnoredSubtreeWinsExplicitReversalAndStopsNestedPropagation()
        {
            var barrier = Rect("barrier"); var child = barrier.gameObject.AddComponent<RtlLayoutRoot>();
            var leaf = Rect("leaf", barrier); var root = Root();
            root.IgnoredNodes.Add(barrier); root.MirroredNodes.Add(barrier); root.MirroredNodes.Add(leaf);
            root.CaptureLayout(); root.SetRightToLeft(true);
            AssertLtr(barrier); AssertLtr(leaf); Assert.That(barrier.localScale, Is.EqualTo(Vector3.one));
            Assert.That(leaf.localScale, Is.EqualTo(Vector3.one)); Assert.That(child.IsRightToLeft, Is.False);
        }
        [Test] public void NestedDuplicateReversalReferencesHaveSingleGeometryOwner()
        {
            var nested = Rect("nested"); nested.localScale = new Vector3(2, 1, 1);
            var child = nested.gameObject.AddComponent<RtlLayoutRoot>(); var root = Root();
            root.MirroredNodes.Add(nested); root.MirroredNodes.Add(nested); child.MirroredNodes.Add(nested);
            root.CaptureLayout(); child.Collect(); root.SetRightToLeft(true); root.Collect();
            Assert.That(nested.localScale.x, Is.EqualTo(-2));
            root.SetRightToLeft(false); Assert.That(nested.localScale.x, Is.EqualTo(2));
        }
    }
}
