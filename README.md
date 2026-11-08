# RTLLayout

## 安装

使用 Unity 2022.3 和 UGUI 1.0。将本仓库 `Assets/RTLLayout` 及 `.meta` 文件复制到工程的 `Assets` 目录。

保留 Editor 目录以使用预收集和 Inspector 预览。如果保留 Tests 目录，需要安装 Unity Test Framework；不需要测试时可移除 Tests。

RTLLayout 可单独使用，无需安装 SegmentUI。请使用目录复制方式安装。

## 配置 Prefab

1. 按从左到右（LTR）的方式完成布局。
2. 在 Prefab 根节点添加 `RTLLayout.RtlLayoutRoot`。
3. 点击 **Discover Components & Bake LTR (Reset Selection)**，收集组件并缓存原始 LTR 布局。
4. 根据需要编辑以下列表。
5. 点击 **Bake Selected Components (Keep Edited Lists)**，保存调整后的选择与基准，然后保存 Prefab。

| Inspector 配置 | 使用方式 |
| --- | --- |
| Collected Components | 添加或移除需要适配的 RectTransform、UGUI Text、LayoutGroup 引用 |
| Ignored Nodes | 添加不需要适配的节点，该节点的整个子树都会被忽略 |
| Mirrored Nodes | 添加需要水平反转的视觉节点，RTL 时反转原始 localScale.x，LTR 时恢复 |
| Baked LTR Baselines | 展开查看已缓存的原始属性，只读 |

**Discover** 会重新发现组件并重置收集选择；保留人工编辑的选择时使用 **Bake Selected Components**。修改原始布局后，应在 LTR 状态重新烘焙。

图片需要反转时，将图片所在的 Transform 加入 Mirrored Nodes。缩放反转会影响其子节点，建议使用专用图片节点，避免把文字一起反转。Ignored Nodes 优先于 Mirrored Nodes。

## 预览 LTR / RTL

完成烘焙后，在 Prefab 编辑模式选中 `RtlLayoutRoot`，点击 Inspector 中的 **Layout Preview → LTR / RTL**。

预览不会覆盖缓存的 LTR 数据。预览期间配置列表与烘焙按钮不可编辑；点击 **Exit Preview / Restore Layout** 后恢复预览前的布局，再继续编辑。

保存、离开该 Inspector、关闭 Prefab Stage、脚本重载或进入 Play 时也会自动退出预览。未烘焙、多选或 Play 模式下不能开启预览；使用前先结束其他动画预览。

## 运行时切换

```csharp
using RTLLayout;
using UnityEngine;

public class ScreenDirection : MonoBehaviour
{
    [SerializeField] RtlLayoutRoot layoutRoot;

    public void SetRightToLeft(bool rightToLeft)
    {
        layoutRoot.SetRightToLeft(rightToLeft);
    }
}
```

`true` 应用 RTL，`false` 恢复 LTR。业务绑定完成后主动调用；组件不会在 Awake/OnEnable 自动应用方向。

## 动态添加节点

先按 LTR 创建并配置新增节点，再注册和应用当前方向：

```csharp
root.Collect();
root.SetRightToLeft(rightToLeft);
```

Collect 仅补充新节点或新组件，不覆盖已缓存的 LTR 基准。对象池复用或节点移到另一个根后，对接收根重新调用 Collect/SetRightToLeft。

运行中修改忽略或反转列表前，先调用 `SetRightToLeft(false)` 恢复 LTR，再修改配置并应用目标方向。

## 嵌套与局部适配

子 Prefab 可以有自己的 `RtlLayoutRoot`。父根管理子根的外部位置，子根管理内部布局；不要再额外编写一套整体镜像逻辑。

需要按节点控制时，添加 `RtlLayoutNode`：

- `IgnoreSelf`：忽略当前节点。
- `IgnoreSubtree`：忽略当前节点及所有子节点，也停止向内部根传播方向。
- `MirrorRectTransform`、`MirrorLayoutGroup`、`MirrorText`：分别控制位置布局、布局组和文字对齐适配。

根节点自身的位置和缩放由外部容器配置。使用 LayoutGroup 排列子节点时，将布局组纳入收集，子项位置继续由布局组管理。

## 配合 SegmentUI 使用

从 [SegmentUI](https://github.com/zhangguozhong/SegmentUI) 导入 `Integrations/RTLLayout` 桥接，在 Page/Item 根同时添加 `RtlLayoutRoot` 和 `SegmentRtlLayoutBridge`。页面绑定完成后，桥接会同步分页方向。

## 文本适配

UGUI Text 会交换左右对齐。阿拉伯文字形连接、双向文本排版和 TMP 需要另接业务文本适配组件。
