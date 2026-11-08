using UnityEngine;
namespace RTLLayout
{
    [DisallowMultipleComponent]
    public sealed class RtlLayoutNode : MonoBehaviour
    {
        public bool IgnoreSelf;
        public bool IgnoreSubtree;
        public bool MirrorRectTransform = true;
        public bool MirrorLayoutGroup = true;
        public bool MirrorText = true;
    }
}
