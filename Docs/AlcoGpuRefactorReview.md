# alco_wgpu 重构审查报告

- 审查日期：2026-10-05
- 分支：`alco_wgpu`
- 基线提交：`5c7605b2`（GPU 重构前）
- 审查终点：`2cb9cb54c354035f3b36a508ae8b940ff2ca91a8`
- 性质：第 1–7 节保留原始只读审查结果；第 8 节记录后续工作区修复及验证。

> 第 1–7 节描述上述审查终点的历史状态，文件行号对应审查时版本。当前修复结果及未解决的额外验证问题以第 8 节为准。

## 1. 总体结论

**原始审查结论：审查终点版本不建议合并，发现 3 项 P1 阻塞问题和 7 项 P2 问题。**

已实际复现的关键回归包括：

1. 现有 PBR 和 SmokeTrail 示例在首帧完成命令编码时失败。
2. 未呈现的交换链纹理释放后，表面重新配置失败。
3. 每次队列提交遗留原生命令缓冲注册项，形成持续内存增长。
4. 句柄表批量释放后无法正确复用空槽。

构建及现有自动化测试全部通过，但没有覆盖上述场景，因此不能据此认定迁移完成。

优先级含义：

- **P1**：阻塞现有主要功能或已支持的部署路径，合并前必须处理。
- **P2**：需要处理的资源生命周期、兼容性、性能或错误恢复问题。

## 2. 审查范围

审查以下五个提交相对基线的 GPU 重构相关变化：

| 提交 | 内容 |
| --- | --- |
| `add03d94` | 引入 alco-gpu Rust ABI 和 Alco.Graphics C# 后端 |
| `ecb6c710` | 删除 wgpu-native 和原生 Vulkan 后端 |
| `b34ba7de` | 修正 surface texture 信息、增加 ABI 文档及验证 |
| `bd6200c4` | 增加八个 RID 的原生构建工作流 |
| `2cb9cb54` | 修正 manylinux 构建脚本的 crate 路径 |

覆盖内容：

- Rust ABI、枚举及结构体映射、句柄表、资源生命周期和错误处理。
- C# GPU 后端、命令编码、渲染管线、资源绑定、上传和读回。
- Surface、交换链、帧缓冲、引擎集成和跨平台发布。
- 新增及删除的测试，以及原 WebGPU/Vulkan 实现的行为差异。

主分支与当前分支间还有大量非 GPU 重构历史，本报告不将这些历史变化纳入问题归因。

审查过程中曾发现 `bd6200c4` 的 Linux 作业错误使用根目录下的 `alco-gpu` 路径；该问题已由 `2cb9cb54` 修正，**不计入本报告的未解决问题**。

## 3. 问题索引

| 编号 | 优先级 | 问题 | 证据 |
| --- | --- | --- | --- |
| GPU-01 | P1 | 无 stencil 的深度附件仍生成 stencil 操作，PBR 首帧失败 | 实际 Sandbox 及最小 ABI 复现 |
| GPU-02 | P1 | 未呈现的 surface texture 释放时没有 discard acquisition | Win32/Vulkan ABI 复现及基线对比 |
| GPU-03 | P1 | 七个 RID 缺失替代原生运行库 | 仓库产物、构建及加载路径确认 |
| GPU-04 | P2 | 队列提交后未释放 core command-buffer 注册项 | 依赖源码及内存对照实验 |
| GPU-05 | P2 | 句柄复用丢失空闲链剩余节点 | ABI 句柄索引复现 |
| GPU-06 | P2 | 编码失败后保留已消费 encoder，清理及恢复失败 | 源码及实际 Sandbox 清理错误 |
| GPU-07 | P2 | 适配器选择丢失 HighPerformance 策略 | 基线及依赖源码确认；硬件条件性问题 |
| GPU-08 | P2 | 拒绝原本合法的空绑定布局和空资源组 | ABI 复现及公共契约对比 |
| GPU-09 | P2 | 临时本机数组缺少异常安全释放 | 静态控制流确认 |
| GPU-10 | P2 | 错误序列化丢弃 source 链和实际根因 | 源码及实际错误日志 |

## 4. P1 问题

### GPU-01：深度格式没有 stencil，但仍生成 stencil 操作

**位置：**

- `Src/Alco.Graphics/AlcoGpu/Objects/AlcoGpuAttachmentLayout.cs:78-85`
- `Src/Alco.Graphics/AlcoGpu/AlcoGpuFrameBufferBase.cs:84-94`
- `Src/Alco.Graphics.Native/alco-gpu/src/commands.rs:416-428`

**原因：**

`IsStencilReadOnly` 仅取决于 `DepthAttachment.ReadOnly`，没有检查附件格式是否包含 stencil。因此，可写的 `Depth32Float` 附件也会生成 stencil `Load/Store`。Rust 将其转换为存在的 stencil 操作。

旧 wgpu-core 对缺失的 aspect 会忽略这些操作；新版 30 显式拒绝缺失 stencil aspect 上的操作，返回 `StencilOpsWithoutAspect`。迁移保留了旧序列化方式，却没有适配新的验证契约。

**实际影响：**

内置 PBR 管线的 G-buffer、shadow 和 RSM 使用可写 `Depth32Float`。实际运行以下示例均在首帧完成命令编码时失败，没有生成截图：

```text
34-PBRDeferred --procedural --frames=60 --screenshot=<absolute-path.png>
38-SmokeTrail --frames=150 --screenshot=<absolute-path.png>
```

错误经 ABI 传回时只显示 `In a pass parameter`，见 GPU-10。

**最小 ABI 复现：**

创建 Vulkan device 和 64×64 `Depth32Float` texture/view，记录不含 draw 的 depth-only pass：

| Stencil load/store | Begin pass | End pass | Finish encoder |
| --- | --- | --- | --- |
| Load / Store | OK | OK | Validation |
| 均为 `ALCO_NONE` | OK | OK | OK |

两组实验仅改变 stencil 操作，排除了 shader、timestamp 和 surface 的影响。

**修复方向：**

分别根据格式检查 depth/stencil aspect，缺失通道必须省略操作。不能把“格式不存在该通道”仅等同于用户声明的只读通道，因为显式操作检查也需要区分这两种情况。

**回归验证：**覆盖可写 `Depth32Float`、包含 stencil 的格式、只读 depth、显式 clear/load/store，并实际运行 PBR 和 SmokeTrail 截图场景。

### GPU-02：释放未呈现的交换链纹理，没有取消 surface acquisition

**位置：**

- `Src/Alco.Graphics.Native/alco-gpu/src/surface.rs:563-566`
- `Src/Alco.Graphics/AlcoGpu/AlcoGpuSurfaceFrameBuffer.cs:253-273`

**原因：**

`alco_texture_release` 移除 Alco 句柄后仅调用 `Global.texture_drop`。这会释放 core 纹理注册项，但 surface 仍持有 `presentation.acquired_texture`。

基线 wgpu-native 在 `WGPUTextureImpl::drop` 中，对尚未呈现的表面纹理先执行 `surface_texture_discard(surface_id)`，再执行 `texture_drop`。新后端遗漏了这一生命周期操作。

**触发场景：**

- VSync 切换到另一个受支持的 present mode。
- 窗口尺寸变化后，取得的纹理仍对应旧 surface configuration。

C# 在这些场景会执行“取得纹理 → Drop → Configure”。由于 acquisition 没有真正结束，重新配置失败，而不是跳过一帧后恢复。

**动态证据：**

使用临时 Win32 窗口及已提交的 Vulkan DLL：

- acquire → present → release：各调用均返回 OK。
- acquire → release（不 present）：均返回 OK。
- 随后 configure：返回 `Validation`，对应 `PreviousOutputExists`，消息为：

```text
The SurfaceOutput returned by get_current_texture must be dropped before re-configuring via configure or retrieving a new texture via get_current_texture.
```

**修复方向：**保存取得纹理的 parent surface 和呈现状态，或者提供显式 discard 操作；释放未呈现纹理时结束 acquisition，避免在已经 present 后重复 discard。

**回归验证：**覆盖 present 后 release、未 present 的 release、resize、VSync 切换和多窗口独立配置。

### GPU-03：七个 RID 没有替代原生运行库

**位置：**

- `Src/Alco.Graphics/runtimes/alco-gpu-manifest.json:26-32`
- `Src/Alco.Graphics/Alco.Graphics.csproj:16-20`
- `Src/Alco.Graphics/AlcoGpu/Interop/AlcoGpuNativeLibrary.cs:15-32`

**现状：**

原来的八个 RID 原生运行库被删除或替换后，当前仅提交 `win-x64/alco_gpu.dll`。以下 RID 的替代库缺失：

```text
win-arm64
linux-x64
linux-arm64
osx-x64
osx-arm64
android-x64
android-arm64
```

项目 Content 通配符会静默匹配不到文件，普通 `dotnet build` 不会构建 Rust 或下载替代产物。新鲜 checkout 中，这些目标的第一次 GPU native 调用没有对应库可加载，将遇到 `DllNotFoundException`。

**为什么是回归：**

基线提交包含上述目标的运行库；Linux/macOS 仍处于常规 CI 矩阵中，项目也继续声明跨平台支持。新增加的手动原生 CI 是必要基础，但不是已提交的发布产物，也没有接入普通 build/test 流程。

**证据限制：**已确认仓库缺失产物及构建、加载路径；本机没有实际运行这些操作系统和架构。

**修复方向：**在删除旧部署路径的同时交付替代二进制，或接入自动原生构建和打包；原生库缺失时应在构建阶段明确失败，而不是等到运行期加载失败。

**回归验证：**各 RID 的干净 checkout 构建、原生库打包与加载、ABI handshake，以及可用 GPU 环境上的最小渲染验证。

## 5. P2 问题

### GPU-04：每次队列提交遗留 core command-buffer 注册项

**位置：**`Src/Alco.Graphics.Native/alco-gpu/src/commands.rs:1435-1455`

**原因：**

`alco_queue_submit` 移除了 Alco `CommandBufferObj`，随后调用 `Global.queue_submit`，但未调用 `Global.command_buffer_drop`。

wgpu-core 的提交操作只读取注册对象并转移命令内容；core 注册项的移除需要显式 `command_buffer_drop`。当前代码丢失了 core ID，导致其 CPU 包装对象和注册槽位留存至 device teardown。基线在提交后执行 `wgpuCommandBufferRelease`。

正常 `Submit`、同步读回和异步读回都经过该路径。

**内存对照实验：**

已提交 DLL、预热后运行空命令缓冲，每 4,000 次操作等待 GPU 空闲：

| 操作次数 | Finish + destroy 增长 | Finish + submit 增长 |
| --- | --- | --- |
| 4,000 | 0 MiB | 4.457 MiB |
| 8,000 | 0 MiB | 8.770 MiB |
| 12,000 | 0 MiB | 12.707 MiB |
| 16,000 | 0 MiB | 17.152 MiB |

约每次提交累积 1.1 KiB。对照路径使用相同的 Alco 句柄表，有助于区分 GPU-05 的空闲链问题。

这里确认的是 CPU 注册对象泄漏，**不是声称全部已提交 GPU 资源都被保留**。

**修复方向：**保留提交索引与错误结果，在成功和失败路径均释放 core command-buffer ID。

**回归验证：**长时间提交并等待 idle 后检查对象计数或内存平台期，加入成功、失败和读回提交路径。

### GPU-05：句柄表插入时丢失剩余空闲链

**位置：**`Src/Alco.Graphics.Native/alco-gpu/src/handle.rs:46-52`

`insert()` 调用 `free.take()` 后只读取 vacant 槽位的 generation，没有将 `free_head` 推进到 `Slot::Vacant.next`。随后覆盖该槽位，使剩余空闲槽永久不可达。

**动态复现：**

```text
创建六个 buffer 的索引：      [0, 1, 2, 3, 4, 5]
全部销毁后再创建六个的索引：  [5, 6, 7, 8, 9, 10]
```

批量资源销毁后只能复用最后释放的一个槽位。反复批量创建和销毁时，即使存活对象数量有界，内部 `slots` vector 仍持续扩张。

**修复方向：**覆盖 vacant 槽前保存并恢复其 `next` 链头。

**回归验证：**批量释放、不同释放顺序、连续多轮创建销毁，同时确认 stale handle 的 generation 检查仍然有效。现有单槽复用测试无法发现此问题。

### GPU-06：编码失败后保留已消费 encoder，清理及恢复失败

**位置：**

- `Src/Alco.Graphics/AlcoGpu/AlcoGpuCommandBuffer.cs:76-78`
- `Src/Alco.Graphics/AlcoGpu/AlcoGpuCommandBuffer.cs:575-582`
- `Src/Alco.Graphics.Native/alco-gpu/src/commands.rs:233-248`
- `Src/Alco.Graphics/Abstraction/GPUCommandBuffer.cs:308-312`

Rust `EncoderFinish` 无论成功还是失败都会消费 encoder 句柄并释放 core encoder。C# 却先调用 `ThrowIfFailed`，再将 `_encoder` 清空。

发生验证错误时：

1. `_encoder` 保留已经无效的句柄。
2. 后续清理调用 `EncoderDestroy`，再次抛出 `InvalidHandle`。
3. `Dispose` 中位于其后的本机名称和附件缓存释放被跳过。
4. 公共 `GPUCommandBuffer.End()` 的录制状态也因异常未被清除，阻碍复用。

**实际证据：**PBR 和 SmokeTrail 的原始 Validation 后，均出现 `invalid encoder handle` 清理错误。失败后的公共状态及清理中断由源码控制流确认。

**修复方向：**按“调用后句柄已消费”的 ABI 契约先更新句柄和状态，再传播错误；异常路径仍须完成状态恢复和资源清理。相同的 consuming-handle 模式也应检查 pass/bundle 结束操作。

**回归验证：**故意产生编码验证错误，捕获后检查状态、清理、再次记录，以及 fresh command buffer 的正常使用。

### GPU-07：适配器选择丢失原有 HighPerformance 策略

**位置：**`Src/Alco.Graphics.Native/alco-gpu/src/device.rs:186-195`

基线明确请求 `WGPUPowerPreference.HighPerformance`；新实现直接选取 `enumerate_adapters().first()`。

Vulkan HAL 保留物理设备枚举顺序，DX12 使用 `EnumAdapters1` 的顺序；这些顺序不保证独显优先。wgpu-core 的高性能 request-adapter 路径则会优先选择 discrete GPU。

**触发条件：**Vulkan/DX12 混合显卡机器先枚举集显。

**影响：**选择较慢的集显；还可能在独显满足所需能力时，因首个集显不满足必要特性而拒绝启动。

**证据限制：**基线与依赖源码已确认；本机未在多 GPU 硬件上复现。Metal HAL 已自行按功耗偏好排序，不包含在本项条件中。

**修复方向：**恢复高性能选择策略，并在候选适配器之间检查必要能力。

**回归验证：**混合显卡环境下的 Auto、显式 Vulkan/DX12，以及首个候选能力不足的选择行为。

### GPU-08：合法的空绑定布局和空资源组被拒绝

**位置：**

- `Src/Alco.Graphics.Native/alco-gpu/src/objects.rs:1167-1172`
- `Src/Alco.Graphics.Native/alco-gpu/src/objects.rs:1245-1250`

两个创建函数都要求 entries 指针非空且 `entry_count > 0`。但空 bind-group layout 及与它匹配的空 bind group 是合法对象，公共描述符没有非空约束；基线允许零 entries。

**动态证据：**count 为零时，无论传 null entries 还是有效占位指针，都返回 status 2：

```text
null descriptor, out pointer or empty entries
```

因此，`GPUDevice.CreateBindGroup(new BindGroupDescriptor([]))` 不再保持旧后端行为。

**范围说明：**未发现当前生产 Slang 路径生成稀疏空分组；本项基于合法公共图形契约，不宣称已有该生产调用链。

**修复方向：**允许 count 为零；只有正 count 才要求非空指针；零 count 时安全构造空 slice，不能对 null 指针直接使用 `from_raw_parts`。

**回归验证：**空布局及匹配的空资源组、空 entries 指针与有效零长度指针，以及非空无效指针的拒绝行为。

### GPU-09：临时本机数组缺少异常安全释放

**位置：**

- `Src/Alco.Graphics/AlcoGpu/Objects/AlcoGpuBindGroup.cs:51-67`
- `Src/Alco.Graphics/AlcoGpu/Objects/AlcoGpuGraphicsPipeline.cs:70-170`
- `Src/Alco.Graphics/Common/InteropUtility.cs:8-15`

布局 entries 和管线 vertex elements 从基线的 `stackalloc` 改成了 `Marshal.AllocHGlobal` 分配，但 `Free` 只在成功路径执行。

绑定布局或图形管线创建返回可捕获验证错误时，`ThrowIfFailed` 跳过这些释放。管线已有的 `finally` 只释放 shader modules，不释放 vertex elements。本机数组没有被保存到失败对象中，后续 finalizer 也无法回收。

**证据等级：**静态控制流确认，未单独测量累计泄漏量。

**修复方向：**将每个临时本机数组纳入 `try/finally`，明确失败时的释放责任。

**回归验证：**重复创建无效布局或管线，捕获异常，确认本机分配和释放保持平衡。

### GPU-10：错误序列化丢弃 source 链和根因

**位置：**`Src/Alco.Graphics.Native/alco-gpu/src/entry.rs:46-48`

`set_error_from` 只接受顶层 `Display` 并保存 `error.to_string()`，没有遍历错误的 source 链。

wgpu-core 的多层验证错误常用顶层消息描述发生错误的命令作用域，实际原因保存在 source 中。当前实现使不同根因变成相同的外层文本。

**实际证据：**GPU-01 的真实 PBR 和最小 ABI 错误均只显示：

```text
In a pass parameter
```

消息未包含 `StencilOpsWithoutAspect`、附件格式及错误操作，必须额外检查依赖源码和最小实验才能定位。

**修复方向：**序列化完整错误链，保留操作上下文和最终验证原因。

**回归验证：**对至少两种不同的 pass/pipeline 验证错误，确认传回 C# 的消息可区分根因并包含关键资源或格式信息。

## 6. 验证结果和限制

本机环境：Windows x64、.NET SDK 10.0.401、Rust 1.97.1、NVIDIA GeForce RTX 4070 Ti。

| 检查 | 结果 |
| --- | --- |
| `dotnet build --no-restore --nologo` | 通过，0 警告、0 错误 |
| `dotnet build --configuration Release --no-restore --nologo` | 通过，1 个 CS0108 成员隐藏警告 |
| Debug 全量 `dotnet test --no-build --no-restore` | 1,371 项通过 |
| Release 全量 `dotnet test --configuration Release --no-build --no-restore` | 1,372 项通过 |
| Alco.Graphics.Test 单独执行 | 37 项通过 |
| `cargo test --release --locked` | 3 项句柄测试和 1 项 adapter diagnostic 通过 |
| `cargo clippy --release --locked --all-targets -- -D warnings` | 未通过：6 项惯用法 lint |
| 已提交 win-x64 DLL 的 SHA-256 | 与 manifest 一致 |
| Sandbox 36 GPU Particles 2D | 成功生成截图，已查看图片 |
| Sandbox 34 PBR Deferred，procedural 场景 | 首帧失败，没有生成截图 |
| Sandbox 38 SmokeTrail | 首帧失败，没有生成截图 |

Release 的成员隐藏警告位于 `AlcoGpuGraphicsPipeline.Stages`。Clippy 报告涉及可派生的 Default 实现、可使用 `?` 的 match、`then_some` 和无效果的按位操作；这些不等同于本文的运行期问题。

Sandbox 34/38 的进程退出码仍为 0，因为示例启用了 `StopWhenError` 并自行停止。验证这些场景必须同时检查日志和截图是否实际生成，不能仅看退出码。

以下内容未在本机动态验证：

- Linux、macOS、ARM64、Android 的运行及原生 CI 执行。
- Metal 和实际 DXIL 渲染。
- 混合显卡机器上的适配器选择。
- 临时数组异常路径的累计内存增长量。

现有测试未充分覆盖：

- 可写 depth-only 附件在新版 core 下的通道合法性。
- surface discard 后的 resize/VSync 重新配置。
- 长时间提交后的原生对象及内存平台期。
- 批量句柄释放后的完整空闲链复用。
- 编码失败后的状态恢复和异常安全清理。
- 非 win-x64 RID 的原生库交付与加载。

## 7. 建议的处理顺序

1. 修复 GPU-01 和 GPU-02，恢复现有 3D 场景及交换链配置变化。
2. 修复 GPU-04 和 GPU-05，消除正常运行中的持续内存和句柄表增长。
3. 完成 GPU-03 的跨平台原生产物交付和构建期缺失检查。
4. 修复 consuming-handle 错误恢复及临时分配清理（GPU-06、GPU-09）。
5. 恢复适配器和空绑定契约（GPU-07、GPU-08），补全错误链（GPU-10）。
6. 将本报告的复现场景加入回归测试，重新执行 Debug/Release 全量测试和实际 Sandbox 截图验证。

最终判断依据应同时包含：构建通过、自动化测试通过、现有示例实际可运行、资源生命周期稳定，以及目标平台产物可交付。

## 8. 工作区修复与验收记录

修复及验证日期：2026-10-05。第 8.1–8.7 节记录 `2cb9cb54` 之后、提交前的修复验证快照，不改变第 1–7 节的历史审查结论。随后已按用户要求提交并推送 `fed0ba76`，触发八 RID 原生 CI 构建；最新交付状态见第 8.8 节。

**当前结论：原报告 GPU-01 至 GPU-10 均已落实源码修复并补充对应回归验证；但整体分支验收尚未通过。** 新增的 DX12 实际执行测试仍有 6 项失败，额外检查的两个 Sandbox 也未通过，SmokeTrail 退出后仍有资源 finalizer 生命周期错误。因此不能将本次结果描述为“全量测试通过”或“分支已可合并”。

### 8.1 十项问题的修复及证据

| 编号 | 已实施的修复 | 当前验证证据与边界 |
| --- | --- | --- |
| GPU-01 | 附件元数据独立记录 `HasDepth` / `HasStencil`；不存在的通道省略 load/store，显式 clear 和共享 depthOps 只作用于存在的通道。保留真正只读通道的修改检查，并对齐 bundle 的只读兼容性。 | 新增 depth/stencil 元数据、clear、load/store、只读、实际 draw 和 bundle 回归通过。PBR 34 与 SmokeTrail 38 已实际生成截图，不再发生原来的首帧附件验证失败。 |
| GPU-02 | surface 记录当前 acquired texture；surface texture 记录 parent surface。释放匹配的未呈现纹理时先 discard；成功 present 后清除跟踪，旧的已呈现句柄不能 discard 新帧。错误类型验证在移除句柄之前执行。 | 5 个真实 Win32/Vulkan 原生生命周期测试通过，覆盖呈现/丢弃、重配置、旧句柄交错、错误恢复及双窗口。托管 Vulkan swapchain 回归完成 24 轮 resize/VSync/未呈现 acquisition 恢复。实际 Sandbox 19 同时打开两个窗口并正常退出。DX12 对应测试仍因独立的 device 创建错误未能进入该场景。 |
| GPU-03 | 普通 build、publish、pack 自动确保所选 RID 原生库存在且与源码匹配；缺失或过期时以 `Cargo --locked --release` 构建到 `obj`，工具链/SDK 不足时在构建期明确失败。当前交付物经源码及二进制双指纹校验后可免 Rust 使用。 | 8 个 RID 映射已验证；Windows 构建、publish、pack 和 PackageReference 消费通过。Linux source-only 副本自动生成 ELF、加载并返回 ABI 1.2，publish/pack 内容一致。缺失/过期/保留时间戳的变化均被识别。ARM64 实际编译进入链接，但因本机缺少 ARM64 SDK/MSVC 库失败并返回 `ALCOGPU004`。**不是八个平台全部构建或渲染通过。** |
| GPU-04 | `queue_submit` 保存提交结果后，无论成功还是失败都调用 `Global.command_buffer_drop`，再返回原提交索引或错误。 | 原生测试精确检查 core registry，覆盖 1,024 次成功提交、失败提交、读回数据正确性。额外 64,000 次有界 in-flight 实验在初始高水位增长后稳定；详见 8.4，不声称任意队列深度下进程内存完全不增长。 |
| GPU-05 | 覆盖 vacant 槽位前读取 generation 和 next，并恢复剩余 freelist。 | Rust 覆盖批量、部分释放、多轮复用及 stale generation；托管 raw ABI 测试以 64 个 buffer × 32 轮确认槽位有界复用与旧句柄失效。 |
| GPU-06 | encoder/pass/bundle 结束调用之后先清空已消费句柄，再传播错误；用 `finally` 恢复录制状态。Dispose 尝试全部清理并保留首个异常；读回辅助路径避免重复销毁已消费 command buffer。另补 device 构造失败的回滚及 Host 事件解绑，避免掩盖原始错误。 | 故意失败后的 End、隐式 pass 结束、Dispose、重新录制和 fresh device 回归通过；不再出现原 GPU-06 的 `invalid encoder handle` 清理失败。SmokeTrail 的**设备销毁后晚到 finalizer**属于另外的未修复生命周期问题，见 8.6。 |
| GPU-07 | 恢复 core `HighPerformance` request-adapter；若首选不满足必要 feature/immediate limit，按高性能优先级稳定选择满足能力的候选。可选特性仍按支持情况启用。 | 选择策略及候选能力过滤的单元测试通过。实际 Vulkan 使用 RTX 4070 Ti；没有混合显卡硬件验证，不将策略测试等同于双 GPU 实测。 |
| GPU-08 | 零 entry 使用安全空 slice，允许 null 或有效占位指针；正 count 仍必须有非空 entries。 | raw ABI 两种零 entry 指针及正 count/null 拒绝测试通过；托管空绑定布局和空资源组创建通过。wrong-kind texture destroy/release 不再提前移除原句柄。 |
| GPU-09 | bind-group entries、graphics vertex elements 置于 `try/finally`；fragment shader 创建也移入已有清理范围，失败时不遗留已创建 vertex shader。 | DEBUG 内部计数器检查 128 次重复无效布局及 64 次无效管线后的临时本机分配平衡。Release 仅跳过这两项计数器专用测试，其他生命周期回归仍执行；未引入 Alco.dll 依赖或 Release 跟踪开销。 |
| GPU-10 | `set_error_from` 接受 `std::error::Error` 并序列化完整 `source()` 链，保留上下文与最终根因。 | 两种真实 Depth32Float pass 验证错误在 ABI 返回中均保留 `In a pass parameter` 上下文和不同根因；另有合成错误链测试。清理前先封送原消息，避免 native TLS 后续调用覆盖。 |

主要回归文件：

- `Test/Alco.Graphics.Test/AlcoGpuRegressionTests.cs`：22 个新增托管回归用例，Debug 全部通过。
- `Test/Alco.Graphics.Test/AlcoGpuAbiTests.cs`：句柄批量复用、空绑定和真实错误链。
- `Test/Alco.Graphics.Test/AlcoGpuIntegrationTests.cs`：真实像素读回及 swapchain 配置变化，保留显式 DX12 失败用例。
- `Src/Alco.Graphics.Native/alco-gpu/tests/surface_lifecycle.rs`：5 个真实窗口/surface 生命周期测试。
- `Src/Alco.Graphics.Native/alco-gpu/src/commands.rs`、`handle.rs`、`device.rs`、`objects.rs`、`entry.rs`：相应原生回归。

### 8.2 当前构建及全量测试结果

验证环境沿用 Windows x64、.NET SDK 10.0.401、Rust 1.97.1、RTX 4070 Ti。GPU 回归执行时已核对消费端 DLL 与交付 DLL 的 SHA-256 一致，避免以旧输出目录的 DLL 代替当前修复产物。

| 检查 | 当前结果 |
| --- | --- |
| 最终 `dotnet build --no-restore --nologo` | 通过，0 警告、0 错误 |
| `dotnet build --configuration Release --no-restore --nologo` | 通过，0 警告、0 错误；原 `Stages` 的 CS0108 已消除 |
| `cargo test --locked` | 22 项通过：16 unit、1 adapter diagnostic、5 surface integration |
| `cargo test --release --locked` | 同上 22 项通过；Vulkan/窗口测试在本机实际执行 |
| `cargo clippy --release --locked --all-targets -- -D warnings` | 通过，没有通过抑制 lint 掩盖原问题 |
| Debug 首次全量测试 | 1,407 总计：1,399 通过、7 失败、1 跳过 |
| **最终 Debug 全量串行项目重跑**：`dotnet test --no-build --no-restore --nologo -m:1` | **1,407 总计：1,400 通过、6 失败、1 跳过** |
| Release 全量测试 | **1,408 总计：1,399 通过、6 失败、3 跳过** |
| 最终 Debug `Alco.Graphics.Test` | 68 总计：66 通过、2 个 DX12 失败 |
| Release `Alco.Graphics.Test` | 68 总计：64 通过、2 个 DX12 失败、2 个 DEBUG 计数器专用测试跳过 |
| 最终 Debug / Release `Alco.Rendering.Test` | 各 343 总计：339 通过、4 个 DX12 失败 |
| `Alco.Effects.Test` | 105 项通过 |
| 最后两处 XML 注释补齐后的托管修复回归重跑 | Debug 22 通过；Release 20 通过、2 个 DEBUG 计数器专用测试跳过；两种配置重新 build 均为 0 警告、0 错误 |
| 差异格式及原生产物指纹 | `git diff --check` 通过，源码/DLL/sidecar/manifest 一致；修改后的项目 XML 与两份 workflow YAML 可解析 |

Debug 和 Release 的 6 个剩余失败相同：

1. `RenderQuadAndReadbackMatchesExpectedPixels(WGPUDx12)`。
2. `SwapchainResizeAndVSyncRecoverUnpresentedAcquisitions(WGPUDx12)`。
3. `GpuCompression_UsesEndpointFirstPalette(BC1,False,WGPUDx12)`。
4. `GpuCompression_UsesEndpointFirstPalette(BC1,True,WGPUDx12)`。
5. `GpuCompression_UsesEndpointFirstPalette(BC3,False,WGPUDx12)`。
6. `GpuCompression_UsesEndpointFirstPalette(BC3,True,WGPUDx12)`。

Debug 首次并行全量运行还出现 `AssetLoaderFontTTF_SecondLoad_HitsCacheAndMatchesGlyphs` 的异步缓存写入等待超时。该测试单独重跑、Release 全量及最终 Debug 串行全量均通过；字体代码未修改。保留首轮失败记录，不以重跑抹去该不稳定性。

既有 `IntAxis_TernaryTrueCondition_CrashesSlang2616` 在两种配置中跳过。Release 另跳过上述两项 DEBUG allocation-counter 专用测试；没有跳过 DX12 用例来获取绿色结果。没有修改 shader 文件。

### 8.3 原生交付、构建及平台边界

当前实际重建并放回仓库交付路径的是 `win-x64/alco_gpu.dll`：

```text
ABI version:       1.2 (0x10002)
wgpu-core:         30.0.1
DLL size:          8,780,800 bytes
DLL SHA-256:       b0fcd7425bb18f5e3fdcae40c068f5cd719879aee569a2308ae2fcc9d48c5e1f
Source SHA-256:    e865152f2262eb0e710e3c1852d71527e55b8689198fd8f479f4b55f561748f9
```

DLL 的两行 `.source.sha256` 与 manifest 的 win-x64 条目匹配。Debug、Release 的 Graphics.Test 及所验证 Sandbox 输出均使用该 DLL。重建前收到的 win-x64 二进制已备份，没有直接丢弃。

修复过程中工作区出现的另外七个 RID 原生产物来自修复前的历史 CI 交付；它们被保留，**没有为旧二进制写入当前源码 stamp**，也不描述成已含本次修复。新的构建逻辑会因缺失/不匹配 stamp 选择源码构建，而不是静默打包旧产物。

构建/打包契约及实测：

- `Alco.Graphics.csproj` 为 8 个 RID 提供明确 Rust triple 映射，自动生成所选 RID 原生库到 `obj/alco-gpu/<triple>/release`。
- 交付与生成库均校验归一化源码指纹和二进制指纹；修改源码但保留 mtime、删除源码、修改二进制或删除库不能绕过 freshness 检查。
- 当前交付物可用时，以不存在的 Cargo 路径构建仍成功；缺失或过期而 Cargo 不可用时明确返回 `ALCOGPU003`，不是运行期 `DllNotFoundException`。
- publish/pack 包括生成后的原生 Content；删除 `.so` 后执行 `--no-build` 的 publish/pack 仍重新生成必要库。
- **pack 只包含所选 RID**，不是一次 pack 聚合八个平台。NuGet 消费者只接收普通 runtime native asset，不携带 Rust 源码构建 targets。
- Windows 本地 PackageReference 消费者无需 Cargo 即可得到当前 native DLL；publish 使用可加载的平面文件名。
- `RUSTUP_AUTO_INSTALL=0` 阻止普通构建隐式安装工具链；缺少 Cargo、目标 linker 或 SDK 时给出 `ALCOGPU001`–`005` 诊断。
- 常规 CI 已接入所选 host RID 源码构建与全量测试；手动八 RID workflow 已补指纹交付。**本次没有触发这些 CI，不声称远端矩阵已经通过。**

Linux 独立验证使用 WSL 内隔离的官方 Rust/.NET 工具链和 source-only 副本，不依赖仓库中的历史 `.so`：

```text
Architecture:      ELF64 x86-64
ABI handshake:     0x10002
Library size:      9,626,112 bytes
Library SHA-256:   afab8d27d981eb116a1e1f5de04dc01bbdf8084768ade16cb9fece8e17281450
Source SHA-256:    e865152f2262eb0e710e3c1852d71527e55b8689198fd8f479f4b55f561748f9
```

自动生成、managed build、native load、publish、NuGet 包内容及 freshness 矩阵通过；生成/output/publish/package 内 `.so` 字节一致。该 Linux 产物保存在本地验证目录，没有覆盖工作区中的历史交付 `.so`。WSL 环境缺少 `libvulkan.so`，**未验证 Linux GPU 渲染**；宿主 glibc 构建也不是 manylinux 2.28 发布验证。

win-arm64 实际尝试进入 MSVC 链接后因 ARM64 库缺失而失败，例如 `LNK1181: opengl32.lib`；构建期诊断按预期出现。linux-arm64、macOS、Android 没有在本机完成当前修复产物的构建/加载/GPU 验证，Metal 及混合显卡选择也未实测。

### 8.4 提交注册项及内存证据

GPU-04 的主要验收依据是 core 注册项精确计数，以及成功/失败/读回路径测试；进程 PrivateUsage 会同时受驱动、allocator 和队列峰值影响。

补充实验加载当前 `b0fcd742…` DLL，预热并在采样前等待 idle/GC：

- 与原报告相同的每 4,000 次提交等待 idle 场景，16,000 次内仍有约 **19.535 MiB 的阶梯式增长**，而 finish+destroy 对照维持不变。不能把它写成“修复后所有提交内存完全持平”。
- 将 in-flight 数量控制到 32、预热 4,000 次、再提交 64,000 次后：4,000 次时 PrivateUsage 为 314,036,224 bytes，16,000 次为 314,155,008 bytes，64,000 次为 314,167,296 bytes。4,000–64,000 样本的范围仅约 **0.156 MiB**，在初始高水位增长后保持稳定。

这些结果支持原 core command-buffer 注册泄漏已被精确计数测试排除及有界队列内存稳定，但不排除引擎其他生命周期或驱动缓存问题。

### 8.5 实际 Sandbox 验证

最终截图运行在 native DLL 已传播到示例输出之后执行，并核对 DLL hash；更早使用旧 DLL 的失败运行不计作本次修复后的验收证据。

| 示例及场景 | 结果 |
| --- | --- |
| `34-PBRDeferred --procedural --frames=60 --screenshot=<absolute>` | 退出 0；实际生成 804,125-byte PNG，已查看 procedural PBR 场景 |
| `36-GpuParticles2D --frames=90 --screenshot=<absolute>` | 退出 0；实际生成 140,874-byte PNG，已查看粒子及 GUI |
| `38-SmokeTrail --frames=150 --screenshot=<absolute>` | 退出 0；实际生成 619,491-byte PNG，已查看场景/trail/effect；**关闭后存在额外 finalizer 错误，见 8.6** |
| `0-BasicWindow` | 1 个可见自有 SDL 窗口，实际持续运行后 Escape 正常退出，无渲染验证错误 |
| `1-DrawQuad` | 同上，实际绘制窗口正常运行和退出 |
| `19-MultiWindow` | 同一个进程下 **2 个同时可见 SDL 窗口**，持续约 7.547 秒后 Escape 正常退出；无渲染验证错误 |
| `4-ComputeShader` | **失败**：构造时请求 `MainCS`，实际 shader 入口为 `mainCS`；未到达 compute dispatch |
| `6-PushConstants` | **失败**：draw 使用的绑定组缺少 pipeline 要求的 sampler binding 1；`StopWhenError` 导致退出码 0，不代表成功 |

窗口操作只针对本次启动的 PID；没有操作其他应用、截图桌面或强制终止其他进程。所有五个窗口验证进程都已退出，没有使用 watchdog kill。

### 8.6 新暴露但未修复的独立问题

以下不属于原 GPU-01–10 的同一根因，保留为分支合并前的额外阻塞/风险，没有通过删除测试或关闭验证掩盖。

#### A. DX12 默认 device 创建：内部 indirect validation 初始化失败

两个显式 DX12 图形/swapchain 用例返回：

```text
[alco-gpu:unsupported] status: Parent device is lost
```

当前 DLL 的独立 raw ABI 探针以 backend `3`、immediate limit `128` 测得：debug=1 时 feature masks 23 和 15 均创建成功；debug=0 时均失败。debug=0 的 masks 0、7、16 也失败，不能归因于单个可选 feature。

源码证据：`src/device.rs` 的 debug=true 使用 `InstanceFlags::DEBUG`，false 使用 `InstanceFlags::default()`。wgpu-types 30 的 Release default 含 `VALIDATION_INDIRECT_CALL`；wgpu-core 30 在该标志下初始化内部 indirect-validation 管线，而 `indirect_validation/mod.rs` 会将**任意初始化失败**映射为 `DeviceError::Lost`，原错误另行记录到 log。

因此目前证据定位到内部验证初始化，**没有证明硬件实际掉线**。原始内部失败信息仍未恢复。强制 debug=true 或禁用 indirect validation 会绕开问题并改变验证语义，不是被实施的修复。

#### B. DX12 Slang DXIL compute：passthrough 绑定契约不匹配的强嫌疑

四个实际 BC compute 测试在 `CreateComputePipelineState` 返回 `E_INVALIDARG (0x80070057)`；相同 Auto/Vulkan 用例通过。

传递路径核对表明：Slang 每个 entry point 的独立 code、target entry name、workgroup size 被转交到 native；DXIL 分支使用原始 blob，并未重新选择或编译入口。直接将 `mainCS` 改成 `main` 没有证据支持。

wgpu-types 30 的 `CreateShaderModuleDescriptorPassthrough` 明确警告：除了 SPIR-V，不应预期带 bindings 的 passthrough 能工作。wgpu-hal DX12 对 DXIL bytes 不应用 Naga binding map，但生成的 root signature 使用按资源种类压紧的 register counters；immediates 还占用 `b0, space0`。引擎 Slang 的 `ParameterBlock`/push-constant bindings 没有相应 DX12 remapping。

**leading hypothesis 是 Slang DXIL registers 与 HAL root signature 不一致**。目前未提取失败 blob 的数值 register 对照，因此这是有源代码支撑的强候选根因，而非完成了二进制级证明。需要进一步确立 compiler/HAL 绑定契约，不属于改入口名或局部十项修复即可解决的情况。

#### C. Sandbox 4 与 6 的额外失败

- `Sandbox/4-ComputeShader/Game.cs:215` 请求 `MainCS`，而 `Assets/box-blur.slang:31` 声明 `mainCS`。失败发生在 managed shader entry lookup。
- Sandbox 6 使用 `default_bind_group_texture_read`，但 `quad_pipline` 要求该组同时有 texture 与 sampler；draw 缺 binding 1。错误链已正确保留并经 `AlcoGpuCommandBuffer.EndCore` 返回，退出码 0 是错误处理策略导致。

本轮没有修改这两个示例或 shader，不能宣称所有基础示例均通过。

#### D. SmokeTrail 的设备销毁后 finalizer

截图及正常渲染已恢复，但退出后 BGL/resource group/compute pipeline wrapper 的 late finalizer 对已经销毁的 device 调用 native，产生 invalid-device 日志。这与原 GPU-06 的 consumed encoder handle 是不同生命周期问题。本轮未扩展到全引擎资源 teardown 重设计，不能称 SmokeTrail 完整日志无错误。

### 8.7 本地证据位置

以下是本次机器上的验证产物位置，不是仓库内测试依赖，也没有上传至外部服务：

| 证据 | 本地目录/文件 |
| --- | --- |
| Debug 首次全量 TRX | `C:/Users/10953/AppData/Local/Temp/alco-repair-test-results/debug-full` |
| **最终 Debug 全量 TRX** | `C:/Users/10953/AppData/Local/Temp/alco-repair-test-results/debug-final-serial` |
| Release 全量 TRX | `C:/Users/10953/AppData/Local/Temp/alco-repair-test-results/release-full` |
| PBR / particles / SmokeTrail 截图及日志 | `C:/Users/10953/AppData/Local/Temp/alco-repair-smoke-final-rvg6ject` |
| 五个窗口示例结果、日志及窗口计数 | `C:/Users/10953/AppData/Local/Temp/alco-fixed10-real-scenarios-20261005-202102-azsnbrtx/summary.json` 及同目录各示例日志 |
| Linux build/freshness/publish/pack/ELF/ABI 证据与 `.so`/NuGet 包 | `C:/Users/10953/AppData/Local/Temp/alco-repair-linux-evidence` |
| Windows ARM64 linker 失败及构建期诊断 | `C:/Users/10953/AppData/Local/Temp/alco-gpu03-5hzix4rp/gpu03-arm64-msvc-output.log` |
| 提交内存实验脚本 | `C:/Users/10953/AppData/Local/Temp/alco_gpu04_memory_probe.py` |
| 重建前收到的历史 win-x64 DLL 备份 | `C:/Users/10953/AppData/Local/Temp/alco-pre-fix-native-ym7fsbm3/alco_gpu.dll` |

**验收界限：**十项原审查问题的修复及上述对应回归已有证据；完整跨后端、跨平台及全部示例验收仍有明确失败和未验证项。保留失败测试，维持“整体分支尚未通过验收”的判断。

### 8.8 提交及 GitHub Actions 八 RID 交付

随后用户要求提交改动并使用 GitHub Actions 编译多平台二进制。修复、回归测试及构建工作流已提交为 `fed0ba76b654cbe275f2d5732e51e70e114ef6a8` 并推送至 `alco_wgpu`。

以该提交触发的 [Native alco-gpu run 37313673995](https://github.com/IssacZhuang/Alco/actions/runs/37313673995) **全部成功**，包括 Windows/macOS/Android 双架构、Linux x64、Linux ARM64 manylinux 2.28，以及汇总清单步骤。`create-pr=false`，没有自动创建 PR。

下载并核对了全部八个 RID 的架构、二进制 SHA-256、源码指纹及相邻 sidecar。所有库的源码指纹为 `e865152f2262eb0e710e3c1852d71527e55b8689198fd8f479f4b55f561748f9`；已替换仓库中的旧交付物，并在 manifest 的 `delivery` 中记录 source commit、run id 和 URL。替换前的库和清单保存在本地备份中。

| RID | 当前 CI 二进制 SHA-256 |
| --- | --- |
| win-x64 | `92b0b7465d26931d1d58b32819fc1cee87d1d5465579c4bf415643015d2c4dd6` |
| win-arm64 | `57266c712db5fbd878bb1b32a77f36433bbabf82808f9fe4b39c8edd86edd04f` |
| linux-x64 | `982ed4a770c85827dee5f42641474c68a3543ce5600eea73d58c81a9b4f5fa22` |
| linux-arm64 | `3298c01ce7c1f4aa87a9d6520f98d20fbb3b3d6affafd66571d0afc03f8c4b02` |
| osx-x64 | `ce390bc07d32203e71d47cfb729433cfbd86e2d51811a931f300783c925565a9` |
| osx-arm64 | `328b7938211dfcbe46d494c3cb5e1ea8e2a0aad090d7c6cddadb4265684167f7` |
| android-x64 | `223d548f3a58c2a8409648ebf0e43625ff5d7dea6e7a47febc86e811fc794c24` |
| android-arm64 | `ea3a31e01f9fc9f81760eed1674340528a725956fb0c15f5a81c4f55feeb4e6a` |

补充实际消费验证：

- 八个 RID 的 `ResolveAlcoGpuNative` 均返回 `_AlcoGpuDeliveryIsCurrent=True`。
- CI win-x64 DLL 直接加载返回 ABI `0x10002`，build info 返回 wgpu-core 30.0.1。
- 以不存在的 Cargo 路径执行完整 Debug `dotnet build` 成功，0 警告、0 错误，确认当前交付可免 Rust 消费。
- 使用 CI DLL 重跑完整 `Alco.Graphics.Test`：**66 通过、2 失败、0 跳过**；失败仍是第 8.6 节记录的两个 DX12 device-create 用例，无新增失败。
- `dotnet pack --no-build --no-restore -r win-x64` 成功生成包，并核对其中 native payload 为当前 CI DLL。
- CI linux-x64 `.so` 在 WSL 中实际加载，ABI handshake 返回 `0x10002`。

Actions 上传了八个单 RID artifact，以及包含库、sidecar 和 manifest 的 `alco-gpu-runtimes-all`。本地下载、旧产物备份、图形测试 TRX 和验证 NuGet 包位于 `C:/Users/10953/AppData/Local/Temp/alco-native-actions-37313673995-l3d1jcyw`。

第 8.3 节“只有本地 win-x64 重建、其余为旧库”的描述是提交前快照，当前仓库已由本节的八 RID CI 交付取代。**八平台编译成功不等于八平台 GPU 渲染验收通过**；第 8.6 节的 DX12、Sandbox 和 finalizer 问题仍未修复。
