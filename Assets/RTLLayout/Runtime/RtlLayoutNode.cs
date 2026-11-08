using UnityEngine;
namespace RTLLayout
{
    /// <summary>节点级适配策略，与根的忽略/反转列表配合控制局部镜像。</summary>
    [DisallowMultipleComponent]
    public sealed class RtlLayoutNode : MonoBehaviour
    {
        /// <summary>跳过当前节点的布局和组件，仍继续遍历子节点。</summary>
        public bool IgnoreSelf;
        /// <summary>跳过整个子树，包括内部 RtlLayoutRoot 的方向传播。</summary>
        public bool IgnoreSubtree;
        /// <summary>允许适配横向几何；被父 LayoutGroup 驱动时仍由布局组控制位置。</summary>
        public bool MirrorRectTransform = true;
        /// <summary>允许镜像布局组的水平 padding、对齐、排列或起始角。</summary>
        public bool MirrorLayoutGroup = true;
        /// <summary>仅控制 UGUI Text 左右对齐，不处理字形连接或双向文本排版。</summary>
        public bool MirrorText = true;
    }
}
