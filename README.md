# RTLLayout

独立 UGUI 横向布局镜像库，运行时程序集仅引用 `Unity.ugui`，不依赖 SegmentUI。

## 安装与验证

验证环境：Unity **2022.3.62f3c1**、UGUI **1.0.0**。将本仓库 `Assets/RTLLayout` 连同 `.meta` 文件复制到 Unity 工程，保持引用完整。运行测试还需 Unity Test Framework **1.1.33**；生产工程可移除 Tests，保留 Editor 以支持预收集和 Inspector 预览。

这是 Assets 目录形式的组件库，目前不是 UPM 包，不能直接通过 Package Manager 的 Git URL 安装。无需安装 SegmentUI；如需分页联动，另见 [SegmentUI](https://github.com/zhangguozhong/SegmentUI) 中的可选桥接。

发布前联合回归（2026-11-09）：EditMode 99/99、PlayMode 44/44；数字包含 SegmentUI 与桥接测试。RTLLayout 自身的测试位于 `Assets/RTLLayout/Tests/Editor`，可在 Unity Test Runner 的 EditMode 中筛选 `RTLLayout.Tests` 运行。未声明真机或其他 Unity 版本兼容性。

## 编辑器配置

在以 LTR 编写的 Prefab 根添加 `RTLLayout.RtlLayoutRoot`。Inspector 提供三个可编辑列表：

- **Collected Components**：参与布局适配的 `RectTransform`、UGUI `Text`、`LayoutGroup` 组件引用。可增删引用；不接受 Image 等没有适配属性的类型。删除已发现的组件后，运行时兜底不会偷偷重新加入它。
- **Ignored Nodes**：忽略所选 Transform 的整个子树，包括向嵌套根的方向传播；忽略优先于反转。
- **Mirrored Nodes**：显式反转所选 Transform 的原始 `localScale.x`。保留非单位值和原有正负号，回到 LTR 时恢复原始 X 缩放。这会影响其子节点的视觉方向，应选择专用 Visual 节点；不会添加 mesh effect。Y/Z 缩放不改写。

点击 **Discover Components & Bake LTR (Reset Selection)** 自动发现组件并序列化 LTR 基准；此操作重建组件选择列表，保留忽略和反转列表。修改组件选择后，点击 **Bake Selected Components (Keep Edited Lists)** 保留选择并重新烘焙。随后保存 Prefab。两个操作均支持 Undo、Prefab Stage、Prefab 资产及实例修改记录，递归处理嵌套根。重复组件引用会去重，非法类型或不属于当前根所有权的组件在配置烘焙时移除；忽略/反转列表中的域外引用安全跳过。

**Baked LTR Baselines** 只读展示节点引用、属性所有权、anchors/pivot/position、localScale、Text 对齐、LayoutGroup padding/排列方向/起始角和节点策略。不得直接编辑缓存；在原始 LTR 布局上编辑组件，再点击相应烘焙按钮。Unity 实例化 Prefab 时会自动重映射这些引用。

Seeder 在最终布局完成后、保存资产前调用 `root.CaptureLayout()`；保留人工选择时改用 `root.BakeConfiguredLayout()`。`BakedNodeCount` 返回保存节点数。Play 模式或当前仍在 RTL 的层级拒绝烘焙。完整恢复 LTR 后可编辑并重烘焙，替换旧基准。

## Inspector 临时预览

完成 LTR 烘焙后，在 Prefab 编辑模式选中挂有 `RtlLayoutRoot` 的节点，使用 **Layout Preview → LTR / RTL** 检查两种方向。预览读取已缓存的 LTR 基准，不重新收集或覆盖缓存；预览期间配置列表和烘焙按钮禁用，只读缓存仍可展开查看。

点击 **Exit Preview / Restore Layout** 恢复进入预览前的布局，包括尚未重新烘焙的编辑值。离开该 Inspector、保存资产或 Prefab、关闭 Prefab Stage、脚本重载、进入 Play 时也会退出并恢复，避免保存镜像后的临时属性。预览使用 Unity AnimationMode，不与其他动画预览同时开启；未烘焙、多选及 Play 模式不提供预览。

## 运行时

```csharp
root.Collect();                 // 可选：载入已烘焙基准并注册动态节点
root.SetRightToLeft(true);      // 自动增量兜底并应用 RTL
root.SetRightToLeft(false);     // 从同一份 LTR 基准恢复
```

重复 Collect/同方向设置幂等。运行时优先使用 Prefab 烘焙数据；只有未烘焙的新节点或新组件兜底采集。动态节点必须按 LTR 创建，配置完成后 Collect/SetRightToLeft。根不在 Awake/OnEnable 自动应用，允许业务先完成绑定。普通 Collect 永不覆盖已存储的 LTR 数据。

## 所有权与边界

- 顶层根的 RectTransform 与缩放由外部容器管理，不修改。根上的 Text 和 LayoutGroup 可参与。
- 父根拥有嵌套根的外部 RectTransform/显式缩放反转；子根拥有自身组件及内部节点。嵌套根自身的 MirroredNodes 重复引用其根 Transform 不会双反转。
- 启用且激活的父 LayoutGroup 管理子项几何；适配组 padding/alignment、水平顺序或 Grid 起始角，不重复镜像被驱动的子项。ILayoutIgnorer 语义与 Unity 一致：所有 ignorer 均为 true 才退出布局；禁用 LayoutElement 仍参与判定。
- 几何只写水平 anchors、pivot、anchoredPosition，保留实时纵向坐标、尺寸。显式反转单独控制 localScale.x。
- `RtlLayoutNode.IgnoreSelf` 仅跳过当前节点，`IgnoreSubtree` 跳过子树及嵌套方向传播。可用 MirrorRectTransform/MirrorLayoutGroup/MirrorText 进一步关闭属性适配。图片反转统一使用根的 MirroredNodes。
- 运行中修改列表/策略不会自动撤销此前已应用的变换，应先恢复 LTR 再改配置。独立库不管理安全区、分页或手势。Text 仅交换对齐，不处理 TMP、字形连接及双向排版。

## 动态生命周期

运行时基准按 Transform 身份共享，支持对象池、跨根重挂及晚加嵌套根；旧根只遍历当前归属的节点。加载烘焙数据时按当前几何/组件所有权过滤，防止旧父 Prefab 记录覆盖后来新增子根的基准。转移后调用接收根 Collect/SetRightToLeft；必要时再同步父根方向。

已销毁 Transform 的缓存在后续 Collect 时清理；新的运行会话清空缓存。活节点离开所有根时保留基准以支持池复用。运行中不提供重设原始基准的 API；编辑器使用明确的烘焙操作替换。不会自动监听层级变化。

`RTLLayout.Tests` EditMode 覆盖幂等/往返、动态纵向值、嵌套初始化顺序、晚加根与跨根转移、烘焙实例引用重映射、旧父基准隔离、列表增删/去重、忽略优先级、布局所有权和原始缩放反转。

## 许可

公开仓库当前未附加开源许可证；公开可见不等于授予任意再分发许可。
