# RenderGraph 重构计划

## 目录

1. [背景与现状](#1-背景与现状)
2. [目标与非目标](#2-目标与非目标)
3. [总体设计](#3-总体设计)
4. [RHI 增强(阶段 0)](#4-rhi-增强阶段-0)
5. [Graph 基础设施(阶段 1)](#5-graph-基础设施阶段-1)
6. [Deferred 管线迁移(阶段 2)](#6-deferred-管线迁移阶段-2)
7. [插件与子图(阶段 3)](#7-插件与子图阶段-3)
8. [公开 API 变化与兼容性](#8-公开-api-变化与兼容性)
9. [实施步骤](#9-实施步骤)
10. [单元测试计划](#10-单元测试计划)
11. [验收方案(Sandbox 34)](#11-验收方案sandbox-34)
12. [风险与陷阱](#12-风险与陷阱)
13. [总结](#13-总结)

---

## 1. 背景与现状

### 1.1 当前管线结构

`PBRDeferredPipeline.Render()`(`Src/Alco.Rendering/Deferred/PBRDeferredPipeline.cs:817-846`)以硬编码顺序驱动一帧:

```
RenderShadowPass()            // 4 个 cascade,每个独立 render pass + submit
RenderGBufferPass()           // 独立 submit
AfterGBufferCallback?.Invoke()
ExecutePlugins(AfterGBuffer)  // HBAO(compute)、VoxelGI(compute),各自独立 submit
RenderLighting(_forwardRT)    // 独立 submit
RenderVolumetricLight(_forwardRT)
depth copy(独立 command buffer + submit,PBRDeferredPipeline.cs:836-843)
_chain.Execute(_forwardRT, destination)  // 每个后处理节点一次 submit + 最终 blit
```

全特性一帧约 **15–20 次 `wgpuQueueSubmit`**;每个 `RenderContext.End()` 立即提交(`Renderer/RenderContext.cs:318-319` → `RenderingSystem.cs:248` → `WGPU/WebGPUDevice.cs:121-127`)。

### 1.2 量化后的痛点

| 痛点 | 证据 | 影响 |
|---|---|---|
| 中间目标全部常驻,无别名复用 | `_gbufferRT`/`_shadowRT`/`_forwardRT`(`PBRDeferredPipeline.cs:637,645,677`)、chain `_tempA/_tempB`(`Node/RenderNodeChain.cs:229-230`)、FXAA 中间纹理、HBAO 两张全尺寸(`HbaoRenderer.cs:128-129`)、SSR 四张(`ScreenSpaceReflectionRenderer.cs:142-147`)、VoxelGI 两张全尺寸输出 | ≥8 张全尺寸 RT 生命周期互不重叠却各自独占;1080p 每张 RGBA16F ≈ 16 MB,4K ≈ 66 MB |
| pass 级裁剪缺失 | `ShadowEnabled=false` 仅置 shader flag,4 个 cascade pass 照常执行(`PBRDeferredPipeline.cs:819,540`) | 特性开关挡不住 GPU 空跑 |
| 新增 pass 成本高 | 字段/构造、`Render()`、`Resize()`、`Dispose()`、写死的 `PipelineTimestampCount=8`(`PBRDeferredPipeline.cs:236`)、lighting cbuffer + HLSL 同步,共 6–8 处 | 可维护性差,易遗漏(参见 `docs/PointLight_MultiLight_Refactor.md:92` 记录的"4 份副本"事故) |
| 每帧一次全尺寸 depth copy | `PBRDeferredPipeline.cs:836-843` | 纯浪费:仅为让玻璃复用 G-buffer 深度 |
| framebuffer 静态拥有纹理 | `WebGPUFrameBuffer.cs:139-224` 构造即创建纹理并烘焙 `WGPURenderPassDescriptor` | transient 池化/别名无法实现 |
| 无 load/store op 控制 | `WebGPUCommandBuffer.cs:132-214` 仅 clear 时改写 op,无 Discard | 无法表达"attachment 用完即弃" |
| 提交模型为单 buffer 立即提交 | `WebGPUDevice.cs:121-127`;批量 `wgpuQueueSubmit(queue, span)` 绑定已存在(`WGPU/Bindings/WebGPU.cs:43-48`)但未暴露 | CPU 侧提交开销 ×15–20/帧 |

### 1.3 不适用的经典卖点(明确排除)

- **自动 barrier 推导**:唯一硬后端是 wgpu-native(`Src/Alco.Graphics/WGPU/`),`GraphicsBackend.Vulkan/D3D12` 仅是 wgpu 适配器选择(`WGPU/WebGPUUtility.TypeMapping.cs:15-34`),同步由 wgpu 隐式处理。即使未来扩展 Vulkan 后端,也参考 wgpu 做法在后端内部处理,不在 Graph 层建模。
- **多队列 / async compute**:wgpu 单队列(`WebGPUDevice.cs:1075`)。

## 2. 目标与非目标

### 目标(按优先级)

1. **性能**
   - transient 纹理池 + 帧内别名:生命周期不重叠的同规格中间目标共享底层 `GPUTexture`;
   - 提交合并:一帧 15–20 次 submit → 1 次(图执行域内);
   - 消除每帧 G-buffer → forward RT 的全尺寸 depth copy(共享 depth attachment);
   - 自动 pass 裁剪:特性关闭时整条上游链不执行(如 `ShadowEnabled=false` → shadow 节点被裁);
2. **可维护性**:pass 声明资源读写,依赖即数据;新增 pipeline 级 pass 只需实现一个节点并 `Use()`;
3. **零逐帧分配**:稳态(节点集合与使能集合不变)下,Setup/Compile/Execute 路径不分配托管对象;材质绑定沿用现有"稳定对象身份 + Version"契约(`Texture/RenderTexture.cs:181-249`);
4. **API 简洁易用**,贴合引擎风格:注册式 `Use()`、plain struct descriptor、极简接口带默认成员。

### 非目标(v1 明确不做)

- barrier/同步推导、多队列、async compute、多线程命令录制;
- 节点内部子资源别名(VoxelGI 内部页池/体积纹理、Bloom 金字塔保持节点私有,黑盒);
- 3D 视锥剔除、light culling(正交问题);
- ForwardPipeline / 2D 路径迁移(`RenderNodeChain` 保持不变);
- Graph 级 GPU 时间戳自动插桩(沿用管线现有手动插桩);
- 资源池驱逐策略(池随 graph 存活,`Resize` 清空重建;峰值即别名后的工作集,这正是收益本身)。

## 3. 总体设计

### 3.1 资源模型:三类资源

| 类别 | 创建方式 | 生命周期 | 底层纹理来源 |
|---|---|---|---|
| **Transient** | `RenderGraph.CreateTransient(in RenderGraphTextureDescriptor)` | 帧内,由编译期生命周期分析驱动 acquire/release | 池化 `GPUTexture`,可与生命周期不重叠的其他 transient 别名 |
| **Imported** | `RenderGraph.Import(RenderTexture)` | 持久,调用方持有(SSR history、GI 体积纹理等跨帧资源) | 不池化、不别名,原样引用 |
| **Destination** | `RenderGraph.Execute(GPUFrameBuffer?)` 传入 | 交换链/视图目标 | 图外资源,节点通过 `ProducesOutput()` 声明副作用 |

**关键设计:稳定包装身份**。每个 `RenderGraphTexture` 持有一个对材质系统可见的 `RenderTexture` 门面;backing(`GPUFrameBuffer` + 纹理)变化时走新增的 `internal Rebind` 路径原地替换并自增 `Version`——这正是 `RenderTexture.Resize` 已有的契约(`RenderTexture.cs:203-249`),材质系统自动重建 bind group,节点代码与材质绑定方式**完全不变**。稳态下池分配收敛到同一底层纹理 → 不触发 Rebind → 零分配、零重绑。

**组合 framebuffer**:transient 的 `GPUFrameBuffer` 不再拥有纹理,而是由池化纹理 + 视图临时"组合"而成(阶段 0 新增的外部纹理 framebuffer 路径)。这使"sceneColor 颜色 + G-buffer 深度"这类跨资源共享 attachment 成为可能(消除 depth copy 的手段)。

### 3.2 节点模型与 API

```csharp
// Src/Alco.Rendering/Graph/IRenderGraphNode.cs
public interface IRenderGraphNode : IRenderNode
{
    /// <summary>Declares this frame's resource reads/writes. Called every frame,
    /// in registration order, before any Execute. Must not allocate.</summary>
    void Setup(RenderGraphBuilder builder) { }

    /// <summary>Records this frame's GPU work. Called only when the node survived culling.</summary>
    void Execute(in RenderGraphContext context);
}
```

```csharp
// Src/Alco.Rendering/Graph/RenderGraphBuilder.cs — 仅 Setup 期间有效,禁止存储
public sealed class RenderGraphBuilder
{
    public void Read(RenderGraphTexture texture);       // 纯读
    public void Write(RenderGraphTexture texture);      // 写入(生产者)
    public void ReadWrite(RenderGraphTexture texture);  // 原地修改(additive、forward 叠加)
    public void ProducesOutput();                        // 声明副作用:本节点不可被裁剪(如最终 blit)
}

// Src/Alco.Rendering/Graph/RenderGraphContext.cs — 仅 Execute 期间有效
public sealed class RenderGraphContext
{
    public RenderingSystem Rendering { get; }
    public RenderProfiler Profiler { get; }
    public GPUFrameBuffer? Destination { get; }
    public float DeltaTime { get; }
}
```

```csharp
// Src/Alco.Rendering/Graph/RenderGraph.cs
public sealed class RenderGraph : AutoDisposable
{
    public RenderGraph(RenderingSystem rendering, uint width, uint height, string name = "unnamed_render_graph");

    public void Use(IRenderGraphNode node);              // 注册即取得所有权,与 ForwardPipeline.Use 一致
    public bool Remove(IRenderGraphNode node);
    public T? Get<T>() where T : class, IRenderGraphNode;

    public RenderGraphTexture CreateTransient(in RenderGraphTextureDescriptor descriptor);
    public RenderGraphTexture Import(RenderTexture texture);

    public void Execute(GPUFrameBuffer? destination);    // Setup → Compile → Execute → Flush
    public void Resize(uint width, uint height);
}

// Src/Alco.Rendering/Graph/RenderGraphTextureDescriptor.cs
public struct RenderGraphTextureDescriptor
{
    public required GPUAttachmentLayout Layout { get; init; }
    public uint Width { get; init; }                     // 0 = 跟随 graph 尺寸 × ResolutionScale
    public uint Height { get; init; }
    public float ResolutionScale { get; init; }          // 默认 1.0
    public RenderGraphTexture? DepthSource { get; init; } // 共享另一 transient 的 depth attachment(可选)
    public FilterMode Filter { get; init; }              // 默认 Linear
    public string Name { get; init; }                    // 默认 "unnamed_graph_texture"
}
```

使用手感(与 `ForwardPipeline.Use()` 同构):

```csharp
var graph = new RenderGraph(RenderingSystem, width, height, "pbr_deferred");
var shadowMap = graph.CreateTransient(new RenderGraphTextureDescriptor { Layout = shadowLayout, Width = 4096, Height = 4096, Name = "shadow_map" });
var gbuffer   = graph.CreateTransient(new RenderGraphTextureDescriptor { Layout = gbufferLayout, Name = "gbuffer" });          // 跟随 graph 尺寸
var sceneColor = graph.CreateTransient(new RenderGraphTextureDescriptor { Layout = hdrLayout, DepthSource = gbuffer, Name = "scene_color" });

graph.Use(_shadowNode);    // Setup: if (ShadowEnabled) Write(shadowMap)
graph.Use(_gbufferNode);   // Setup: Write(gbuffer)
graph.Use(_lightingNode);  // Setup: Read(gbuffer); if (ShadowEnabled) Read(shadowMap); Write(sceneColor)
graph.Use(_bloomNode);     // Setup: Read(input); Write(bloomOut)
graph.Execute(MainPresenter.FrameBuffer);
// ShadowEnabled=false 时:lighting 不读 shadowMap → shadow 节点写入无人引用 → 自动整链裁剪
```

### 3.3 每帧执行流程

```
Execute(destination):
  1. Setup   — 按注册顺序调用每个 IsEnabled 节点的 Setup,读/写记录进复用数组(零分配)
  2. Compile — (a) 依赖校验:read-before-write 报 InvalidOperationException(带节点与资源名)
               (b) 裁剪:从 ProducesOutput 根节点反向标记存活(见 3.4)
               (c) 生命周期:每个 transient 的 firstTouch/lastTouch(节点序索引)
  3. Assign  — pool.BeginFrame()(全部条目转 Idle)→ 分配走查(见 3.4):
               按 firstTouch 顺序遍历本帧用到的 transient,
               先把 lastTouch 严格小于当前 firstTouch 的既有分配释放回池(成为别名候选),
               再为当前资源的每个 attachment 槽从池中取条目;
               与上帧分配逐槽 ReferenceEquals 比较,变了才重组 framebuffer 并 Rebind 门面
  4. Execute — RenderingSystem.BeginCommandCollection()(见 3.6)
               按序遍历存活节点执行 node.Execute(context)(不再穿插 acquire/release)
  5. Flush   — FlushCommandCollection():收集到的 command buffer 一次批量 Submit
```

`destination == null`(最小化/无头视图)时保持现语义:管线把内容产出节点标记为 `ProducesOutput()`(见 6.3),内容照常渲染,仅最终 blit 不执行。

### 3.4 裁剪与生命周期算法(纯逻辑,可单测)

反向扫描,维护"被需要"资源集合 `needed`(复用 `HashSet<RenderGraphTexture>`,每帧 Clear):

```
for i = last .. 0:
    live = node[i].ProducesOutput || (writes(node[i]) ∩ needed ≠ ∅)
    if live:
        needed -= writes(node[i])      // 本节点满足这些需求(后写覆盖先写)
        needed += reads(node[i])       // 需求向前传播(ReadWrite 节点读写在同一节点,先减后加即正确)
    else:
        裁剪 node[i](其写入无人需要,其读取不传播)
```

- 纯 `Write` 两次写入同一资源:后者覆盖,合法;`ReadWrite` 同时是读者与写者;
- Imported 资源永不入池;对 imported 的 Write 不构成存活根(输出没人用时连 history 更新一起裁掉,符合预期);
- 生命周期:`firstTouch` = 存活节点中最早 Write/ReadWrite 索引,`lastTouch` = 最晚 Read/Write/ReadWrite 索引;共享 depth 的 transient 把 DepthSource 记为每个写者的隐式读,保证共享深度覆盖整个使用区间;
- 别名正确性由分配走查(见 3.3 第 3 步)保证:按 firstTouch 升序遍历,只有 `lastTouch < firstTouch` 的既有分配才会被释放复用,因此生命周期重叠(含端点相接:一者 lastTouch 等于另一者 firstTouch,该节点执行期间两者都存活)的资源永不共享纹理。

#### 分配走查与池的优先级(测试驱动修订)

初版设计为"创建即 acquire + 帧首全量归还 + 执行期按节点边界 acquire/release(FIFO 池)",单测暴露两个致命缺陷:

1. **别名完全不生效**:前置创建的 transient 在创建时即钉住 1:1 纹理,生命周期不重叠的资源也无法共享——池化的核心收益落空;
2. **嵌套生命周期振荡**:A=[n1,n4] 包裹 B=[n2,n3] 时,FIFO 归还顺序使 B 逐帧在两张纹理间摇摆,稳态每帧都触发 Rebind 与材质重建。

因此分配从执行期挪到编译期(节点执行前),一次走查完成全部分配;池按 `TexturePoolKey` 分桶,每桶维护 `All`(物化全集)/`Idle`(帧首未分配)/`Freed`(本次走查中已释放)三个常驻列表,`Allocate(key, sticky, name)` 的取值优先级:

1. **sticky 在 Freed 中** → 取回(sticky = 该槽上帧的分配;即使条目刚被其他资源释放,也优先物归原主,保持门面稳定);
2. **Freed 最新者** → 取走(这是生命周期不重叠的资源别名同一纹理的途径);
3. **sticky 在 Idle 中** → 取回(稳态路径:本帧走查复现上帧分配);
4. **Idle 最旧者** → 取走(确定性兜底);
5. **factory 新建**(物化新条目入 All)。

效果:相同调度下走查确定性地复现上帧分配 → 门面不 Rebind、材质不重绑、全程零分配;前置创建且生命周期不重叠的同 key 资源从第 2 帧起稳定别名;嵌套/重叠生命周期的资源各自保持独立且稳定的分配(使能切换引入的错位至多一帧即自愈)。未参与本帧走查的 transient(被裁剪)不占用条目,其 sticky 可被别名占用,重新启用时按规则回退(当帧一次 Rebind,正确)。

### 3.5 零分配策略

| 路径 | 措施 |
|---|---|
| 每帧节点记录 | 节点注册时创建 `NodeRecord`(含读/写数组,只增不减容量),逐帧复用 |
| `RenderGraphBuilder`/`RenderGraphContext` | graph 各持有一个实例,逐节点换字段复用;文档标注"回调外禁止持有" |
| 编译期集合 | `HashSet`、分配走查的排序索引数组与存活分配列表均为 graph 常驻,每帧复用 |
| 池 | `Dictionary<TexturePoolKey, KeyState{All, Idle, Freed}>` 常驻,三个列表只增容量;key 为 `readonly struct`(宽/高/格式/usage/mip)实现 `IEquatable` |
| 稳态 backing | 分配走查确定性 + sticky 优先 → 同一 `RenderGraphTexture` 逐帧拿到同一底层纹理 → 不 Rebind、材质不重绑 |
| 禁止事项写入节点契约 | Setup/Execute 不得分配(无 LINQ、无闭包、无 `new` 集合);`CLAUDE.md` 的 `for`/`Span` 规范适用 |

### 3.6 延迟提交(command collection)

现状:`RenderContext.End()` → `RenderingSystem.ScheduleCommandBuffer()` → 立即 `device.Submit`。

改造(`Src/Alco.Rendering/RenderingSystem.cs`,内部 API,不影响既有调用方):

```csharp
internal void BeginCommandCollection();   // 开启收集域;嵌套调用抛 InvalidOperationException
internal int FlushCommandCollection();    // 批量 Submit 收集到的 command buffer,返回提交数
```

- 收集域激活时,`ScheduleCommandBuffer` 改为入队(常驻 `List<GPUCommandBuffer>`,复用容量);未激活时维持立即提交——ImGui、ForwardPipeline、readback 等既有路径行为不变;
- `GPUDevice` 新增 `Submit(ReadOnlySpan<GPUCommandBuffer>)`;WebGPU 端收集 native buffer 到常驻数组,单次 `wgpuQueueSubmit`(绑定已存在);NoGPU 端空实现;
- 正确性:wgpu 隐式同步按提交顺序生效,收集域内顺序与今天逐次提交完全一致;
- **已知风险**:uniform 重写语义变化(见 12.1),通过审计 + 契约约束解决。

### 3.7 资源池与外部纹理 framebuffer

- 池条目 `PooledTexture`:首次物化时创建 `GPUTexture` + 标准 attachment view(+depth/stencil 采样 view),条目随池常驻;
- transient 的 `GPUFrameBuffer` 由阶段 0 新增的 `CreateExternalFrameBuffer` 从池条目的纹理 + 视图组合烘焙,**不拥有纹理**;backing 变化时才重建,稳态零成本;
- `RenderGraphTexture` 门面 `RenderTexture` 通过新增 `internal Rebind(GPUFrameBuffer)` 原地换背并 `Version++`(复用 `Resize` 的重建逻辑,抽出公共路径);
- `Resize`:池整体清空(延迟销毁兜底在飞 GPU 工作),transient 按新尺寸重新物化;
- 跨视图:每个视图/管线持有各自 graph 与池,不共享(多窗口现状即每视图独立管线)。

## 4. RHI 增强(阶段 0)

全部为增量改动,不改变既有行为;NoGPU 后端同步桩。

### 4.1 Attachment load/store op

新增:

```csharp
// Src/Alco.Graphics/Enums/AttachmentLoadOp.cs
public enum AttachmentLoadOp : byte { Load, Clear }
// Src/Alco.Graphics/Enums/AttachmentStoreOp.cs
public enum AttachmentStoreOp : byte { Store, Discard }
// Src/Alco.Graphics/Structs/AttachmentOps.cs
public readonly struct AttachmentOps
{
    public AttachmentLoadOp LoadOp { get; init; }   // 默认 Load
    public AttachmentStoreOp StoreOp { get; init; } // 默认 Store
    public static readonly AttachmentOps Default = new();
}
```

`GPUCommandBuffer.BeginRender` 既有重载增加可选参数 `ReadOnlySpan<AttachmentOps> colorOps = default, AttachmentOps? depthOps = null`(含 timestamp 重载);抽象 `BeginRenderCore`/`BeginRenderTimestampCore` 同步加参。

WebGPU 端(`WebGPUCommandBuffer.BeginRenderInternal`,`WebGPUCommandBuffer.cs:132-214`):在现有 clear 处理之后应用 ops——`storeOp` 无条件覆盖;`loadOp` 仅在该 attachment 未被 clear 指定时生效(clear 已隐含 `LoadOp.Clear`)。NoGPU:接受并忽略。

语义不变式:不传 ops 时逐字节等同现状。

### 4.2 批量提交

```csharp
// GPUDevice
public void Submit(ReadOnlySpan<GPUCommandBuffer> commandBuffers);  // 空 span 为 no-op
protected abstract void SubmitCore(ReadOnlySpan<GPUCommandBuffer> commandBuffers);
```

- WebGPU:常驻 `WGPUCommandBuffer[]`(只增容量),逐个 `TakeBuffer()` 后单次 `wgpuQueueSubmit`,再逐个 release 计数;
- NoGPU:no-op。

### 4.3 外部纹理 framebuffer

```csharp
// Src/Alco.Graphics/Descriptor/ExternalFrameBufferDescriptor.cs
public struct ExternalFrameBufferDescriptor
{
    public required GPUAttachmentLayout AttachmentLayout { get; init; }
    public required GPUTexture[] Colors { get; init; }         // 长度须等于 layout 颜色数
    public required GPUTextureView[] ColorViews { get; init; }
    public GPUTexture? DepthStencil { get; init; }
    public GPUTextureView? DepthStencilView { get; init; }
    public GPUTextureView? DepthView { get; init; }
    public GPUTextureView? StencilView { get; init; }
    public required uint Width { get; init; }
    public required uint Height { get; init; }
    public string Name { get; init; }
}

// GPUDevice
public GPUFrameBuffer CreateExternalFrameBuffer(in ExternalFrameBufferDescriptor descriptor);
```

- WebGPU:复用 `WebGPUFrameBuffer` 的 descriptor 烘焙逻辑(抽出共享构造路径),跳过纹理创建;`Dispose` 仅释放视图(自有部分)与 native 内存,**不释放传入纹理**;
- 校验:纹理尺寸/格式与 layout 一致,否则 `GraphicsException`;
- NoGPU:返回 `NoFrameBuffer` 桩。

### 4.4 阶段 0 文件改动表

| 文件 | 改动 |
|---|---|
| `Src/Alco.Graphics/Enums/AttachmentLoadOp.cs` | 新增 |
| `Src/Alco.Graphics/Enums/AttachmentStoreOp.cs` | 新增 |
| `Src/Alco.Graphics/Structs/AttachmentOps.cs` | 新增 |
| `Src/Alco.Graphics/Abstraction/GPUCommandBuffer.cs` | BeginRender 各重载加 ops 可选参数;抽象 core 加参 |
| `Src/Alco.Graphics/WGPU/WebGPUCommandBuffer.cs` | `BeginRenderInternal` 应用 ops |
| `Src/Alco.Graphics/NoGPU/NoCommandBuffer.cs` | 签名对齐 |
| `Src/Alco.Graphics/Abstraction/GPUDevice.cs` | `Submit(span)` 重载 + `CreateExternalFrameBuffer` 工厂 |
| `Src/Alco.Graphics/Descriptor/ExternalFrameBufferDescriptor.cs` | 新增 |
| `Src/Alco.Graphics/WGPU/WebGPUDevice.cs` | `SubmitCore(span)` + `CreateExternalFrameBufferCore` |
| `Src/Alco.Graphics/WGPU/WebGPUFrameBuffer.cs` | 抽出共享烘焙逻辑,支持外部纹理路径 |
| `Src/Alco.Graphics/NoGPU/NoDevice.cs`、`NoFrameBuffer.cs` | 桩对齐 |
| `Src/Alco.Rendering/RenderingSystem.cs` | `BeginCommandCollection`/`FlushCommandCollection`(internal) |

## 5. Graph 基础设施(阶段 1)

新增 `Src/Alco.Rendering/Graph/`(公开 API)与 `Graph/Internal/`(内部实现):

| 文件 | 职责 |
|---|---|
| `Graph/IRenderGraphNode.cs` | 节点接口(见 3.2) |
| `Graph/RenderGraph.cs` | 注册、资源工厂、Execute/Resize 驱动 |
| `Graph/RenderGraphBuilder.cs` | Setup 期读写声明(密封类,逐节点复用) |
| `Graph/RenderGraphContext.cs` | Execute 期上下文(密封类,逐节点复用) |
| `Graph/RenderGraphTexture.cs` | 资源句柄 + `RenderTexture` 门面;internal Rebind 触发 |
| `Graph/RenderGraphTextureDescriptor.cs` | transient 描述(见 3.2) |
| `Graph/Internal/RenderGraphCompiler.cs` | 纯逻辑:校验、裁剪、生命周期、acquire/release 计划;**不引用任何 GPU 类型**,可单测 |
| `Graph/Internal/RenderGraphNodeRecord.cs` | 节点注册记录(读/写/标志位数组,逐帧复用) |
| `Graph/Internal/RenderGraphTexturePool.cs` | 池:key → KeyState(All/Idle/Freed);Allocate(sticky 优先)/ReleaseExpired/BeginFrame/Clear |
| `Graph/Internal/TexturePoolKey.cs` | `readonly struct`,`IEquatable`,GPU 无关 |
| `Src/Alco.Rendering/Texture/RenderTexture.cs` | 抽出 Rebind 公共路径(Resize 复用),新增 internal Rebind |

**可测试性设计**:编译器输入为 `RenderGraphNodeRecord[]` + 资源表,输出为存活位掩码 + acquire/release 计划;池通过抽象工厂创建底层纹理对象(测试用 fake)。`InternalsVisibleTo("Alco.Rendering.Test")` 已存在(`Src/Alco.Rendering/AssemblyInfo.cs`),直接测 internal 类型。

## 6. Deferred 管线迁移(阶段 2)

### 6.1 节点分解

`PBRDeferredPipeline` 持有一个 `RenderGraph` 与全部 transient,公有门面(`GBuffer`/`ShadowMap`/`ForwardRenderTexture`)改为返回各 transient 的 `RenderTexture` 门面,**外部代码零感知**:

```
[0] ShadowPassNode        Write(shadowMap)                       IsEnabled := ShadowEnabled
[1] GBufferPassNode       Write(gbuffer)                         常启用
[2] CallbackNode          (AfterGBufferCallback,无资源声明)      有订阅时启用
[3] HBAO plugin 节点      Read(gbuffer) Write(aoOut)             (阶段 3,先走 6.4 适配)
[4] VoxelGI plugin 节点   Read(gbuffer) Write(giDiffuse, giSpecular)
[5] LightingNode          Read(gbuffer)
                          Read(shadowMap)   — 仅 ShadowEnabled
                          Read(aoOut/gi*)   — 仅对应插件产出
                          Write(sceneColor)
                          ProducesOutput()  — 仅 destination == null(保 headless 语义)
[6] VolumetricLightNode   ReadWrite(sceneColor)                  IsEnabled := VolumetricLightEnabled
[7] ForwardContentNode    Read(gbuffer) ReadWrite(sceneColor)    IsEnabled := 有启用的 IForwardRenderNode 子节点
[8] SSR plugin 节点       Read(gbuffer) ReadWrite(sceneColor)    (阶段 3)
[9] PostProcessNode ×N    Read(上一节点输出) Write(自有输出)      Bloom/FXAA/Tonemap,适配 IContentProcessorNode
[10] BlitNode             Read(链尾输出) ProducesOutput()        destination != null 时启用
```

- 节点 0/1/5/6 的执行体为现有 `RenderShadowPass`/`RenderGBufferPass`/`RenderLighting`/`RenderVolumetricLight` 逻辑原样搬迁(含时间戳插桩与 profiler 计数,槽位常量保持不变);
- `IGBufferRenderNode`/`IShadowRenderNode` 契约不变:仍由 GBuffer/Shadow 节点在 pass 内回调;`ForwardContentNode` 持有原 chain 的 `IForwardRenderNode` 列表逐个回调;
- 后处理穿线:pipeline 持有内部 `PostChainState`(每帧 Setup 前重置为 sceneColor);LightingNode(常启用)在 Setup 中设置 current;各 PostProcessNode Setup 时 `Read(current)` → `Write(ownOut)` → current 前推;BlitNode `Read(current)`。依赖"Setup 按注册顺序执行"这一已文档化保证。

### 6.2 深度共享,消除 depth copy

- `sceneColor` 以 `DepthSource = gbuffer` 创建:其组合 framebuffer 的颜色来自池,深度复用 gbuffer transient 的池化 Depth32 纹理;
- LightingNode 的 pass begin:颜色 `LoadOp.Load`(或 clear),**深度 `LoadOp.Load`**(阶段 0 的 ops 参数),lighting 材质 `DepthStencilState.Default`(Always、不写深度,`Structs/DepthStencilState.cs:26`)不触碰深度;
- ForwardContentNode 直接对已填充的 G-buffer 深度做硬件深度测试(玻璃材质 `DepthStencilState.Read`);
- 删除 `_depthCopyCommand` 及 `Render()` 中的 copy 段(`PBRDeferredPipeline.cs:836-843`);
- 正确性前提:gbuffer 生命周期覆盖最后一个读者——ForwardContentNode/SSR 声明 `Read(gbuffer)` 即由编译器自动保证;wgpu 隐式同步处理两 pass 间 depth hazard。

### 6.3 兼容语义对照

| 现状行为 | 迁移后 |
|---|---|
| `ShadowEnabled=false` → shadow pass 空跑 | shadow 节点禁用 + lighting 不读 → 自动裁剪,零 GPU 开销 |
| `VolumetricLightEnabled=false` → 早退 | 节点禁用 → 自动裁剪 |
| 无 forward 内容 → 跳过 depth copy | ForwardContentNode 禁用 → 无 depth copy(已删除),链自然缩短 |
| destination==null → 内容照渲、processor 跳过 | LightingNode 标记 ProducesOutput,blit 禁用,post 链无人引用自动裁剪 |
| `AfterGBufferCallback` 在插件前调用 | CallbackNode 注册序在插件节点之前 |
| 插件输出缺省绑白/黑 fallback | LightingNode 按 Setup 期实际声明绑定,缺省逻辑不变 |

### 6.4 插件过渡形态(阶段 2 内)

阶段 2 不改插件内部:为每个 `IRenderPlugin` 生成 `PluginAdapterNode`(黑盒节点,`Execute` 内原样调用 `plugin.Execute(RenderPluginContext)`);插件自有的输出 RT(AO/GI 全尺寸)以 `Import` 注册进图,LightingNode 按插件使能状态条件 Read。此形态下插件输出暂不参与别名,阶段 3 再 transient 化。

### 6.5 阶段 2 文件改动表

| 文件 | 改动 |
|---|---|
| `Src/Alco.Rendering/Deferred/PBRDeferredPipeline.cs` | 内部重写:持有 RenderGraph + transient;删除 `_gbufferRT/_shadowRT/_forwardRT/_depthCopyCommand` 直接所有权;`Render()` 改为 graph 驱动 |
| `Src/Alco.Rendering/Deferred/Nodes/ShadowPassNode.cs` | 新增(搬迁 RenderShadowPass + 插桩) |
| `Src/Alco.Rendering/Deferred/Nodes/GBufferPassNode.cs` | 新增 |
| `Src/Alco.Rendering/Deferred/Nodes/LightingNode.cs` | 新增(含条件 Read 声明) |
| `Src/Alco.Rendering/Deferred/Nodes/VolumetricLightNode.cs` | 新增 |
| `Src/Alco.Rendering/Deferred/Nodes/ForwardContentNode.cs` | 新增(IForwardRenderNode 列表适配) |
| `Src/Alco.Rendering/Deferred/Nodes/PostProcessNode.cs` | 新增(IContentProcessorNode 适配 + 穿线) |
| `Src/Alco.Rendering/Deferred/Nodes/BlitNode.cs` | 新增 |
| `Src/Alco.Rendering/Deferred/Nodes/CallbackNode.cs` | 新增 |
| `Src/Alco.Rendering/Deferred/Nodes/PluginAdapterNode.cs` | 新增(阶段 3 替换) |
| `Src/Alco.Rendering/Deferred/IRenderPlugin.cs` | 视适配需要微调 context 装配 |

## 7. 插件与子图(阶段 3)

黑盒原则:**插件内部结构不变,只有"图的可见面"变换**。

1. **HBAO**:`_rawAO` 保持私有;`_aoResult`(全尺寸)改为 transient(注册时经管线提供的工厂创建);blur pass 终点写入 transient;节点 Setup `Read(gbuffer) Write(aoOut)`;
2. **VoxelGI**:9-shader 子管线、页池、clipmap、history 全部私有不动;`_giDiffuseFullRes/_giSpecularFullRes` 两张全尺寸输出改 transient;节点 Setup `Read(gbuffer) Write(giDiffuse, giSpecular)`;其 rate-limit 与内部 ping-pong 不受图管辖;
3. **SSR**:`_sceneCopy/_reflectionRaw` 改 transient;两张 history 以 `Import` 注册(跨帧持久);合成阶段 `ReadWrite(sceneColor)`;
4. 删除 `PluginAdapterNode`,插件直接实现 `IRenderGraphNode`(`Setup` 声明 + `Execute` 原逻辑);`RegisterPlugin` 公有签名不变,内部转为 `graph.Use(plugin)`。

**逐帧 uniform 审计**(12.1 的前置):逐一确认 HBAO/VoxelGI/SSR/GBufferRenderer/ShadowRenderer 在收集域内不存在"写 uniform → 录制 → 重写同一 uniform → 再录制"模式;存在则把上传前移到录制前。

## 8. 公开 API 变化与兼容性

### 保持不动(外部零感知)

- `PBRDeferredPipeline`:`Render`/`Resize`/`Use`/`Remove`/`Get`/`RegisterPlugin`/`UnregisterPlugin`/`GetPlugin`/`SetCamera`/`ComputeShadowCascades`/`UpdatePointLights`/全部场景属性/`GBuffer`/`ShadowMap`/`ForwardRenderTexture`/`GBufferLayout`/`ShadowLayout`/`ForwardLayout`/`ShadowDataBuffer`/`LightingDataBuffer`/`PointLightBuffer`/`GBufferContext`/`ShadowContext`/`AfterGBufferCallback`/`Profiler`;
- `RenderNodeChain`/`ForwardPipeline` 全路径;
- `RenderTexture` 公有面;
- Sandbox 34 代码无需改动。

### 收窄(已验证 Sandbox 无使用,grep 全仓确认后执行)

- `BeginShadowPass`/`EndShadowPass`/`BeginGBufferPass`/`EndGBufferPass`/`RenderShadowPass`/`RenderGBufferPass`/`RenderLighting`/`RenderVolumetricLight`/`ExecutePlugins` → 降为 internal(手动分步驱动与 transient 模型冲突,见 12.4);
- `ExecuteShadowSubContext`/`ExecuteGBufferSubContext` 随宿主节点降为 internal。

### 新增

- `Src/Alco.Rendering/Graph/` 公开 API(§3.2);
- RHI 增量(§4),全部向后兼容。

## 9. 实施步骤

每步独立可验证,完成后 `dotnet build` + 相关测试全绿再进下一步。

| # | 内容 | 验证 |
|---|---|---|
| 0 | RHI 三件套:ops 参数、批量 Submit、外部 framebuffer(§4) | build;`Test/Alco.Graphics.Test` 全绿;新增 ops/外部 framebuffer 单测 |
| 1 | Graph 核心 + 池 + 编译器 + RenderTexture.Rebind(§5) | build;新增 Graph 单测全绿(§10) |
| 2 | `PBRDeferredPipeline` 迁移(§6,含节点文件、depth 共享、删 depth copy、profiler 原样搬迁) | build;全部既有测试;Sandbox 34 画面对比(§11) |
| 3 | 插件 transient 化 + history import(§7) | build;全部测试;Sandbox 34 全特性画面对比 |
| 4 | 验收:全套测试 + 稳态零分配验证 + 内存/submit 计数对比(§11) | §11 清单 |

## 10. 单元测试计划

位置:`Test/Alco.Rendering/Graph/`(NUnit,沿用现有项目约定)。

**TestRenderGraphCompiler**(纯逻辑,fake 池):

1. 线性链全存活(blit 根拉起全链);
2. 未引用写入 → 写入节点被裁;
3. 链尾禁用 → 上游逐段裁剪;
4. `ShadowEnabled` 场景:lighting 条件不读 → shadow 裁;条件读 → 存活;
5. `ReadWrite` 链(post)依赖前向传播全存活;
6. `ProducesOutput` 条件根(destination==null 语义);
7. read-before-write → `InvalidOperationException` 含节点/资源名;
8. 生命周期:不重叠两 transient 获同一池纹理;重叠则不同;
9. imported 永不入池、不被裁减需求以外的影响;
10. 多写者后者覆盖语义;
11. 池:key 相等性(尺寸/格式/usage)、Allocate 优先级(sticky 从 Freed/Idle 取回、Freed 最新优先、Idle 最旧兜底、factory 新建)、BeginFrame 重置、Clear 后重建。

**TestRenderGraphAllocation**:稳态(固定节点与使能)连续 100 帧 Setup+Compile+Execute(fake 节点/池),`GC.GetAllocatedBytesForCurrentThread` 增量 ≈ 0(允许极小常数阈值)。

**RHI 单测**(`Test/Alco.Graphics.Test`):ops 参数透传(NoGPU 可验证签名),外部 framebuffer 校验路径。

## 11. 验收方案(Sandbox 34)

1. **画面回归**:重构前先运行 `Sandbox/34-PBRDeferred` 抓取基准截图(各调试视图:正常、AO、GI、阴影、cascade),重构后同场景同机位截图对比(目视 + 像素差在容差内,tonemap 前 HDR 路径一致时差异应为 0);
2. **特性开关矩阵**:`ShadowEnabled`×`VolumetricLightEnabled`×`GiEnabled`×forward 玻璃有无,确认裁剪生效且画面正确;
3. **性能计数对比**(同场景):逐帧 submit 数 15–20 → 1–2;池化后纹理常驻字节数下降(附实测数字回填本节);
4. **零分配验证**:§10 的分配单测 + Sandbox 稳态运行 GC 观察;
5. `dotnet test` 全量绿(含 `ValidateShader`);
6. resize 反复触发(窗口拖拽),确认池 Clear/重建与材质重绑无异常。

## 12. 风险与陷阱

### 12.1 延迟提交的 uniform 重写语义

立即提交时代码可以"写 uniform → submit → 重写同一 buffer → submit";收集域内所有提交延后到 flush,先录制的 pass 会看到**最后一次**写入的值。

- 已确认安全:shadow cascade(4 独立槽,`PBRDeferredPipeline.cs:919-931` 注释本就假设"全部录制完才提交")、push constants(录制时快照入 command buffer)、每帧一次的上传(相机、lighting data、点光、GI rate-limited);
- 措施:阶段 2/3 逐一审计节点内 `UpdateBuffer` 调用点;契约写入 `RenderGraph` 文档:**节点内所有 uniform 上传必须先于依赖它的 pass 录制**;发现冲突则上传前移。

### 12.2 别名与视图悬挂

release 后纹理回池可能被同帧后续 acquire 覆盖内容;已裁资源门面仍持有旧 view。只要生命周期分析正确,死者不再被采样。编译器单测必须覆盖(§10.8);调试模式可对已 release 资源的访问抛错(门面内部加帧号戳,开发期断言)。

### 12.3 使能集合抖动引发的重绑

- **池 LIFO 在相同调度下确定**;使能切换后一两帧内 backing 变化 → `Version++` → 材质重建(正确性由既有机制保证,成本一次性)。sticky 优先规则使切换后至多一帧即恢复稳定分配。`RenderGraphTexture` 文档注明:高频逐帧切换节点使能会产生重绑 churn,不这么用。

### 12.4 手动分步驱动与 transient 的冲突

transient backing 仅在 graph 执行期内物化,`Begin*/Render*Pass` 手动模式无法提供有效目标。故公有手动 API 收窄为 internal(§8);已有"游戏手动编排帧流程"由 `SetCamera`/`ComputeShadowCascades`/`UpdatePointLights` + `Render()` 承载(Sandbox 34 即如此)。

### 12.5 其他

- **NoGPU 对齐**:所有 RHI 新增路径同步桩,`Test/*` 用 NoGPU 的用例保持绿;
- **共享深度的 layout 约束**:`sceneColor` 的 depth 格式必须与 `DepthSource` 一致(Depth32Float),`CreateTransient` 校验不符抛 `ArgumentException`;
- **延迟销毁**:池 Clear/替换走 `BaseGPUObject` 延迟销毁(`GPUDevice.cs:461-476`),在飞帧安全;
- **GpuTimestampSampler 槽位**:沿用现有 8 槽常量和插桩位置,仅宿主从 pipeline 移到对应节点,行为不变;
- **多窗口**:每视图独立 graph/池,与现状一致;不引入跨视图资源共享。

## 13. 总结

- 以**轻量 Render Graph**(声明式依赖 + 自动裁剪 + transient 池化/别名 + 统一提交)重构 deferred 管线;不做 barrier 推导与多队列(wgpu 隐式同步已覆盖);
- 资源模型复用引擎既有"稳定身份 + Version"契约,材质系统与节点代码零感知;
- 公开 API 基本冻结(Sandbox 34 无需改动),手动分步驱动 API 收窄为 internal;
- 分 4 步实施,每步 build + 测试可验证;纯逻辑(编译器/池)NUnit 单测覆盖,画面以 Sandbox 34 对比验收;
- 预期收益:消除全部冗余全尺寸 RT(≥8 张参与别名)、提交数 15–20 → 1–2、depth copy 删除、特性开关零空跑、新增 pass 从 6–8 处改动降为 1 个节点类 + `Use()`。
