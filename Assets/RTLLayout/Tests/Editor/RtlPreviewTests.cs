using NUnit.Framework;
using RTLLayout.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace RTLLayout.Tests
{
    public class RtlPreviewTests
    {
        GameObject host;
        [TearDown] public void Cleanup() { RtlLayoutPreview.Stop(); if(host != null) Object.DestroyImmediate(host); }
        [Test] public void PreviewRoundTripNeverChangesSerializedBaselinesAndRestoresAuthoringValues()
        {
            host = new GameObject("root", typeof(RectTransform), typeof(RtlLayoutRoot));
            var root = host.GetComponent<RtlLayoutRoot>();
            var child = new GameObject("label", typeof(RectTransform), typeof(Text)); child.transform.SetParent(host.transform, false);
            var rect = (RectTransform)child.transform; rect.anchoredPosition = new Vector2(24, 7);
            rect.localScale = new Vector3(2, 1, 1);
            var label = child.GetComponent<Text>(); label.alignment = TextAnchor.MiddleLeft;
            root.MirroredNodes.Add(rect); root.CaptureLayout();
            var before = EditorJsonUtility.ToJson(root);
            rect.anchoredPosition = new Vector2(35, 7); // Unsaved authored edit must also survive preview.
            for (int i=0; i<3; i++)
            {
                RtlLayoutPreview.SetDirection(root,true);
                Assert.That(rect.anchoredPosition.x,Is.EqualTo(-24));
                Assert.That(rect.localScale.x,Is.EqualTo(-2));
                Assert.That(label.alignment,Is.EqualTo(TextAnchor.MiddleRight));
                RtlLayoutPreview.SetDirection(root,false);
                Assert.That(rect.anchoredPosition.x,Is.EqualTo(24));
                Assert.That(EditorJsonUtility.ToJson(root),Is.EqualTo(before));
            }
            RtlLayoutPreview.Stop();
            Assert.That(rect.anchoredPosition.x,Is.EqualTo(35));
            Assert.That(rect.localScale.x,Is.EqualTo(2));
            Assert.That(root.IsRightToLeft,Is.False);
            Assert.That(EditorJsonUtility.ToJson(root),Is.EqualTo(before));
        }
        [Test] public void NestedPreviewKeepsBothCachesAndRestoresScaleOnce()
        {
            host = new GameObject("root", typeof(RectTransform), typeof(RtlLayoutRoot));
            var child = new GameObject("nested", typeof(RectTransform), typeof(RtlLayoutRoot)); child.transform.SetParent(host.transform,false);
            var leaf = new GameObject("visual", typeof(RectTransform), typeof(Text)); leaf.transform.SetParent(child.transform,false);
            var outer=host.GetComponent<RtlLayoutRoot>(); var inner=child.GetComponent<RtlLayoutRoot>();
            leaf.transform.localScale=new Vector3(2,1,1); inner.MirroredNodes.Add(leaf.transform);
            leaf.GetComponent<Text>().alignment=TextAnchor.UpperLeft; outer.CaptureLayout();
            var outerData=EditorJsonUtility.ToJson(outer); var innerData=EditorJsonUtility.ToJson(inner);
            RtlLayoutPreview.SetDirection(outer,true); RtlLayoutPreview.SetDirection(outer,true);
            Assert.That(leaf.transform.localScale.x,Is.EqualTo(-2));
            Assert.That(inner.IsRightToLeft,Is.True);
            RtlLayoutPreview.Stop();
            Assert.That(leaf.transform.localScale.x,Is.EqualTo(2));
            Assert.That(inner.IsRightToLeft,Is.False);
            Assert.That(EditorJsonUtility.ToJson(outer),Is.EqualTo(outerData));
            Assert.That(EditorJsonUtility.ToJson(inner),Is.EqualTo(innerData));
        }
        [Test] public void SavingAssetsEndsPreviewBeforePersistingChanges()
        {
            var path="Assets/rtl-preview-save-"+System.Guid.NewGuid().ToString("N")+".asset";
            var material=new Material(Shader.Find("UI/Default"));
            try
            {
                AssetDatabase.CreateAsset(material,path);
                host=new GameObject("root",typeof(RectTransform),typeof(RtlLayoutRoot));
                var child=new GameObject("child",typeof(RectTransform));child.transform.SetParent(host.transform,false);
                var rect=(RectTransform)child.transform; rect.anchoredPosition=new Vector2(12,0);
                var root=host.GetComponent<RtlLayoutRoot>();root.CaptureLayout();
                var before=EditorJsonUtility.ToJson(root);
                RtlLayoutPreview.SetDirection(root,true);
                EditorUtility.SetDirty(material);AssetDatabase.SaveAssets();
                Assert.That(RtlLayoutPreview.IsActive,Is.False);
                Assert.That(rect.anchoredPosition.x,Is.EqualTo(12));
                Assert.That(EditorJsonUtility.ToJson(root),Is.EqualTo(before));
            }
            finally { RtlLayoutPreview.Stop();AssetDatabase.DeleteAsset(path); }
        }
        [Test] public void PlainTransformReversalRestoresUnbakedScaleEdit()
        {
            host=new GameObject("root",typeof(RectTransform),typeof(RtlLayoutRoot));
            var child=new GameObject("plain");child.transform.SetParent(host.transform,false);
            child.transform.localScale=new Vector3(2,1,1);
            var root=host.GetComponent<RtlLayoutRoot>();root.MirroredNodes.Add(child.transform);root.CaptureLayout();
            child.transform.localScale=new Vector3(3,1,1);
            RtlLayoutPreview.SetDirection(root,true);
            Assert.That(child.transform.localScale.x,Is.EqualTo(-2));
            RtlLayoutPreview.Stop();
            Assert.That(child.transform.localScale.x,Is.EqualTo(3));
        }
        [Test] public void UnbakedRootCannotPreviewOrImplicitlyCapture()
        {
            host = new GameObject("root",typeof(RectTransform),typeof(RtlLayoutRoot));
            var root=host.GetComponent<RtlLayoutRoot>();
            Assert.Throws<System.InvalidOperationException>(()=>RtlLayoutPreview.SetDirection(root,true));
            Assert.That(root.BakedNodeCount,Is.Zero);
        }
    }
}
