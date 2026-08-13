# RenderContext 重构计划

## 目录

1. [背景与现状](#1-背景与现状)
2. [目标与非目标](#2-目标与非目标)
3. [总体设计](#3-总体设计)
4. [新 API 形态](#4-新-api-形态)
5. [兼容与迁移策略](#5-兼容与迁移策略)
6. [影响面清单](#6-影响面清单)
7. [实施步骤](#7-实施步骤)
8. [单元测试计划](#8-单元测试计划)
9. [验收方案](#9-验收方案)
10. [风险与陷阱](#10-风险与陷阱)
11. [总结](#11-总结)

---

## 1. 背景与现状

### 1.1 当前形态

`RenderContext`(`Src/Alco.Rendering/Renderer/RenderContext.cs`)把三件事绑死在一个类里:

1. **命令缓冲所有权与提交**:构造即持有一个 `GPUCommandBuffer`(`RenderContext.cs:54`);`End()` 立即 `ScheduleCommandBuffer` → `device.Submit`(`RenderContext.cs:298-323`);
2. **pass 作用域管理**:`Begin(target,...)` 打开 render pass、`End()` 关闭,timestamp resolve 挂在两者之间(`RenderContext.cs:304-316`);
3. **绘制人体工学**:mesh 绑定缓存、push constants、`Draw*` 系列、`ICommandListener` 监听。

由此产生"一次 `Begin`/`End` = 一个 pass = 一次提交"的硬结构。render graph 的每个节点各自持有 `RenderContext`(15 个节点均如此,如 `RGNode_GeometryPass.cs:37`),一帧提交数 ≈ 存活节点数 + 插件自留提交。全特性 PBR deferred 一帧 **15–20 次 `wgpuQueueSubmit`**(见 `docs/RenderGraph_Refactor.md` §1.1 的量化)。

### 1.2 已否决的方案

"命令收集域 + 批量提交"(多个 command buffer 收集后一次 `Submit(span)`)已实现后被回退(commit `de9d6a1b`):它把提交点从绘制 API 里抽走,却没有改变"每节点一个 command buffer"的结构,复杂度换收益不划算。正确的路径是**让一帧只用一个 command buffer**——节点把各自 pass 顺序录进共享 `RenderContext`,图结束一次提交;可复用子命令仍打包 `SubRenderContext`(render bundle)。

### 1.3 API 形态问题

`Begin`/`End` 是裸配对:忘记 `End`、异常跳过 `End`、`End` 两次,都只能运行时靠断言兜底(`WeaponVisualCache.cs:606-610` 被迫用 `Framebuffer != null` 判断 context 是否打开来做异常清理)。RHI 层已有更优形态:`GPUCommandBuffer.RenderPass`/`ComputePass` 是 readonly struct RAII 作用域(`GPUCommandBuffer.cs:11-251`),`using` 即关闭。本重构把同一形态提升到渲染层。

## 2. 目标与非目标

### 目标(按优先级)

1. **提交合并**:render graph 一帧一个 command buffer、一次 submit(插件收编后 15–20 → 1–3);
2. **API RAII 化**:绘制方法全部移入 `RenderPassScope` 作用域,`using` 关闭;pass 外无绘制由 guard 强制(运行期抛异常),`Begin`/`End` 裸配对从公开 API 移除;
3. **保持独立路径可用**:不走 graph 的调用方(离屏渲染、sandbox 直画、资源期渲染器、DebugStats)获得等价的 using 形态,语义逐字节保留(pass 关闭即提交);
4. **零逐帧分配**:作用域对象由所属 context 复用(与 `RenderGraphBuilder` 同契约),稳态不分配;
5. **双解决方案编译**(`Alco.slnx` + `Game.slnx`),全量测试绿,Sandbox 34 画面对比验收。

### 非目标(明确不做)

- listener 渲染器(`TextRenderer`/`InstanceRenderer`/`DynamicMeshRenderer`)的帧域 buffer 回收——当前没有任何 listener 渲染器挂在"每帧多 pass"的 context 上,该工作推迟到确有需求时(见 §10.2 契约);
- 多线程命令录制、多队列;
- 旧立即模式效果器(`Node/Bloom`/`Node/FXAA`)迁移;
- `AttachmentOps`(load/store op)接线——RHI 已支持,`BeginPass` 预留参数,节点侧使用留作后续。

## 3. 总体设计

### 3.1 `RenderPassScope`:唯一的绘制面(新增)

`Src/Alco.Rendering/Renderer/RenderPassScope.cs`,**sealed class**,实现 `IRenderContext` 与 `IDisposable`:

- 承载全部绘制 API:`Draw` / `DrawWithConstant` / `DrawInstanced` / `DrawInstancedWithConstant`、`ExecuteSubContext`、`SetScissorRect`、`SetStencilReference`、`ResolveTimestampsOnEnd`、`Framebuffer`、`AddListener`/`RemoveListener`(转发给所属 context);
- **内部两态**:直录 render pass(持有 `GPUCommandBuffer.RenderPass`)或 bundle 录制(持有 `GPURenderBundle`)——`RenderContext` 与 `SubRenderContext` 产出的作用域是同一个类型,消除现在两份 `Draw*` 重复代码;
- **复用而非新建**:每个 context 在构造期创建一个 scope 实例,`BeginPass` 时重绑 native pass 后返回同一对象。这是与 `RenderGraphBuilder` 相同的"单实例复用、回调外禁止持有"契约:仅在 `using` 块内有效,关闭后任何调用抛 `InvalidOperationException`;身份稳定,因此渲染器可以构造期持有它跨帧使用(见 §3.4);
- 关闭顺序:关闭 native pass → 记录 pending timestamp resolve → 交还所属 context 收尾(§3.2)。listener 通知在帧作用域边界触发(见 §3.2/§10.2,2026-08-13 修订);仅 `SubRenderContext` 保持录制域——scope 关闭、bundle 仍打开时触发 `OnCommandEnd`,listener 可向仍打开的 bundle 补充绘制。

不用 struct:实现 `IRenderContext` 传参会装箱且破坏内部 mesh 缓存;需要身份稳定供渲染器长期持有。

### 3.2 `RenderContext`:缓冲生命周期 + pass 工厂

`Src/Alco.Rendering/Renderer/RenderContext.cs` 重写,公开面:

- `BeginFrame()` → `RenderFrameScope`(§3.2.1):**唯一的提交者**,唯一打开缓冲的入口;
- `BeginPass(...)` 系列(plain / timestamp 重载,预留 `AttachmentOps` 参数)→ `RenderPassScope`;**要求帧内**——无活动帧作用域直接抛 `InvalidOperationException`,误用当场报错;
- `CommandBuffer`(既有,compute 节点经它在共享缓冲上开 `GPUCommandBuffer.ComputePass`;同样要求帧内);
- `Pass`(回收 scope 实例,供工厂/渲染器绑定)、listener 注册。

提交模型(2026-08-13 方案 B 定稿,取代最初设计的"即时/共享双模式"):

- 帧作用域打开即 `_command.Begin()`,关闭(Dispose)即 `_command.End()` + `ScheduleCommandBuffer`——**提交点只有一个,且由 `using` 保证异常安全**;pass 作用域永不提交;
- 总是提交:compute 直录在 `CommandBuffer` 上无法计入 pass 数,零 pass 空帧也提交,开销可忽略;
- 帧作用域关闭时仍有 pass 未关 → `Abort()` 丢弃整个缓冲不提交(半开 pass 无法合法收尾,保证下一帧从干净状态开始);
- 守卫:无帧不能开 pass;已有帧不能再 `BeginFrame`;两级作用域重复 `Dispose` 均抛异常;
- **listener 在帧域触发**:帧开 `OnCommandBegin`、帧提交/丢弃前 `OnCommandEnd`,每帧恰好一轮,与 pass 数量解耦(原设计随 pass 开关触发,在黑屏事故中证明会破坏共享 context,见 §10.3 末条)。

### 3.2.1 `RenderFrameScope`:唯一的提交面(方案 B 新增)

`Src/Alco.Rendering/Renderer/RenderFrameScope.cs`,**sealed class**,`IDisposable`:

- 持有所属 `RenderContext`;`Dispose` = 提交(或 pass 未关时丢弃),重复 `Dispose` 抛异常;
- `RenderGraph.Execute` 与独立/离屏路径走同一条公开路径,引擎不再保留 internal 提交入口(`Open`/`Submit`/`Abort` 全部私有化);双模式状态标记 `_autoOpenedBuffer` 随之删除;
- 跨方法持帧(如 `DebugStatsRenderer` 的 Begin/End 形态):class 字段持有,与 pass scope 同契约。

### 3.3 `SubRenderContext`:对称改造

- `BeginPass(GPUAttachmentLayout)` → bundle 态 `RenderPassScope`,`using` 结束即 `GPURenderBundle.End()`;
- 旧 `Begin`/`End` 移除;`HasBuffer`/`RenderBundle`/`Pass` 保留;
- bundle 态下 `ExecuteSubContext`(wgpu 不支持嵌套 bundle)与 `ResolveTimestampsOnEnd` 抛 `InvalidOperationException`;
- 每个 `SubRenderContext` 持独立 scope 实例 → 既有多线程并行录制形态(sandbox 15、Game `MapService_Shadow`)不受影响。

### 3.4 渲染器与工厂兼容

- `IRenderContext` **不变**,由 `RenderPassScope` 实现;
- `TextRenderer`/`InstanceRenderer`/`DynamicMeshRenderer`/`SpriteRenderer`/`TileRenderer` 构造期持有 `IRenderContext` 并跨帧使用 → scope 身份稳定使其无感,**零改动**;
- `RenderingSystem.Renderer.cs` 工厂增加 `RenderContext`/`SubRenderContext` 重载,内部转发 `context.Pass`,现有调用点(含 Game)**全部原样编译**;`IRenderContext` 重载保留;
- `ICommandListener` 挂载 list 仍在 context 上(scope 转发注册);触发时机 2026-08-13 起改为缓冲域(§3.2),`SubRenderContext` 保持录制域——`TextRenderer` 等"Begin 重置/End 归还"语义的渲染器在每提交周期恰好一轮通知下行为不变。

### 3.5 render graph 共享 context

- `RenderGraph` 构造期创建一个常驻 `RenderContext`(零逐帧分配);`Execute` 流程改为:Setup → Compile → Assign → `using (BeginFrame())` → 逐存活节点 `Execute` → 帧作用域关闭统一提交(节点带未关 pass 抛出时 Dispose 自动转为 Abort,见 §10.5)——graph 与独立路径走同一条公开 API;
- `RenderGraphContext` 新增 `RenderContext` 属性注入共享 context(与 `RenderGraphTexture.Texture` 同先例);`RenderGraph` 同时公开 `RenderContext` 属性,供节点外但与 graph 同帧的渲染(Game 世界、Canvas UI)共享同一缓冲(§10.3 末条);
- 节点删除自持 `_context` 字段,`Execute` 改为 `using (RenderPassScope pass = context.RenderContext.BeginPass(...))`;
- 内容接口签名变更:`IRenderPassContent.OnRender(RenderContext, ...)` → `OnRender(RenderPassScope, ...)`;`IShadowPassContent.OnRenderShadow` 同理;
- `SubRenderContext` 用途不变(bundle 录制与回放),`ExecuteSubContext` 移入 scope;
- **多窗口**:每视图独立 graph → 独立共享 context → 每视图一次提交,正确。

### 3.6 compute 收编

- `RGNode_SSR`(4 个 render pass)→ 共享 context 上 4 个顺序 scope,天然等价(录制序=执行序,wgpu 隐式同步);
- `RGNode_HBAO` / `RGNode_VoxelGI`:弃用自持 `_commandBuffer`,改经 `context.RenderContext.CommandBuffer` 开 `GPUCommandBuffer.ComputePass`(RHI 层本就是 using 作用域);pass 间 timestamp resolve 在共享缓冲内合法;
- VoxelGI 在两个 compute pass 之间上传 upsample uniform(`RGNode_VoxelGI.cs:1404-1410`):仅被后一 pass 使用,单缓冲内安全,但属 §10.1 模式,逐一注释确认;
- **uniform 契约升级为帧域**:单次提交意味着提交前任何 queue 写入对全部 pass 可见,"上传必须先于依赖它的 pass 录制、且不得重写已被本帧先前 pass 消费的 buffer"成为图级契约,写入 `RenderGraph` 与 `IRenderGraphNode` 文档。已审计:cascade 4 独立槽、push constants 录制快照、每帧一次上传(相机/lighting)、SSR/HBAO 先传后录,均安全。

### 3.7 timestamp 与 instrumentation

- `ResolveTimestampsOnEnd` 移入 scope,关 pass 后、缓冲结束前的窗口在多 pass 缓冲内依然合法;
- `PassInstrumentation.BeginPass(RenderContext, ...)` 改为返回 `RenderPassScope`;`EndPass` 更名 `ScheduleResolve(RenderPassScope)`(仅在 scope 内登记 pending resolve,关闭由 `using` 完成);
- `GpuTimestampSampler` 读回的是 ≥1 秒前的 resolve 数据,与帧内提交时机无关,不受影响。

## 4. 新 API 形态

独立/离屏路径(帧作用域包装,帧关闭即提交):

```csharp
using (RenderFrameScope frame = context.BeginFrame())
using (RenderPassScope pass = context.BeginPass(target, clearColors, clearDepth: 1.0f))
{
    pass.Draw(mesh, material);
    spriteRenderer.Draw(...);            // 渲染器构造期绑定的就是该 scope,照常
}   // 关 pass → timestamp resolve;帧作用域关闭 → 统一提交(唯一提交点)
```

graph 节点:

```csharp
public void Execute(in RenderGraphContext context)
{
    long startTicks = Instrumentation?.BeginCpuTiming() ?? 0;
    using (RenderPassScope pass = Instrumentation != null
        ? Instrumentation.BeginPass(context.RenderContext, _target.Texture.FrameBuffer, _clearColors, _clearDepth)
        : context.RenderContext.BeginPass(_target.Texture.FrameBuffer, _clearColors, _clearDepth))
    {
        List<IRenderPassContent> content = Content;
        for (int i = 0; i < content.Count; i++)
            if (content[i].IsEnabled) content[i].OnRender(pass, _target.Texture.AttachmentLayout);
        Instrumentation?.ScheduleResolve(pass);
    }
    Instrumentation?.PushCpuTiming(startTicks);
}
// RenderGraph.Execute 的帧作用域内录制,关闭时统一提交——一帧一次提交
// 节点内禁止再开帧(嵌套提交),内容必须画在共享 context 上(§10.3 末条)
```

bundle 录制(不变的使用场景,新形态):

```csharp
using (RenderPassScope pass = subContext.BeginPass(attachmentLayout))
{
    pass.DrawInstanced(mesh, material, count);
}
renderPass.ExecuteSubContext(subContext);   // 在直录 scope 内回放
```

## 5. 兼容与迁移策略

- **删除** `RenderContext.Begin/End/Draw*`、`SubRenderContext.Begin/End`——不做双轨,全量迁移调用点;
- 渲染器/`IRenderContext` 消费方零改动(§3.4);
- 内容接口签名变更波及:`GBufferRenderer`、`ShadowRenderer`、全部 `RGNode_*` 内容适配、Game `RGNode_World`/`RGNode_CanvasUI`(`RGNode_SceneContent.OnRender` 签名)、测试 fake;
- 独立路径(自持 context、离屏合成、sandbox 直画)迁移到"帧作用域 + pass"双层 `using` 形态,提交边界与原自动提交逐处对应(每处原自动提交点各包一个帧,不合并提交);
- `DebugStatsSystem` 的 pass 横跨 `OnUpdate`/`OnEndFrame` 两个方法,不用 `using`,字段持有帧+pass 两个 scope 手动开关(scope 均为 class,合法用法);
- ~~Game 的世界渲染主 context 本期不强制迁入共享 context~~ **该决策已被实测推翻(2026-08-13)**:节点内自持 context 的即时提交会插队到 graph 共享缓冲之前执行,而 `RGNode_Clear` 录在共享缓冲里帧末才执行 → clear 把世界与 Canvas 已画内容全部擦除(Game 黑屏,根因见 §10.3 末条)。修复:Game 世界渲染与 Canvas UI 均迁入 graph 共享 context(`Game.OnRenderWorld(RenderContext, ...)`、`CreateCanvas(..., renderContext)`),listener 触发改帧域,§10.2 契约随之重写。同日下午方案 B 定稿:自动提交整体移除,提交收敛为帧作用域唯一概念(§3.2);Sandbox 13/19/23 排查出与 Game 同型隐患(节点内自持 context,管线含 clear 节点即黑屏),一并迁共享 context。

## 6. 影响面清单

### 引擎(Alco)

| 文件 | 改动 |
|---|---|
| `Renderer/RenderPassScope.cs` | 新增(绘制面全部在此) |
| `Renderer/RenderContext.cs` | 重写:缓冲生命周期 + pass 工厂,删除 Draw*/Begin/End |
| `Renderer/SubRenderContext.cs` | 重写:BeginPass → bundle scope |
| `Renderer/IRenderContext.cs` | 不变 |
| `RenderingSystem.Renderer.cs` | 工厂重载(RenderContext/SubRenderContext → `.Pass`) |
| `RenderingSystem.cs` | `ScheduleCommandBuffer` 增加 internal 提交计数(测试钩子) |
| `Graph/RenderGraph.cs` | 持有共享 context,Execute 驱动 Open/Submit |
| `Graph/RenderGraphContext.cs` | 新增 `RenderContext` 属性 |
| `Graph/Nodes/IRenderPassContent.cs` / `IShadowPassContent.cs` | 签名 → `RenderPassScope` |
| `Graph/Nodes/PassInstrumentation.cs` | BeginPass 返回 scope;EndPass → ScheduleResolve |
| `Graph/Nodes/` ~15 个节点 | 删自持 context,改 using scope;SSR/HBAO/VoxelGI 收编 |
| `Deferred/GBufferRenderer.cs` / `ShadowRenderer.cs` | 内容接口签名 + bundle 录制改 scope |
| `Src/Alco.GUI/CanvasGUI/Canvas.cs` | 自持 context 改 using 形态;ctor 增加可选 `RenderContext` 注入(传入则不持有所有权),`RenderingSystemGUIExtension.CreateCanvas` 同步加重载 |
| `Src/Alco.Engine/.../DebugStatsRenderer.cs` / `DebugStatsSystem.cs` | 跨帧持有 scope |
| `Test/Alco.Rendering.Test` | fake 签名、extensibility 样例迁移;新增提交计数与守卫测试 |

### 父工程 Game(C:\Projects\Game)

- ~30 个 `OnRender(RenderContext)` 服务签名(含 IService/BaseGameService/BaseMapService/BaseOverworldService 及全部 override)→ `RenderPassScope`;16+ 处 `renderContext.Framebuffer` → scope 同名属性(机械);
- 自持 context:shadow/snapshot/weapon 3 个 + `MapSnapshotRenderer` 两阶段、`WeaponVisualCache` 合成 → using 形态(`WeaponVisualCache` 的 `Framebuffer != null` 异常兜底由 using 取代);**world context 已删除**——`Game.OnRenderWorld` 改收 graph 共享 context(黑屏修复,§5/§10.3 末条);
- ~15 处 `SubRenderContext` 录制类 → `BeginPass` scope;
- 8 处已标记时序假设(§10.3)全部位于独立/离屏路径,即时模式保留后天然安全。

### Sandbox

- B/C 类约 10 个项目直画路径 → 帧作用域包装;13/19/23 节点内自持 context → 迁共享 context(19 双管线各建一套渲染器);A 类(裸 GPUCommandBuffer)与 D 类(纯管线)不动。

## 7. 实施步骤

每步独立可验证:`dotnet build` 双解决方案 + 相关测试全绿再进下一步。

| # | 内容 | 验证 |
|---|---|---|
| 1 | `RenderPassScope` + `RenderContext`/`SubRenderContext` 重写 + 工厂重载;引擎内部消费方(GBufferRenderer/ShadowRenderer/Canvas/DebugStats/ImGUI 相邻代码)同步迁移 | `Alco.slnx` build;既有测试(迁移后)全绿 |
| 2 | graph 共享 context + 内容接口签名 + 全部节点迁移 + `PassInstrumentation` | build;Graph 测试全绿;新增提交计数测试(=1)与守卫测试 |
| 3 | compute 收编(SSR/HBAO/VoxelGI)+ uniform 帧域审计注释 | build;34 全特性画面对比 |
| 4 | Sandbox(B/C 类)与 Game 工程迁移 | `Game.slnx` build;Game 运行冒烟 |
| 5 | 验收:全量测试 + 稳态零分配复查 + submit 计数实测回填本文 | §9 清单 |

**执行结果(2026-08-13 回填)**:步骤 1–5 全部完成;同日完成黑屏热修复(§5/§10.3 末条)并随即按方案 B 定稿统一提交模型(§3.2/§3.2.1:帧作用域为唯一提交者,自动提交与 `_autoOpenedBuffer` 模式标记删除,`RenderGraph.Execute` 改走公开 `BeginFrame`,引擎/Game/Sandbox 全部录制点迁移)。`Alco.slnx` 与 `Game.slnx` 均 0 错误;`dotnet test Alco.slnx` 全量 843 项全部通过(`Alco.Profiler.BuildTool.Test` 在全量并行运行下偶有既有文件锁抖动,基线 HEAD 同样复现,单跑 100% 通过,与本重构无关,见 §9.4);headless 真机(Vulkan)截图验证主菜单与游戏内画面完整渲染(§9.4)。

## 8. 单元测试计划

位置:`Test/Alco.Rendering.Test`(NUnit,NoGPU 后端)。

**TestRenderPassScope**(新增):

1. 独立 context `BeginPass` → 关闭自动提交(提交计数 +1);
2. 同一 context 连续两个 pass:两次提交、缓冲正确开合;
3. 作用域关闭后再调用 `Draw`/`SetScissorRect` 抛 `InvalidOperationException`;
4. `BeginPass` 嵌套(上个 scope 未关)抛异常;`Dispose` 二次调用抛异常;
5. listener `OnCommandBegin`/`OnCommandEnd` 在 pass 开/关各触发一次,`OnCommandEnd` 内可向 scope 补充绘制;
6. bundle 态:`BeginPass`/`Draw`/`Dispose` 后 `HasBuffer` 为 true;bundle 态调 `ExecuteSubContext`/`ResolveTimestampsOnEnd` 抛异常;
7. timestamp 重载:`ResolveTimestampsOnEnd` 在关闭时记录 resolve(NoGPU 验证调用路径可达)。

**TestRenderGraph 扩充**:

8. graph Execute 全程提交数 == 1(经 `RenderingSystem` internal 计数,NoGPU);
9. 节点从 `context.RenderContext` 取得共享 context 并正常开关 pass;
10. 稳态 100 帧零分配复查(复用既有 `SteadyStateFramesDoNotAllocate` 框架)。

**既有测试迁移**:extensibility 样例、preset fake、graph 用例改新 API,断言不变。

**实际落地(回填)**:`Test/Alco.Rendering.Test/Renderer/TestRenderPassScope.cs` 新增 9 个测试,全绿——

- 计划 1/2 → 方案 B 定稿后为 `BeginPassWithoutFrameThrows`(无帧开 pass 即抛)/ `SequentialFramesSubmitOnceEach`(两个帧各提交一次);
- 计划 3/4 → `CallsOnClosedScopeThrow`(含 `Dispose` 二次)/ `NestedBeginPassThrows`;
- 计划 5 → `ListenersFireOncePerFrame`(listener 帧域,每帧一轮);
- 计划 6 → `SubRenderContextRecordsAndReplaysBundle` / `BundleScopeRejectsPassOnlyOperations`(NoGPU 的 `HasBuffer` 恒 true,录制前为 false 的断言不适用,已调整);
- 计划 8/9 → `GraphSubmitsOncePerFrame`(两 pass 节点共享 context,一帧提交数 == 1);
- 计划外追加 `GraphSubmitsEmptyFrameOnce`(§10.5:空帧仍提交一次);
- 计划 7(timestamp 重载)**未落地**:NoGPU 后端 `TimestampQuerySupported == false`,timestamp query set 无法在 NoGPU 下创建,该路径只能 GPU 侧验证;
- 计划 10 → 既有 `TestRenderGraph.SteadyStateFramesDoNotAllocate` 随 Alco.Rendering.Test 全绿通过;
- 黑屏修复追加(2026-08-13):`ListenersFireOncePerFrameOnSharedGraphContext`(graph 共享 context 上 listener 每帧恰好一轮)/ `SubRenderContextListenersFirePerRecording`(bundle 录制域每录制一轮);
- 方案 B 追加(2026-08-13):`FrameScopeSubmitsOnceForManyPasses` / `FrameScopeListenersFireOncePerFrame` / `FrameScopeWithOpenPassAbortsWithoutSubmit` / `FrameScopeNestingAndDoubleDisposeThrow`,Alco.Rendering.Test 总计 232 项全绿。

## 9. 验收方案

1. **画面回归**:Sandbox 34 全特性场景 ✅(用户实测正常);同机位逐像素对比**待 GPU 环境补充**;
2. **特性开关矩阵**:`ShadowEnabled`×`VolumetricLightEnabled`×`GiEnabled`×forward 玻璃,裁剪行为不变——**待 GPU 环境执行**;
3. **提交计数**:NoGPU 层已验证——graph 一帧恰好 1 次 `ScheduleCommandBuffer`(`GraphSubmitsOncePerFrame` / `GraphSubmitsEmptyFrameOnce`);真实 `wgpuQueueSubmit` 逐帧计数(15–20 → 预期 1,不含 VoxelGI 自留部分时 ≤3)**待 GPU 环境实测回填**;
4. `dotnet test` 全量绿 ✅(843 项全部通过;`Alco.Profiler.BuildTool.Test` 全量并行下偶发 `File.Move` `UnauthorizedAccessException`,基线 HEAD 3 跑 1 败同样复现——既有抖动与本重构无关,单跑 100% 通过);`Game.slnx` build ✅ 0 错误;**Game 运行冒烟 ✅(2026-08-13,headless + 真实 Vulkan):修复前主菜单截图纯黑(100,970 字节),修复后主菜单完整渲染(383,470 字节),脚本加载存档后游戏内画面完整(地形/建筑/角色/HUD,3,818,932 字节);方案 B 定稿后再次 headless 冒烟通过**;
5. resize 反复触发,池重建与材质重绑无异常——**待 GPU 环境执行**;
6. 稳态零分配测试绿 ✅(`TestRenderGraph.SteadyStateFramesDoNotAllocate`)。

## 10. 风险与陷阱

### 10.1 单提交下的 uniform 帧域污染

单次提交内,提交前任何 queue 写入(`UpdateBuffer`)对**全部** pass 可见——后写的值会漏进先录制的 pass。契约:"节点内所有 uniform 上传必须先于依赖它的 pass 录制;不得重写本帧先前 pass 已消费的 buffer"。已审计现行代码全部满足(§3.6);新增节点由 `IRenderGraphNode` 文档约束。

### 10.2 listener 渲染器的 buffer 复用契约(2026-08-13 重写)

`TextRenderer`/`InstanceRenderer`/`DynamicMeshRenderer` 假设"每轮命令周期 Begin 重置 buffer 轮换、End 归还 pool"。原设计 listener 随 **pass** 开关触发,共享 context 多 pass 下会每 pass 触发一轮 → 后一 pass 拿到同一批 pool buffer 并在提交前重写 → 先录制 pass 读脏数据;原对策是"约束挂 listener 的 context 每帧只开一个 pass"。

方案 B 定稿后(§3.2):**listener 通知收敛为帧作用域边界**——`BeginFrame` 触发 `OnCommandBegin`,帧作用域提交/丢弃前触发 `OnCommandEnd`,每帧恰好一轮,与 pass 数解耦;帧内开任意多个 pass 均安全,原"每帧一 pass"约束废除。`SubRenderContext` 保持**录制域**:每次 `BeginPass` 触发 Begin、scope 关闭(bundle 仍打开时)触发 End,listener 可向仍打开的 bundle 补充绘制——bundle 是一次性录制物,语义不变。

### 10.3 已标记的时序假设(逐条复核结论)

- **【根因记录,2026-08-13】嵌套即时提交 vs graph 缓冲提交的顺序反转**:旧模型每节点各自提交,执行序=录制序;新模型 graph 共享缓冲帧末统一提交,节点内嵌套的独立 context 即时提交会**插队到整个 graph 缓冲之前**执行。于是 `RenderPipeline` 的 `RGNode_Clear`(清 scene 纹理,录在共享缓冲)在帧末才执行,把 Game 世界(自持 context 中途提交)与 Canvas UI 已画内容全部擦除 → **Game 黑屏仅见 debug stats**(ImGUI 在 graph 提交后直接画到 FrameBuffer,故可见);Sandbox 34 无 clear 节点故未暴露。修复:Game 世界/Canvas 迁入共享 context(提交内录制序即执行序),listener 改帧域(§10.2)。教训:**graph 节点内不允许嵌套提交**,凡在节点执行期间画内容的代码必须使用共享 context——方案 B 后该规则由 API 结构部分兜底(`BeginFrame` 嵌套于同一 context 即抛;不同 context 的嵌套帧仍合法但受本文档契约约束)。Sandbox 13/19/23 排查出同型隐患(节点内自持 context + 管线含 clear 节点),一并迁共享 context;`MapService_Shadow` 是唯一的例外形态(节点内对自己的 shadow context 显式开帧提交,目标纹理独立于 graph 资源,队列序保持——见下条),属设计内;
- `MapSnapshotRenderer`/`WeaponVisualCache`/`ScreenshotCaptureSystem`/`TextureEncoderPNG`:独立 context 即时模式,`using` 关闭即提交,readback 顺序不变——安全(均在 graph 节点外执行,不构成上条嵌套形态);
- `MapService_Shadow`:shadow context 节点内显式开帧提交(目标纹理独立于 graph 资源)、主 context 后采样——不同提交,队列序保持——安全;
- Sandbox 23/24:裸 compute 先提交、管线后采样——路径不动——安全;
- Sandbox 34 截图:`Pipeline.Render` 返回即全部提交(graph 末尾统一 Submit)——安全;
- `DebugStatsSystem`:pass 跨帧循环,与 pipeline 提交交错——保持即时模式独立 context——安全;
- 19-MultiWindow:顺势改为每视图独立 context——需 Sandbox 改动验证。

### 10.4 复用 scope 的误用

scope 跨 `using` 块持有后使用 → guard 抛 `InvalidOperationException`(与 `RenderGraphBuilder` 同契约 + XML 文档显著标注)。

### 10.5 其他

- **空帧提交**:全部节点被裁或节点不录指令时,`Submit()` 仍提交一次(compute 直录无法计数,统一总是提交,空提交开销可忽略);
- **异常路径**:节点 `Execute` 抛异常时,异常传播经过帧作用域 `Dispose` 自动收尾——pass 已全部关闭则照常提交(已录制部分);节点带未关 pass 抛出则丢弃整个缓冲不提交(半开 pass 无法合法收尾,保证下一帧从干净状态开始);
- **NoGPU 对齐**:全部新路径经抽象层,NoGPU 测试保持绿;
- **多线程 bundle 录制**:每 `SubRenderContext` 独立 scope,不加锁(与现状一致,文档注明非线程安全)。

## 11. 总结

- 两级 RAII 作用域:**帧作用域(`RenderFrameScope`)= 唯一提交者**,**pass 作用域(`RenderPassScope`)= 唯一绘制面**——取代 `Begin`/`End` 裸配对,也取代中间形态"pass 关闭自动提交"的双模式;提交收敛为单一概念,误用(无帧开 pass、嵌套帧、重复 `Dispose`)全部当场抛异常;
- pass 作用域为所属 context 复用的 class(身份稳定、零分配),直录/bundle 两态合一,消除 `Draw*` 双份重复;
- `RenderContext` 瘦身为"帧工厂 + pass 工厂";`Open`/`Submit`/`Abort` 私有化,graph 与独立路径同走公开 `BeginFrame`;
- render graph 一帧共享一个 context、一次 submit;compute 插件收编后进同一缓冲;
- listener 通知为帧域(每帧一轮);**graph 节点内禁止嵌套提交**——Game 世界渲染与 Canvas UI 均使用共享 context(黑屏根因修复,§10.3 末条);
- 渲染器/`IRenderContext` 消费方零改动;内容接口与 ~15 个节点、Game、Sandbox 全量迁移;
- 分 5 步实施,每步 build + 测试可验证;画面以 Sandbox 34 与 Game headless 真机截图验收。
