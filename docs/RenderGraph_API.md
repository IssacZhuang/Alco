# RenderGraph 公开 API 设计

本文档描述 render graph 的公开 API 模型:管线如何组合、用户如何扩展。它面向"不能访问 internal、不能修改引擎代码"的引擎用户 —— 这也是本 API 的设计验收标准:

> **用户仅凭 public API,就能从零搭建一条完整渲染管线,也能把现有管线上任意一个功能替换或重编排为自己的实现。**

## 1. 模型:一切皆节点,管线即组合

旧 API 的问题在于"概念分裂":`IForwardRenderNode` / `IContentProcessorNode` 方法同名语义不同;`IGBufferRenderNode` / `IShadowRenderNode` 等接口把管线结构硬编码进类型系统;旧 `PBRDeferredPipeline` 内部对 HBAO / VoxelGI / SSR 做类型判断,本质是分发器而非组合。

现在的模型只有两层概念:

- **`IRenderGraphNode`**(`Src/Alco.Rendering/Graph/IRenderGraphNode.cs`):一切管线行为的唯一载体。节点每帧执行两阶段:
  - `Setup(RenderGraphBuilder)`:按注册顺序声明本帧读写的 `RenderGraphTexture`,不得分配托管内存;
  - `Execute(in RenderGraphContext)`:仅当节点在剔除中存活时记录 GPU 工作。
  - 剔除规则:写出的资源未被任何 `ProducesOutput()` 节点(直接或间接)消费 → 节点不执行,其 transient 不分配。特性开关(`IsEnabled=false`)与"无消费者"走同一条裁剪路径。
- **组合积木**(`Src/Alco.Rendering/Graph/Nodes/`):管线由这些公开节点类组合而成,没有任何一个需要引擎特权:

  | 积木 | 角色 |
  |---|---|
  | `RenderGraph` | 资源表、transient 池、剔除;`Use` / `InsertBefore` / `InsertAfter` / `Remove` / `CreateTransient` / `DestroyTransient`。节点按注册序录制,每个节点的 command buffer 在其完成时即时提交。设置了 `Profiler` 时,`Execute` 自动包 `BeginFrame` / `EndFrame` |
  | `RenderChain` | 线性内容链的穿线状态(`Current` / `Reset` / `Advance`),依赖"Setup 严格按注册顺序执行"这一图契约 |
  | `RGNode_Clear` | 清一个资源 |
  | `RGNode_GeometryPass` | 几何 pass:清目标 + 循环 `Content: List<IRenderPassContent>` |
  | `RGNode_ShadowPass` | 级联阴影 pass:2x2 atlas + 循环 `Content: List<IShadowPassContent>` |
  | `RGNode_DeferredLighting` | 全屏光照 pass,输入(`AoInput` / `GiDiffuseInput` / ...)是公开可设的资源槽;每帧数据组装委托给 `PrepareData` |
  | `RGNode_SceneContent`(abstract) | 原地向 `chain.Current` 绘制内容(场景、透明、UI);只需重写 `OnRender` |
  | `RGNode_ChainTransform`(abstract) | 后处理:读 `chain.Current` → 写自有 transient → `Advance`;只需重写 `OnProcess` |
  | `RGNode_FullscreenPass` / `RGNode_FullscreenOverlay` | 全屏材质绘制(变换 / 原地叠加)的现成实现 |
  | `RGNode_Blit` | 链尾 → 帧目标;headless(目标为 null)时自动禁用,是通常的剔除根 |
  | `RGNode_Callback` | 图中间的托管回调(事件钩子、每帧数据上传) |

- **内容接口按 pass 定界**:`IRenderPassContent`(`OnRender(context, layout)`)与 `IShadowPassContent`(`OnRenderShadow(context, cascade)`)注册在 pass 节点的 `Content` 列表上,管线本身不认识它们 —— 管线不认识任何内容类型,也就不存在"按功能区分的注册接口"。

## 2. 没有管线类型:壳 + 特性对象 + 工厂预设

forward 与 deferred 不再是两个类。**管线类型之间的区别只在于图里组合了哪些节点**。三层结构:

- **`RenderPipeline`**(`Pipeline/RenderPipeline.cs`):唯一的管线壳。持有一张 `RenderGraph`、一条以 scene color 为根的 `RenderChain`、一个最终 `RGNode_Blit`;提供 `Use` / `Remove` / `Get` / `Render(destination)` / `Resize` 与所有权/释放。公开构造器直接给出最小管线(clear + blit);预设工厂内部走组合构造器装配好图再交给壳。
- **特性对象**:跨节点共享的每帧状态不属于任何节点。`PBRSceneEnvironment`(`Deferred/PBRSceneEnvironment.cs`)持有太阳/天空/阴影/GI/体积光参数、相机、点光源列表、级联拟合(`ComputeShadowCascades`)以及三个 GPU buffer(`LightingDataBuffer` / `ShadowDataBuffer` / `PointLightBuffer`)。它不认识图:节点显式读它(lighting 节点的 `PrepareData` 回调调用 `AssembleLightingData` + `UploadLightingData`),组合方通过 `ShadowEnabledChanged` / `VolumetricLightEnabledChanged` 事件把开关同步到节点。
- **工厂预设**:`RenderPipelines.CreatePBRDeferred(...)`(`Pipeline/RenderPipelines.cs`)把"阴影 → GBuffer → 光照 → 体积光(可选)→ blit"的组合装配出来,返回 **`PBRDeferredPreset`** —— 一个纯引用包 + 所有权句柄:

  | 成员 | 内容 |
  |---|---|
  | `Pipeline` | 壳:`Render` / `Resize` / `Use` / `Graph` |
  | `Environment` | 场景特性对象(上文) |
  | `GBufferResource` / `ShadowMapResource` / `SceneColorResource` | transient 资源 |
  | `ShadowPass` / `GBufferPass` / `Lighting` / `VolumetricLight` / `FinalBlit` | 节点锚点 |
  | `GBufferLayout` / `ShadowLayout` / `ForwardLayout` / `PostProcessLayout` | pass layout |
  | `GBuffer` / `ShadowMap` / `ForwardRenderTexture` | 身份稳定的 facade |
  | `Profiler` | 性能计数器(graph 已接线;GPU 时间戳由两个回调节点读回) |
  | `AfterGBuffer` | GBuffer 之后、插件之前的事件(内部是一个 `RGNode_Callback`) |

  preset 不是黑盒:工厂方法体本身就是公开的参考组装,产物上的每个零件都可替换。

## 3. 扩展配方(recipes)

以下全部经 `Test/Alco.Rendering.Test/Graph/TestRenderGraphExtensibility.cs` 验证(NoGPU 后端)。

### 3.1 从零搭建一条管线

```csharp
var graph = new RenderGraph(rendering, width, height, "my_pipeline");
var chain = new RenderChain();
RenderGraphTexture scene = graph.CreateTransient(new RenderGraphTextureDescriptor(
    rendering.PreferredHDRPass, name: "my_scene"));

graph.Use(new RGNode_Clear(scene, [new ClearColorData(0, Vector4.Zero)], 1.0f));
graph.Use(new MySceneContent(graph, chain));      // : RGNode_SceneContent,重写 OnRender
graph.Use(new MyEffect(graph, chain, postLayout));// : RGNode_ChainTransform,重写 OnProcess
graph.Use(new RGNode_Blit(rendering, graph, chain, blitShader));

// 每帧:
chain.Reset(scene);
graph.Execute(destination);
```

最小前向管线也可以一行壳搞定:`new RenderPipeline(rendering, sceneLayout, blitShader, w, h)` 自带 clear + blit,之后 `Use(...)` 加内容/后处理节点。

### 3.2 给现有管线加后处理

```csharp
preset.Pipeline.Use(new RGNode_Bloom(rendering, preset.Graph, preset.PostChain,
    preset.PostProcessLayout, bloom, blitShader));
```

### 3.3 替换管线的一个阶段

```csharp
preset.Graph.Remove(preset.Lighting);            // 摘掉引擎光照
preset.Graph.InsertAfter(preset.GBufferPass, myLighting); // 换成自己的
// myLighting 在 Setup 里 builder.Write(preset.SceneColorResource)
```

### 3.4 内容注册进 pass

```csharp
preset.GBufferPass.Content.Add(myGeometryContent); // IRenderPassContent
preset.ShadowPass.Content.Add(myShadowCasters);    // IShadowPassContent
```

### 3.5 带自有输出的插件(HBAO / VoxelGI / SSR 模式)

插件自己持有 `Attach(...)` / `Detach()` 对称方法,签名接收的是它**实际依赖的零件**而不是某个管线类:

```csharp
hbao.Attach(preset.Graph, preset.Lighting, preset.GBufferResource, preset.Environment);
voxelGi.Attach(preset.Graph, preset.Lighting, preset.GBufferResource, preset.ShadowMapResource, preset.Environment);
var ssr = new RGNode_SSR(rendering, preset.Graph, preset.PostChain,
    preset.GBufferResource, preset.SceneColorResource, voxelGi, camera, preset.Environment, ...);
ssr.Attach(preset.FinalBlit);
```

Attach 内 `CreateTransient` 自有输出、`Graph.InsertBefore(lighting, this)`、把输出设进 `Lighting.AoInput` 等公开资源槽、向消费方材质绑定 facade(对象身份稳定,绑一次即可);Detach 反向执行(`Graph.Remove` + `DestroyTransient` + 清空资源槽)。

注意:图校验会拒绝"读取未被启用的上游写入的 transient",所以插件节点保持 always-enabled;禁用时必须同时清掉消费方的输入引用。

## 4. 不变量与契约

- **facade 身份稳定**:`RenderGraphTexture.Texture` 在 resize / 池重绑间保持对象身份,材质绑一次即可(Version 检查自动重建 bind group);
- **链确定性**:`Setup` 严格按注册顺序执行,`RenderChain` 据此穿线;
- **资源生命周期对称**:节点在图上创建 transient,就在自己的 `Dispose` 里 `DestroyTransient`(先判 `!graph.IsDisposed` —— 图自毁时会统一清理);
- **headless 语义**:目标为 null 时 `RGNode_Blit` 自动禁用,整条后处理链被剔除,`RGNode_SceneContent` 通过 `ProducesOutput` 自我扎根照常执行。
