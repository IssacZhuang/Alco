# Radiance Cache GI

## 目标与方案选择

Alco 的 WebGPU 后端目前没有硬件光线追踪接口，因此不能直接移植 RTXGI 的完整 DDGI ray-traced probe update，也不能照搬依赖 Mesh SDF / Surface Cache 的完整 Lumen。新系统采用 **Screen-seeded Cascaded Radiance Cache**：

- 三层相机跟随的世界空间 `32^3` cache，cell size 每层翻倍；
- 从当前 G-buffer 将表面出射 radiance 原子注入 cache；
- cache 滚动时按世界坐标重投影，不因相机跨 cell 而整体失效；
- 未出现在当前屏幕中的 cell 继续保留，并按 `OffscreenRetention` 缓慢衰减；
- 上一帧 cache 被当作本帧表面的入射光，再乘材质 albedo 写回，因此多帧后会收敛出多次 diffuse bounce；
- 每帧执行一次邻域传播，为空 cell 建立低频 radiance field；
- 屏幕空间四方向近场 gather 补足小于 cell 的接触染色；
- 半分辨率 gather 后执行 depth/normal bilateral upsample 和带 disocclusion 验证的 full-resolution temporal resolve。

它是独立的 `IGlobalIlluminationPlugin`，只通过 `GIDiffuse` / `GISpecular` 输出接入 Deferred Lighting，不读取或修改 `VoxelGiRenderer` 的 page table、attribute brick、radiance volume 或 history。Voxel GI 仍可单独选择。

## 参考

- [Dynamic Diffuse Global Illumination with Ray-Traced Irradiance Fields (Majercik et al., JCGT 2019)](https://jcgt.org/published/0008/02/01/paper-lowres.pdf)：世界空间 irradiance field、visibility-aware interpolation、normal bias、temporal hysteresis 和迭代多次弹反。
- [Scaling Probe-Based Real-Time Dynamic Global Illumination for Production](https://arxiv.org/abs/2009.10796)：滚动 probe volume、更新摊销、稳定性和生产环境参数。
- [NVIDIA RTXGI DDGI reference implementation](https://github.com/NVIDIAGameWorks/RTXGI-DDGI)：probe update、history blending、scroll clear 和资源布局的成熟实现。
- [Wicked Engine DDGI compute ray tracing shader](https://github.com/turanszkij/WickedEngine/blob/master/WickedEngine/shaders/ddgi_raytraceCS.hlsl)：硬件 RT 之外的软件 compute fallback 的工程参考。
- [Lumen: Real-time Global Illumination in Unreal Engine 5](https://www.realtimerendering.com/advances/s2022/SIGGRAPH2022-Advances-Lumen-Wright%20et%20al.pdf)：screen probe 负责近场、world-space radiance cache 负责远场与屏幕外稳定性的分工。

本实现借鉴上述缓存与时域策略，但不是 RTXGI 或 Lumen 的逐行移植。由于缺少通用场景 BVH / Mesh SDF，cache 的高质量材质 seed 来自 G-buffer；这意味着从未进入屏幕、也没有任何历史 seed 的表面不能立刻产生准确的材质染色。已经进入 cache 的 radiance 则与屏幕解耦，能够在离屏后稳定保留并参与后续 gather。

## GPU 流程

1. `RadianceCacheClear.hlsl`：清空本帧 fixed-point accumulation buffer。
2. `RadianceCacheInject.hlsl`：每个 `2x2` G-buffer block 选一个时变 sample，计算 sun/CSM、point light、sky、emissive 和 previous-cache bounce，原子注入全部覆盖它的 cascade。
3. `RadianceCacheUpdate.hlsl`：按世界坐标重投影旧 cache，解析原子累积，并用 radiance clamp + hysteresis 更新；未观测 cell 保留。
4. `RadianceCachePropagate.hlsl`：执行一次六邻域 Jacobi radiance 传播；有表面的 cell 保持为 source。
5. `RadianceCacheTrace.hlsl`：半分辨率 hemisphere cache gather、近场 screen gather 和 cache-based rough specular。
6. `RadianceCacheResolve.hlsl`：depth/normal-aware upsample，随后用 previous view-projection 和 camera distance 检查 disocclusion，再累积 full-resolution history。

默认资源（1280x720、50% gather）约 `40.6 MiB`，其中三层共有 `98,304` 个 cache cell。

## Sandbox 34

Radiance Cache 是 Sandbox 34 默认 GI：

```text
34-PBRDeferred.exe --procedural
```

旧 Voxel GI 保留：

```text
34-PBRDeferred.exe --procedural --gi=voxel
```

常用选项：

- `--no-gi`
- `--gi=radiance|voxel`
- `--gi-debug=DiffuseIrradiance|IndirectSpecular|CacheConfidence`
- `--gi-resolution=50|75|100`
- `--gi-offscreen-test`：先让场景可见并预热 cache，随后背对场景，截图前一帧返回；用于验证返回第一帧仍能读取离屏 cache。

ImGui 的 `Global Illumination (Radiance Cache)` 面板可调 bounce、cache hysteresis、off-screen retention、propagation、near-field distance、分辨率和 debug view。

## 验证记录

验证环境：Windows，Vulkan，NVIDIA GeForce RTX 4070 Ti。

- `dotnet build Alco.slnx --no-restore`：通过。
- `dotnet test Test/Alco.Engine.Test/Alco.Engine.Test.csproj --filter ValidateShader --no-restore`：全部 HLSL 通过 DXC/SPIR-V 验证。
- Sandbox 34 Radiance Cache 运行 90 帧并截图：通过，无 WebGPU validation error。
- `--gi-offscreen-test`：预热 30 帧、场景离屏 59 帧、返回第一帧仍保留红色物体向大球与地面的间接染色。
- `--gi=voxel` 回归运行并截图：通过，旧 Voxel GI 的 brick residency、voxelization 和输出路径正常。

验证截图位于 `artifacts/radiance-cache-procedural.png`、`artifacts/radiance-cache-diffuse-debug.png`、`artifacts/radiance-cache-cold-frame.png`、`artifacts/radiance-cache-offscreen-return.png` 和 `artifacts/voxel-gi-regression.png`。

## 当前边界

- 这是 diffuse-first cache；specular 是低频 cache reflection，不替代高质量 SSR / ray-traced reflection。
- 离屏 radiance 会稳定保留，但离屏动态材质或光照变化只有再次被观测后才能得到精确更新。
- 世界 cache 是低频近似；薄墙和小于 finest cell 的遮挡主要依赖 HBAO 与 screen gather，极端场景可能出现少量漏光。
- 若后端以后加入硬件 RT 或通用 GPU BVH，可保留现有 cache/update/resolve，单独把 screen seed 替换为 probe-ray seed，演进为完整 DDGI。
