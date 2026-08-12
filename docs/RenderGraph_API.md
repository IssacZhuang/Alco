# RenderGraph 公开 API 设计

本文档描述 render graph 的公开 API 模型:管线如何组合、用户如何扩展。它面向"不能访问 internal、不能修改引擎代码"的引擎用户 —— 这也是本 API 的设计验收标准:

> **用户仅凭 public API,就能从零搭建一条完整渲染管线,也能把现有管线上任意一个功能替换或重编排为自己的实现。**

## 1. 模型:一切皆节点,管线即组合

旧 API 的问题在于"概念分裂":`IForwardRenderNode` / `IContentProcessorNode` 方法同名语义不同;`IGBufferRenderNode` / `IShadowRenderNode` 等接口把管线结构硬编码进类型系统;`PBRDeferredPipeline` 内部对 HBAO / VoxelGI / SSR 做类型判断,本质是分发器而非组合。

现在的模型只有两层概念:

- **`IRenderGraphNode`**(`Src/Alco.Rendering/Graph/IRenderGraphNode.cs`):一切管线行为的唯一载体。节点每帧执行两阶段:
  - `Setup(RenderGraphBuilder)`:按注册顺序声明本帧读写的 `RenderGraphTexture`,不得分配托管内存;
  - `Execute(in RenderGraphContext)`:仅当节点在剔除中存活时记录 GPU 工作。
  - 剔除规则:写出的资源未被任何 `ProducesOutput()` 节点(直接或间接)消费 → 节点不执行,其 transient 不分配。特性开关(`IsEnabled=false`)与"无消费者"走同一条裁剪路径。
- **组合积木**(`Src/Alco.Rendering/Graph/Nodes/`):管线由这些公开节点类组合而成,没有任何一个需要引擎特权:

  | 积木 | 角色 |
  |---|---|
  | `RenderGraph` | 资源表、transient 池、剔除、单批提交;`Use` / `InsertBefore` / `InsertAfter` / `Remove` / `CreateTransient` / `DestroyTransient` |
  | `RenderChain` | 线性内容链的穿线状态(`Current` / `Reset` / `Advance`),依赖"Setup 严格按注册顺序执行"这一图契约 |
  | `ClearNode` | 清一个资源 |
  | `GeometryPassNode` | 几何 pass:清目标 + 循环 `Content: List<IRenderPassContent>` |
  | `ShadowPassNode` | 级联阴影 pass:2x2 atlas + 循环 `Content: List<IShadowPassContent>` |
  | `DeferredLightingNode` | 全屏光照 pass,输入(`AoInput` / `GiDiffuseInput` / ...)是公开可设的资源槽 |
  | `SceneContentNode`(abstract) | 原地向 `chain.Current` 绘制内容(场景、透明、UI);只需重写 `OnRender` |
  | `ChainTransformNode`(abstract) | 后处理:读 `chain.Current` → 写自有 transient → `Advance`;只需重写 `OnProcess` |
  | `FullscreenPassNode` / `FullscreenOverlayNode` | 全屏材质绘制(变换 / 原地叠加)的现成实现 |
  | `BlitNode` | 链尾 → 帧目标;headless(目标为 null)时自动禁用,是通常的剔除根 |
  | `CallbackNode` | 图中间的托管回调 |

- **内容接口按 pass 定界**:`IRenderPassContent`(`OnRender(context, layout)`)与 `IShadowPassContent`(`OnRenderShadow(context, cascade)`)注册在 pass 节点的 `Content` 列表上,管线本身不认识它们 —— 管线不认识任何内容类型,也就不存在"按功能区分的注册接口"。

## 2. 管线是组合,不是黑盒

`PBRDeferredPipeline` 不再分发,而是把组装产物全部公开:

- `Graph`:整张图,可任意 `InsertBefore` / `InsertAfter` / `Remove`;
- 资源:`GBufferResource` / `ShadowMapResource` / `SceneColorResource`;
- 节点锚点:`ShadowPass` / `GBufferPass` / `LightingNode` / `VolumetricLightNode` / `FinalBlit`;
- `PostChain`:后处理链;
- `PostProcessLayout`:后处理输出 transient 的 layout(color-only,与场景色同格式);
- `Use(node)` 只是 `Graph.InsertBefore(FinalBlit, node)` 的糖。

`ForwardPipeline` 同样是最小组合:`ClearNode` + 用户节点 + `BlitNode`,公开 `Graph` / `Chain` / `SceneColorResource` / `PostProcessLayout` / `FinalBlit`。

## 3. 扩展配方(recipes)

以下全部经 `Test/Alco.Rendering.Test/Graph/TestRenderGraphExtensibility.cs` 验证(NoGPU 后端)。

### 3.1 从零搭建一条管线

```csharp
var graph = new RenderGraph(rendering, width, height, "my_pipeline");
var chain = new RenderChain();
RenderGraphTexture scene = graph.CreateTransient(new RenderGraphTextureDescriptor(
    rendering.PreferredHDRPass, name: "my_scene"));

graph.Use(new ClearNode(rendering, scene, [new ClearColorData(0, Vector4.Zero)], 1.0f));
graph.Use(new MySceneContent(graph, chain));      // : SceneContentNode,重写 OnRender
graph.Use(new MyEffect(graph, chain, postLayout));// : ChainTransformNode,重写 OnProcess
graph.Use(new BlitNode(rendering, graph, chain, blitShader));

// 每帧:
chain.Reset(scene);
graph.Execute(destination);
```

### 3.2 给现有管线加后处理

```csharp
pipeline.Use(new BloomNode(rendering, pipeline.Graph, pipeline.PostChain,
    pipeline.PostProcessLayout, bloom, blitShader));
```

### 3.3 替换管线的一个阶段

```csharp
pipeline.Graph.Remove(pipeline.LightingNode);            // 摘掉引擎光照
pipeline.Graph.InsertAfter(pipeline.GBufferPass, myLighting); // 换成自己的
// myLighting 在 Setup 里 builder.Write(pipeline.SceneColorResource)
```

### 3.4 内容注册进 pass

```csharp
pipeline.GBufferPass.Content.Add(myGeometryContent); // IRenderPassContent
pipeline.ShadowPass.Content.Add(myShadowCasters);    // IShadowPassContent
```

### 3.5 带自有输出的插件(HBAO / VoxelGI / SSR 模式)

插件自己持有 `Attach(pipeline)` / `Detach()` 对称方法:Attach 内 `CreateTransient` 自有输出、`Graph.InsertBefore(pipeline.LightingNode, this)`、把输出设进 `LightingNode.AoInput` 等公开资源槽、向消费方材质绑定 facade(对象身份稳定,绑一次即可);Detach 反向执行(`Graph.Remove` + `DestroyTransient` + 清空资源槽)。

注意:图校验会拒绝"读取未被启用的上游写入的 transient",所以插件节点保持 always-enabled;禁用时必须同时清掉消费方的输入引用。

## 4. 不变量与契约

- **facade 身份稳定**:`RenderGraphTexture.Texture` 在 resize / 池重绑间保持对象身份,材质绑一次即可(Version 检查自动重建 bind group);
- **链确定性**:`Setup` 严格按注册顺序执行,`RenderChain` 据此穿线;
- **资源生命周期对称**:节点在图上创建 transient,就在自己的 `Dispose` 里 `DestroyTransient`(先判 `!graph.IsDisposed` —— 图自毁时会统一清理);
- **headless 语义**:目标为 null 时 `BlitNode` 自动禁用,整条后处理链被剔除,`SceneContentNode` 通过 `ProducesOutput` 自我扎根照常执行。
