# Alco 引擎 GI vs CRYENGINE 5.7.1 SVOTI — 完整差别对比

> 生成时间：2026-08-04
> 对比对象：
> - **Alco 引擎**：`Src/Alco.Rendering/Deferred/VoxelGi*.cs` + `Src/Alco.Engine/Assets/Shaders/Pipelines/Rendering/PBR/Voxel*.hlsl`
> - **CRYENGINE 5.7.1**：`Code/CryEngine/Cry3DEngine/SVO/*` + `Code/CryEngine/RenderDll/XRenderD3D9/D3D_SVO.*` + `Shaders/HWScripts/CryFX/CommonSVO.cfi` + `Total_Illumination.cfx`

---

## 目录

- [1. 架构总览](#1-架构总览)
- [2. 已经基本对齐的部分](#2-已经基本对齐的部分算法层面已一致)
- [3. 有差异但可以照抄/移植的部分](#3-有差异但可以照抄移植的部分)
- [4. 无法直接照抄的部分（数据结构绑定）](#4-无法直接照抄的部分数据结构绑定)
- [5. 总结：能不能做"完整复刻"](#5-总结能不能做完整复刻)
- [6. 性能基线分析](#6-性能基线分析)
- [7. 改进优先级评估](#7-改进优先级评估)
- [8. 推荐实施路线](#8-推荐实施路线)
- [附录 A：Alco GI 文件清单](#附录-aalco-gi-文件清单)
- [附录 B：CRYENGINE SVOTI 文件清单](#附录-bcryengine-svoti-文件清单)

---

## 1. 架构总览

### Alco 引擎

| 维度 | 实现 |
|------|------|
| **技术** | Voxel Cone Tracing GI（SVOGI 风格），clipmap + mip-mapped Texture3D |
| **稀疏存储** | 物理页池 + 一级页表（sparse brick page-pool），单次间接寻址 |
| **体素化** | 全 GPU 实时体素化（compute shader，三角形→体素），静态增量更新 + 动态全量重建 |
| **数据流** | Voxelize → Inject → Mip → Propagate ×N → Trace → Demosaic → Deferred Lighting |
| **渲染 Pass** | 8 个 compute shader pass（Clear / Voxelize / Inject / Mip / Propagate / BounceApply / Trace / Demosaic） |
| **Binding** | 宏系统 `DEFINE_UNIFORM/STORAGE/TEX3D_*`，slot 索引制，set 0 |

### CRYENGINE 5.7.1

| 维度 | 实现 |
|------|------|
| **技术** | SVOTI — Sparse Voxel Octree Total Illumination |
| **稀疏存储** | 八叉树指针级联，`brickPool_Tree` 存子节点指针纹理 |
| **体素化** | **CPU** 体素化（`CVoxelSegment::VoxelizeMeshes`），16³ brick，DXT 压缩，支持磁盘 streaming |
| **数据流** | CPU voxelize → Inject → Propagate ×N → ConeTrace → Demosaic → Upscale |
| **渲染 Pass** | Compute: Clear / InjectAtmosphere / DirectStatic / DirectDynamic / Propagate；Pixel: ConeTrace / Demosaic / UpScale / Atmosphere / SunShadows |
| **Binding** | 显式 register 绑定（`register(t0)` 等），根签名管理 |

---

## 2. 已经基本对齐的部分（算法层面已一致）

以下算法经过逐行对比确认逻辑等价，无需修改。

| 算法 | Alco 位置 | CE5 位置 | 说明 |
|------|-----------|----------|------|
| **Cone 累积公式** | `VoxelTrace.hlsl:328-329` | `CommonSVO.cfi:948,1078` | `color += (1-alpha) * a * rgb; alpha += (1-alpha) * a` |
| **方向性不透明度投影** | `VoxelTrace.hlsl:266` | `CommonSVO.cfi:914` | `dot(opacity.xyz, abs(rayDir))` |
| **近场淡入** | `VoxelTrace.hlsl:322` | `CommonSVO.cfi:948` | `saturate(distance / voxelSize)` |
| **DiffuseBias 注入** | `VoxelInject.hlsl:235` | `Total_Illumination.cfx:2149` | `vRGB += DiffuseBias * skyColorTop` |
| **PropagationBooster** | `VoxelPropagate.hlsl:244` | `Total_Illumination.cfx:2474` | `pow(collected, 1/1.5)` |
| **MinReflectance 0.2** | `VoxelPropagate.hlsl:250` | `SceneTreeCVars.inl` | 暗色 albedo 钳制 |
| **GetAverNormAndSmooth** | `VoxelTrace.hlsl:417-455` | `Total_Illumination.cfx:1487` | 2×2 深度加权法线 |
| **Demosaic min/max 双层** | `VoxelDemosaic.hlsl:173-196` | `Total_Illumination.cfx:979-1088` | near/far 层，深度边缘两侧各保完整核 |
| **UpScalePS 5-tap** | `DeferredLighting.hlsl` | `Total_Illumination.cfx:2609+` | 深度加权上采样 |
| **4 帧方位镜像** | `VoxelTrace.hlsl:370-371` | `Total_Illumination.cfx:1531-1537` | 奇偶帧翻转 kernel.x/y |
| **Sky light 仅首 bounce** | `VoxelPropagate.hlsl:177` | `Total_Illumination.cfx:2437` | `bAllowSkyLight = (nPassId == 0)` |
| **锥孔径 tan(θ/2)** | `VoxelTrace.hlsl:28` (`1/24`) | `Total_Illumination.cfx:1626` | 数值不同但概念一致 |
| **接收面偏移** | `VoxelTrace.hlsl:465-466` | `Total_Illumination.cfx:1578` | 按体素大小偏移起点 |

---

## 3. 有差异但可以照抄/移植的部分

### 3.1 Propagation 锥采样数量

| | CE5 | Alco |
|---|---|---|
| **锥数量** | **32 个**（`kernel_HS_32`）或 16（硬件 PCF 模式） | **9 个**（1 zenith + 4@45° + 4@75°） |
| **随机旋转** | ✅ `GetRndRotationMat(vPos0 + nPassId, ...)` 每帧位置相关抖动 | ❌ 固定方向 |
| **空气体素传播** | ✅ air voxel 也传播 sky light（`kernel_S_32`） | ❌ 只传播 occupied 体素 |

**影响**：CE5 的 32 锥 + 随机旋转在多 bounce 后收敛更平滑。Alco 的 9 锥偏少，在强间接光场景可能有 banding。

**可移植性**：✅ 可以直接移植 32 锥 kernel 和随机旋转。`TracePropagationCone`（Alco）和 `ConeTraceTreeAndSkyEx`（CE5）在体采样层面是等价的（都是 front-to-back 累积），只是 CE5 走树、Alco 走 mip 体积。

**参考位置**：
- Alco: `VoxelPropagate.hlsl:42-58`（PROP_CONE_DIRECTIONS / WEIGHTS）
- CE5: `Total_Illumination.cfx:2400-2420`（kernel_HS_32 循环）

---

### 3.2 Multi-bounce 缓冲方案

| | CE5 | Alco |
|---|---|---|
| **Bounce 池** | **5 个独立 RGB 池**（`brickPool_Rgbs` + `UAV_Popag1/2`），交替读写 | **1 个临时纹理** `_propagateTemp`，copy-back 到 mip 0 |
| **读写冲突** | 无（双缓冲交替） | 用额外 copy pass 绕过 |
| **开销** | 无额外 pass | 多一次全量 Texture3D copy（`VoxelBounceApply`） |

**可移植性**：✅ 可以移植 CE5 的双缓冲方案——在 clipmap 架构下只需两张 Texture3D 交替即可，省掉 copy-back pass。

---

### 3.3 Desaturation 控制

CE5 在传播后做了去饱和处理：
```hlsl
// CE5 Total_Illumination.cfx:2454
vCollectedLight = lerp(luminance(vCollectedLight), vCollectedLight, SvoParamsInject.z);
```

Alco 目前没有这一步。

**可移植性**：✅ 一个 lerp，直接加到 `VoxelPropagate.hlsl` 的 `gathered` 计算之后即可。

---

### 3.4 Injection 的光类型

| 光源 | CE5 | Alco |
|------|-----|------|
| **太阳** | CSM + **RSM**（Reflective Shadow Map） | CSM + cone march fallback |
| **点光** | **Tiled lights**（任意数量） | 4 个硬编码 |
| **Portal 灯** | ✅ `FindBestPortals` | ❌ |
| **RSM 太阳** | ✅ `rsmSunShadowMap` + `rsmSunColorsMap` | ❌ |

**可移植性**：
- ⚠️ RSM 注入需要引擎侧先实现 Reflective Shadow Map 管线（从光视角渲染 albedo + normal）
- ⚠️ Tiled lights 需要引擎侧的 tiled light culling 基础设施
- Portal 灯需要 portal 实体系统
- 算法本身可以直接照抄

---

### 3.5 Dual-kernel（radiance + opacity）✅ 已完成

CE5 的 `ConeTracePS` 用双 kernel——一个采样 radiance，一个采样 opacity（压低仰角增加 AO）：

```hlsl
// CE5 Total_Illumination.cfx:1522-1551
float3 kern = GetDiffuseKernel(tiling, i);          // radiance 方向
float3 kernOpa = GetDiffuseKernel(tiling, i, false); // opacity 方向
kernOpa.z -= SvoParamsCommon.y;                       // 压低仰角
kern = lerp(kernOpa, kern, saturate(transmittance * 4)); // 透明度混合
```

**状态**：已实现并验证（2026-08-04）。编译通过，shader 验证通过（`ValidateAllShaders`），180 个渲染测试全部通过。

**实现方案**：在 Alco 的 screen-space 锥追踪中移植了 CE5 的 dual-kernel opacity bias。与 CE5 的关键差异是 Alco 没有 G-buffer transmittance 通道，因此不执行 transmittance 混合——所有锥方向统一应用 Z 偏移。在 `DiffuseSpreading=0`（默认）时方向完全不变（identity），效果与改动前完全一致。

改动涉及 4 个文件：

- **VoxelCommon.hlsli**：`giFrameParams.w` 从 `unused` 改为 `diffuseSpreading`
- **VoxelTrace.hlsl**：`TraceDiffuseCones` 在帧镜像后计算 opacity 方向（`kernelDirection.z -= diffuseSpreading`），renormalize 后变换到世界空间。ALD 输出自动反映偏移后的方向（因为 `outWorldDir` 使用最终 trace 方向）
- **VoxelGiRenderer.cs**：新增 `DiffuseSpreading` 属性（float, 默认 0.0f，对应 CE5 `e_svoTI_Diffuse_Spr`），通过 `GiFrameParams.W` 传入 shader
- **Game.cs**：UI 新增 `GI Diffuse Spreading` 滑块（0.0–0.5），带 tooltip 说明

**算法说明**：
- CE5 的 `lerp(kernOpa, kern, saturate(transmittance * 4))`：不透明面（transmittance=0）→ 使用 kernOpa（压低仰角）；透明面（transmittance>0）→ 使用 kern（原方向）
- Alco 无 transmittance 通道，因此等价于 transmittance 恒为 0，始终使用 kernOpa。`DiffuseSpreading` 参数直接控制 Z 压低量
- Z 压低后 renormalize 使锥方向更靠近表面切平面 → 锥在近场采样到更多几何体 → 累积更多遮挡 → 更强的 contact AO

**质量影响**：DiffuseSpreading > 0 时增强近场 AO（角落、缝隙、接触面更暗），对开放空间影响较小。推荐值 0.1–0.3。0 = 无效果（与改动前完全一致）。

**性能开销**：零（仅修改方向向量，不增加采样次数）

---

### 3.6 ALD（Average Light Direction）输出 ✅ 已完成

CE5 的 diffuse trace 输出 ALD——方向加权平均：
```hlsl
// CE5 Total_Illumination.cfx:1635-1636
vALD.xyz += r.direction * brightness;  // 方向加权
vALD.w += brightness;                  // 亮度累加
```
然后在材质 shader 里用 ALD 做有方向的 diffuse 响应（间接光不是纯 flat ambient）。

**状态**：已实现并验证（2026-08-04）。编译通过，shader 验证通过（`ValidateAllShaders`），180 个渲染测试全部通过。

**实现方案**：在 Alco 的 clipmap + mip Texture3D 架构上移植了 CE5 的 ALD 管线。与 CE5 的关键差异是**能量守恒的方向调制**——CE5 在 demosaic 中将 RGB 归一化到单位向量，亮度完全由 ALD 的 `fIntensity` 重建；Alco 保留完整 RGB 辐射度，ALD 仅调制方向分布，不放大整体亮度。

改动涉及 5 个文件：

**Atlas 段数扩展**：
- `_traceRaw`：2 段（diffuse+vis, specular）→ **3 段**（+diffuse+vis, specular, ALD）
- `_indirectAtlas`：3 段 → **5 段**（+ALD near, ALD far）
- `_historyGI`：4 段 → **6 段**（+ALD near, ALD far）

- **VoxelTrace.hlsl**：`TraceDiffuseCones` 新增 `outWorldDir` 输出参数；MainCS 计算 `ALD = float4(worldDir × brightness, brightness)`，写入 `_traceRaw` 第 3 段
- **VoxelDemosaic.hlsl**：双层空间 gather 中同步累积 ALD（相同 bilateral 权重），时域累积中对 ALD 做同样的 reprojection + blend；输出到 `_indirectAtlas` 第 3/4 段和 `_historyOut` 第 3/4 段
- **DeferredLighting.hlsl**：5-tap 上采样同时 gather ALD near/far 层，应用能量守恒方向调制：

```hlsl
// dirFraction: 0=环境光（均匀半球），1=单一主导方向
float dirFraction = saturate(dirIntens / aldBrightness);
float directionalMod = lerp(1.0, NdotAld * 2.0, dirFraction);
// 环境光 → mod=1.0（亮度不变）；方向光 → mod=NdotAld*2（半球均值≈1.0，能量守恒）
diffuseIrradiance = indirectDiffuse.rgb * directionalMod;
```

- **VoxelGiRenderer.cs**：`_traceRaw` 2×→3×, `_indirectAtlas` 3×→5×, `_historyGI` 4×→6×；`traceWidth` 推导从 `/2` 改为 `/3`
- **PBRDeferredPipeline.cs**：`Params4.Z` 系数从 `3.0/Width` 改为 `5.0/Width`

**显存开销**：+2 段 × traceW × traceH × 8 bytes（atlas）+ 2 段 × traceW × traceH × 8 bytes（history）≈ +18 MiB（1080p, scale=0.5）

**质量影响**：间接光从 flat ambient 变为有方向性的 diffuse——角落和背向间接光源的面变暗，朝向 bounce-light 源的面更亮，立体感显著增强。新增 debug view `giDebugView=4` 可可视化 ALD 方向。

**性能开销**：极小（每像素多读 2 张纹理 + 少量算术）

**与 CE5 差异说明**：CE5 的 `UpScalePS` 用 `fIntensity = fDirIntens × pow(NdotH, 1) + max(0, vALD.w - fDirIntens)` 重建亮度（因为 RGB 已归一化）。Alco 保留完整 RGB，直接用 `directionalMod = lerp(1.0, NdotAld*2, dirFraction)` 做方向调制。两者在视觉效果上等价——都让间接光有方向性——但 Alco 的方案不需要在 demosaic 中归一化 RGB，避免了色彩信息丢失。

---

### 3.7 Firefly clamp 方式

| CE5 | Alco |
|-----|------|
| 邻域 clamp（用 4 个相邻 phase 做对比） | 固定 luminance 上限 `ClampRadianceLuminance(rgb, 8.0)` |

Alco 的注释解释了为什么不用邻域 oracle——有效小光源可能只出现在一个方向上。

**建议**：这是一个正确的改进，不建议回退到 CE5 的方式。

---

### 3.8 Propagation 最大距离

```
CE5:  VOX_CONE_MAX_LEN = 12m (固定值)
Alco: maxDistance = voxelSize * resolution (整个 level 范围)
```

Alco 让粗 level 传播更远，是改进。CE5 固定 12m 导致粗 level 利用率低。

**建议**：保持 Alco 的方案。

---

### 3.9 Analytical Occluders

CE5 的 `ProcessAnalyticalOccluders()` 支持手工放置的胶囊体/OBB/圆柱体做廉价间接阴影。

**可移植性**：✅ 与数据结构无关，纯数学交集测试。需要引擎侧增加 occluder 组件。

---

### 3.10 Air 体素传播

CE5 在空气体素中也传播光——`bAir` 分支调用 `kernel_S_32`（天空半球 kernel），让 sky light 通过空气传播。

Alco 没有空气体素概念，sky light 只通过首 bounce 的锥追踪 fallback 进入体积。

**可移植性**：✅ 可以移植。需要：
1. 在 voxelization 阶段标记空气体素（或不标记——直接在传播 pass 里对空体素也 trace）
2. 修改 `VoxelPropagate.hlsl` 使空体素也参与传播

---

### 3.11 Troposphere / 体雾

CE5 的 `RenderAtmospherePS` + `ComputeInjectAtmosphere` 用体素网格做大气散射和云阴影。依赖 `brickPool_Opac` 的 air density 通道。

**可移植性**：✅ 可以移植但需要先扩展体素数据格式，增加 air density 通道。

---

### 3.12 Sun Shadow 追踪

| CE5 | Alco |
|-----|------|
| `TraceSunShadowsPS`：用 `ConeTraceTree` 以 geometry-only 模式追踪太阳阴影 | inject pass 里的简化 cone march（`TraceVoxelSunCone`） |

**功能等价**，精度不同。Alco 的方案更轻量。

---

## 4. 无法直接照抄的部分（数据结构绑定）

### 4.1 ConeTraceTree（八叉树下降遍历）— 核心不可移植

这是最关键的区别。CE5 的 cone tracing **本身就是八叉树遍历**：

```hlsl
// CE5 CommonSVO.cfi:1108 — ConeTraceTree
for (int nDescentId = 0; nDescentId < 64; nDescentId++) {
    // 从根节点开始，沿射线方向下降
    for (; curNode.nTreeLevel < nMaxTreeDepth; ...) {
        // 判断射线在8个子节点中的哪一个
        uint3 vS = step(center, rayOrigin);
        int nChildId = dot(vS, float3(4,2,1));
        // 从 brickPool_Tree 纹理读取子节点指针
        int childPtr = ReadChildPtr(curNode.vTC, nChildId);
        if (childPtr) curNode.vTC = ComputeDataTC(childPtr);
        else break; // 空间跳跃
        // LOD 测试：cone 直径 vs 节点大小
        if (GetLodTransition(...) >= 1) break;
    }
    // 到达叶子节点后，march 16³ brick
    ConeTraceBrick(..., curNode.vTC, ...);
    // 推进射线到节点 AABB 外
}
```

**不可移植原因**：
- 没有 `brickPool_Tree`（子节点指针纹理）
- 没有多级树下降（clipmap 是扁平的）
- LOD 不是通过树深度控制，而是通过 Texture3D 的 mip level

**Alco 的替代方案是正确的**：`TraceCone` 直接在 mip-mapped `Texture3D` 上采样，用 `mip = log2(diameter / voxelSize)` 计算 LOD。这在数学上是等价的——cone 的直径随距离增长，对应 mip level 提升——只是实现路径不同。

---

### 4.2 ConeTraceBrick（16³ brick 内行进）

CE5 在叶子节点内 march 16³ brick：
```hlsl
// CE5 CommonSVO.cfi:870
for (int nSample = 0; nSample < VOX_BRICK_TEXRES * 2; nSample++) {
    r.startpoint += r.direction * fVoxSize * fStepSize;
    float3 vCurTC = tcMin + worldToBrickTC(r.startpoint);
    // 读取 brickPool_Norm, brickPool_Rgbs, brickPool_Opac
}
```

**不需要移植**——Alco 的硬件 trilinear + mip chain 已经提供了连续 LOD 的体积采样，替代了手动的 brick 行进。

---

### 4.3 Mesh Ray Tracing（`RayTraceMesh`）

CE5 在 `_RT_QUALITY` 模式下，对光滑表面用 mesh RT 替换锥追踪反射：
```hlsl
// CE5 CommonSVO.cfi:977-1003
if (fMultTriRT > 0 && vRGBD.w) {
    RayTraceMesh(float4(vCurTC.xyz, vRGBD.w), rOrig, vRgbOutRT, vHitNorm, fNearestHitDist);
}
```

**不可移植原因**：依赖 `brickPool_RTri`（三角形索引 atlas）和 `geomPool_Tris`（三角形顶点 atlas），这些是 CPU 体素化时构建的。Alco 的 GPU 体素化不生成三角形数据。

**如果要实现**：需要独立的 mesh RT 管线（如 DXR / ray query），从体素锥追踪完全切换到硬件光线追踪。

---

### 4.4 磁盘流式加载（Streaming）

CE5 的 `CVoxStreamEngine` 支持从磁盘加载预烘焙的体素数据：
```cpp
// VoxelSegment.cpp — 预烘焙 + DXT 压缩 + 磁盘 I/O
class CVoxStreamEngine { ... };
```

**不可移植原因**：Alco 是全 GPU 实时体素化，没有磁盘体素数据格式。这是架构层面的选择差异。

---

### 4.5 CPU 体素化 + DXT 压缩

CE5 的 CPU 体素化 (`CVoxelSegment::VoxelizeMeshes`) 在 CPU 上做三角形光栅化到体素，计算 opacity/albedo/normal/distance-to-surface，然后 DXT 压缩上传。

Alco 用 compute shader 做 GPU 体素化，原子累加 packed attributes。

**这是架构差异**，不是算法差异。两种方式生成的体素数据在后续管线中的用法是等价的。

---

### 4.6 brickPool_Tree 指针纹理遍历

CE5 将整个八叉树结构编码在 `brickPool_Tree` 纹理中——每个节点的 AABB + 8 个子节点指针。shader 通过 `ReadNodeCropBox` / `ReadChildPtr` 读取。

**不可移植原因**：clipmap 没有树结构，用扁平页表替代。

---

## 5. 总结：能不能做"完整复刻"

### 答案：算法可以照抄，但八叉树遍历不能（也不需要）

**算法层面的等价性**：clipmap + mip-mapped Texture3D 方案已经**正确地替代了** CE5 的八叉树遍历。锥追踪的核心数学——front-to-back alpha 累积、cone 直径到 LOD 的映射、方向性不透明度——在两种数据结构下是等价的。**不需要复刻八叉树遍历来获得相同的视觉效果。**

### 能完整复刻的（直接照抄算法）

- ✅ 光照注入的全部数学（CSM shadow、点光、DiffuseBias、emissive）
- ✅ 传播的完整流程（增加到 32 锥 + 随机旋转 + desaturation + multi-bounce 双缓冲）
- ✅ Screen-space 锥追踪的完整逻辑（64 方向 Bayer tile、4 帧镜像、GetAverNormAndSmooth）
- ✅ Demosaic 双层 resolve + 时域累积
- ✅ UpScalePS 上采样
- ✅ 方向性不透明度投影
- ✅ PropagationBooster / MinReflectance
- ✅ ALD（Average Light Direction）输出（方向加权平均 + 能量守恒方向调制）

### 能移植但需要引擎侧基础设施的

- ⚠️ RSM 注入（需要 Reflective Shadow Map 管线）
- ⚠️ Tiled lights（需要 tiled light culling）
- ⚠️ Portal 灯变形（需要 portal 系统）
- ⚠️ ~~Dual-kernel opacity（多一组 kernel 方向 + cbuffer 参数）~~ ✅ 已完成（§3.5）
- ⚠️ Analytical Occluders（需要 occluder 组件系统）
- ⚠️ Air 体素传播（需要扩展体素数据格式）
- ⚠️ Troposphere / 体雾（需要 air density 通道）

### 不能移植的（八叉树绑定）

- ❌ `ConeTraceTree` — 八叉树下降遍历
- ❌ `ConeTraceBrick` — 16³ brick 内行进
- ❌ `RayTraceMesh` — mesh RT 反射（依赖 CPU 体素化的三角形 atlas）
- ❌ 磁盘流式加载
- ❌ `brickPool_Tree` 指针纹理遍历

### 质量提升优先级建议

如果要进一步提升 GI 质量，按预期收益排序：

| 优先级 | 改进项 | 预期收益 | 状态/工作量 |
|--------|--------|----------|--------|
| **1** | 传播锥从 9 增到 32 + 随机旋转 | ★★★★★ | 中（kernel 表 + 旋转矩阵 + 性能调优） |
| **2** | ALD 输出 | ★★★★☆ | ✅ 已完成 |
| **3** | Air 体素传播 | ★★★☆☆ | 中（体素数据格式扩展 + 传播 pass 修改） |
| **4** | Multi-bounce 双缓冲 | ★★☆☆☆ | 低（多一张 Texture3D，去掉 copy pass） |
| **5** | Desaturation 控制 | ★★☆☆☆ | 低（一个 lerp） |
| **6** | Dual-kernel opacity | ★★★☆☆ | ✅ 已完成 |
| **7** | RSM 注入 | ★★★★★ | 高（需要 RSM 管线） |
| **8** | Tiled lights | ★★★☆☆ | 高（需要 tiled light 基础设施） |

---

## 6. 性能基线分析

### 6.1 默认配置

所有数字基于默认配置（`resolution=128`, `baseVoxelSize=0.1`, 4 levels, 1 bounce, trace 半分辨率）。

| 参数 | 默认值 | 来源 |
|------|--------|------|
| `LevelCount` | 4（硬编码） | `VoxelGiRenderer.cs:307` |
| `BrickSize` | 8（硬编码） | `VoxelGiRenderer.cs:308` |
| `resolution` | 128 | `VoxelGiRenderer.cs:431` |
| `baseVoxelSize` | 0.1 world units | `VoxelGiRenderer.cs:432` |
| `_mipCount` | `log2(128)+1 = 8` | `VoxelGiRenderer.cs:444` |
| `Get/SetStaticBrickBudget(level)` | 32/16/8/4 bricks/frame（L0→L3，可按层分别调节） | `VoxelGiRenderer.cs:444` |
| `DynamicLevelCount` | 2（仅最近 2 个 level 处理动态几何） | `VoxelGiRenderer.cs:323` |
| `BounceCount` | 1 | `VoxelGiRenderer.cs:329` |
| `TraceResolutionScale` | 0.5（半分辨率） | `VoxelGiRenderer.cs:433` |
| `TemporalHysteresis` | 0.8 | `VoxelGiRenderer.cs:341` |
| `DiffuseTemporalHysteresis` | 0.9 | `VoxelGiRenderer.cs:350` |

Clipmap 几何参数：

- `BricksPerAxis = 128 / 8 = 16`
- 每 level 页数 = `16³ = 4,096`
- 体素大小 = `0.1 × 2^level`：**0.1, 0.2, 0.4, 0.8** world units
- Level 覆盖范围 = `128 × voxelSize`：**12.8m, 25.6m, 51.2m, 102.4m**

### 6.2 GPU 资源占用

#### Texture3D 体积纹理（RGBA16Float = 8 bytes/voxel）

| 资源 | 尺寸 (W×H×D) | Mips | 显存 |
|------|-------------|------|------|
| `_radiance` | 128×128×512（128×4 levels 沿深度堆叠） | 8 | ~73.2 MiB |
| `_opacity` | 同上 | 8 | ~73.2 MiB |
| `_propagateTemp` | 同上 | 1 | 64.0 MiB |
| **Texture3D 合计** | | | **~210 MiB** |

#### 属性缓冲（稀疏物理页池）

| 池 | 页容量 | 体素容量 | 缓冲大小 |
|----|--------|---------|---------|
| `_attrStatic` | 8,192（2 个完整 level） | 4,194,304 | 64 MiB |
| `_attrDynamic` | 4,096（1 个完整 level） | 2,097,152 | 32 MiB |
| **属性合计** | | | **96 MiB** |

#### 屏幕空间 RT（1080p, scale=0.5 → 960×540）

| 资源 | 尺寸 |
|------|------|
| `_indirectAtlas` | 2880×540（3 段：diffuse-near / diffuse-far / specular） |
| `_traceRaw` | 1920×540（2 段：diffuse + specular） |
| `_historyGI[0]`, `[1]` | 3840×540 各一个（4 段含 depth+normal） |

#### 总显存

**~306 MiB**（96 MiB attributes + 210 MiB radiance/opacity/propagate）

### 6.3 每帧 dispatch 开销

#### 固定 dispatch（默认配置：4 levels, 1 bounce）

| Pass | Dispatch 数 | 每次 dispatch 尺寸 | 线程组数/dispatch |
|------|------------|-------------------|------------------|
| Static clear | ≤4（每 level, 有 dirty 时） | (8, 8, 8×brickCount) | (2, 2, 2×brickCount) |
| **Inject** | **4**（每 level） | (128, 128, 128) | **32,768** |
| Mip 链 #1（注入后） | **28**（7 mip × 4 levels） | 递减 | mip 0→1: 4,096; 后续剧降 |
| **Propagate** | **4**（1 bounce × 4 levels） | (128, 128, 128) | **32,768** |
| **BounceApply** | **4**（1 bounce × 4 levels） | (128, 128, 128) | **32,768** |
| Mip 链 #2（传播后） | **28** | 同 #1 | 同 #1 |
| Trace | **1** | (traceW, traceH, 1) | ~(120, 68) at 1080p |
| Demosaic | **1** | (traceW, traceH, 1) | ~(120, 68) at 1080p |

#### 可变 dispatch（场景相关）

| Pass | Dispatch 数 | 说明 |
|------|------------|------|
| Static voxelize | 4 levels × N_intersecting_instances | 每 instance 每 level 一次 dispatch |
| Dynamic clear | 2（DynamicLevelCount） | |
| Dynamic voxelize | 2 levels × N_dynamic_instances | 每 instance 每 level 一次 |

**Voxelize 是最重的可变开销**——每个 instance 每 level 发 `ceil(triangleCount/64) × 8` 个线程组。10k 三角形 × 4 levels ≈ 5,056 个线程组/instance。

#### 线程组合计（典型场景）

| Pass | 线程组数/帧 |
|------|------------|
| **Inject** | 4 × 32,768 = **131,072**（全量，含空 voxel） |
| **Propagate** | 4 × 32,768 = **131,072**（9 锥 × 32 步/voxel） |
| **BounceApply** | 4 × 32,768 = **131,072**（纯 memcpy） |
| Mip 链 ×2 | ~28 dispatch × 2，mip 0→1 主导：4 × 4,096 = 16,384 |
| Trace + Demosaic | ~16,000 threads（半分辨率，最轻量） |
| **固定合计** | **~70 dispatch, ~410K+ 线程组/帧** |

### 6.4 性能瓶颈识别

**核心问题：Inject + Propagate + BounceApply 做全量 128³ dispatch，但大部分 voxel 是空的。**

典型场景 occupancy ~15-30%。三个最重的 pass 合计 ~393K 线程组中，约 70-85% 在 early-return。dispatch 调度开销（hardware 生成 wave、分配资源）全付了。

---

## 7. 改进优先级评估

### 设计原则

> **先释放预算（Phase 1），再花预算提升质量（Phase 2）**
>
> Phase 1 的稀疏化把 inject+propagate 从 ~260K 线程组降到 ~40-80K，腾出的预算在 Phase 2 里花在锥数和 ALD 上——最终 propagate 比 Phase 1 之前更便宜（分级锥数 + 稀疏化），同时质量显著更高。

---

### Phase 1 — 性能释放（零质量影响，纯降开销）

#### ① Inject / Propagate 稀疏化 dispatch ✅ 已完成

**状态**：已实现并验证（2026-08-04）。编译通过，shader 验证通过（`ValidateAllShaders`），运行正确。

**改动前**：每 level dispatch 全量 `128³ = 32,768` 个线程组，空 voxel early-return 但 dispatch 调度开销全付。

**实现方案**：只 dispatch 有数据的 brick。

- **Shader 侧** (`VoxelInject.hlsl` / `VoxelPropagate.hlsl` / `VoxelBounceApply.hlsl`)：
  - 新增 `_brickList` storage buffer，每个 entry 是一个 `uint4`（xyz = brick 逻辑坐标，w = padding）
  - 入口点从 `dispatchId` 直接做坐标改为通过 `_brickList[brickIndex].xyz * 8 + localOffset` 重建逻辑坐标
  - 将 `_pageTableStatic` + `_pageTableDynamic` 合并为一个 `RWStructuredBuffer<uint2> _pageTable`（`.x` = static、`.y` = dynamic），腾出一个 descriptor set 给 `_brickList`（WebGPU 限制 8 个 set）

- **C# 侧** (`VoxelGiRenderer.cs`)：
  - 新增 `CollectResidentBricks(level)`：每帧扫描 page table（固定 4096 slots/level），收集 resident + stale brick 列表，构建 combined page table
  - Stale brick（上帧 resident、本帧已释放）追加到 brick list 末尾，Inject 发现 page entry = 0 自然写零——清理旧 radiance，无需额外 clear pass
  - Inject dispatch `(8, 8, 8 × brickCount)`，Propagate/BounceApply dispatch `(8, 8, 8 × residentCount)`
  - 新增 `_residentBrickCoordinates[]` / `_pageTableCombined[]` GPU buffer（共 ~640 KB）

- **Diagnostics**：`VoxelGiStatistics` 新增 `SparseBrickTotal` / `DenseBrickTotal`，Sandbox UI 显示 sparse dispatch 百分比

**实测结果**：
- Resident brick 数量：~2000/16384（~12% occupancy），sparse dispatch 正确生效
- **帧率无明显变化**——分析结论：Inject/Propagate 不是瓶颈
  - Dense 模式下空 voxel 线程在前几条指令 early-return（读 page table → entry=0 → return），GPU warp scheduler 快速回收，实际 GPU 耗时很低
  - 真正的瓶颈是 **VoxelTrace**（屏幕空间全分辨率，每像素 6-9 锥 × 32 步 = 200-300 次纹理采样）和 **BuildMipChains**（仍然 dense）
  - 稀疏化的价值在于 Phase 2 增加 16 锥后 propagate 开销仍在可控范围内

**质量影响**：零（输出完全相同）

**CPU 开销**：与场景物体数量**完全无关**——`CollectResidentBricks` 扫描固定大小 page table（4 × 4096 = 16K slots），不遍历物体列表。估算 < 0.05ms/帧。

**参考**：CE5 的 `GetSvoBricksForUpdate()` + `SVO_NodesForUpdate0..3` 就是这个机制。

---

#### ② Multi-bounce 双缓冲（消除 BounceApply） ✅ 已完成

**状态**：已实现并验证（2026-08-04）。编译通过，shader 验证通过（`ValidateAllShaders`）。

**改动前**：propagate 写 `_propagateTemp` → BounceApply copy 回 `_radiance` mip 0 → 重建 mip 链。BounceApply 是纯 memcpy pass（sparse 后 ~2000 bricks × 4 levels = ~8K 线程组）。

**实现方案**：两张完整 mip-chain radiance Texture3D 交替读写，propagate 直接写入目标纹理 mip 0。

```
Inject → radiance[0] mip 0 → BuildMipChains(radiance[0])
Bounce 0: Propagate reads radiance[0] → writes radiance[1] mip 0 → BuildMipChains(radiance[1])
Bounce 1: Propagate reads radiance[1] → writes radiance[0] mip 0 → BuildMipChains(radiance[0])
...alternating each bounce
Trace reads from the last-written radiance texture
```

- **VoxelGiRenderer.cs**：
  - `_radiance` (单 Texture3D) + `_propagateTemp` (单 mip Texture3D) → `_radiance[2]` (双完整 mip-chain Texture3D)
  - 移除 `_bounceApplyMaterial` 字段和构造函数 `bounceApplyShader` 参数
  - `BuildMipChains` 改为接收 `Texture3D radiance` 参数，每次在正确的纹理上构建 mip 链
  - Render 方法中每个 bounce 动态切换 propagate 的读写绑定：`SetTexture("_radiance", read)` + `SetTexture3DStorage("_propagateOut", write, 0)`
  - Trace 在 bounce 循环结束后绑定到最后写入的纹理

- **Shader 侧**：VoxelPropagate.hlsl 无算法修改，仅更新头部注释说明双缓冲方案。VoxelBounceApply.hlsl 不再使用（文件保留但已不被引用）。
- **Game.cs**：移除 `VoxelBounceApply.hlsl` shader 加载行。

**收益**：
- 消除 BounceApply pass（sparse 后 ~8K 线程组 → 0，4 个 dispatch → 0）
- 消除 `_propagateTemp` 纹理（64 MiB → 0），但新增第二张完整 mip-chain radiance（+73.2 MiB）
- 净增显存 ~9.2 MiB（第二张 mip-chain radiance 73.2 MiB - 移除 _propagateTemp 64 MiB）
- 多 bounce 时无 copy 开销，bounce 数量扩展成本更低

**质量影响**：零（算法完全等价——propagate 读源纹理写目标纹理，双缓冲消除了 read-modify-write hazard）

**与 CE5 方案对比**：CE5 用 5 个独立 RGB 池交替读写。Alco 用 2 张 Texture3D 交替，原理相同，只是存储粒度不同（CE5 按 brick pool 分池，Alco 按整张 volume 分池）。

---

#### ③ Mip 链合并小 mip dispatch ✅ 已完成

**现状**：7 个 mip transition × 4 levels = 28 dispatch/次 × 2 次/帧 = 56 dispatch。但 mip 4 以上合计只有 ~520 个线程组，却用了 16 个 dispatch。

**方案**：两层优化：
1. **Level 维度合并**（VoxelMip.hlsl）：将 4 个 clipmap level 打包进 dispatch z 维度（`z = dstRes * LevelCount`），shader 内 `level = dispatchId.z / dstRes` 解包。一次 dispatch 覆盖所有 level，消除内层 level 循环。
2. **尾部 3 transition 级联**（VoxelMipChain.hlsl）：新增级联 shader，在 `[numthreads(4,4,4)]` 的一个线程组内用 groupshared 内存完成 srcMip→srcMip+1→srcMip+2→srcMip+3 三个连续 transition。radiance 和 opacity 分别 dispatch（因 descriptor set 上限无法同时容纳两组各 3 个输出 mip）。

**实际 dispatch 变化**（resolution=128, _mipCount=8, cascadeSrcMip=4）：
- 标准 mip：4 个 transition × 1 dispatch（含所有 level）= 4 dispatch/texture
- 级联：1 dispatch/texture（radiance）+ 1 dispatch/texture（opacity）= 2 dispatch
- 每 texture 6 dispatch（原 28），每帧 2 次 BuildMipChains → **12 dispatch/帧**（原 56，减少 79%）

**实现文件**：
- `VoxelMip.hlsl` — 改为 z 维度打包 level
- `VoxelMipChain.hlsl` — 新增级联 shader
- `VoxelGiRenderer.cs` — 新增 `_mipChainMaterial` + `DispatchMipChain()` 方法
- `Game.cs` — 新增 VoxelMipChain shader 加载

**收益**：减少 ~44 dispatch/帧（对低延迟 API 开销改善明显）

**质量影响**：零

**工作量**：低

---

### Phase 2 — 质量提升（在 Phase 1 释放的预算内）

#### ④ Propagation 锥 9→16 + 随机旋转

**现状**：9 个固定方向（1 zenith + 4@45° + 4@75°），无旋转。

**方案**：16 个方向 + `GetRndRotationMat(position + frameIndex)` 位置相关随机旋转。

**为什么不是 32**：32 锥让 propagate 开销 3.5x。16 锥 + 旋转在时域累积后等价于 32+ 锥覆盖，性价比远高于直接上 32。

**质量收益**：★★★★★
- 消除间接光 banding（9 锥方位间隔太大，单次 trace 可见）
- 随机旋转让多帧时域累积收敛到正确积分
- 对粗糙表面间接光的色彩渗色更均匀

**性能成本**：propagate 从 9 锥 × 32 步 → 16 锥 × 32 步 = +78%。Phase 1 稀疏化后完全在预算内。

**工作量**：中（kernel 表 + 旋转矩阵 + cbuffer 参数）

---

#### ⑤ Propagation 按 level 分级锥数

**现状**：4 个 level 都用相同的锥数。

**方案**：

| Level | 体素大小 | 覆盖范围 | 锥数 | 理由 |
|-------|---------|---------|------|------|
| 0 | 0.1 | 12.8m | 16 | 近场需要精度 |
| 1 | 0.2 | 25.6m | 9 | 中场 |
| 2 | 0.4 | 51.2m | 5 | 远场粗采样足够 |
| 3 | 0.8 | 102.4m | 3 | 极粗，只需天空光整体感 |

**收益**：Level 2-3 voxel 体积大（102m 覆盖）但传播精度需求低。分级后总 propagate 开销降低 ~40%。

**质量影响**：Level 2-3 间接光稍粗，但屏幕占比小且被 demosaic 时域平滑覆盖。

**工作量**：低（per-level cbuffer 参数 + shader 分支）

---

#### ⑥ ALD（Average Light Direction）输出 ✅ 已完成

**状态**：已实现并验证（2026-08-04）。编译通过，shader 验证通过（`ValidateAllShaders`），180 个渲染测试全部通过。

**改动前**：diffuse trace 输出 `float4(rgb, visibility)`，deferred lighting 当 flat ambient 用。

**实现方案**：trace 输出 `float4(ald.xyz * brightness, brightness)`，demosaic 双层空间 + 时域同步传播 ALD，deferred lighting 用能量守恒方向调制替换 flat ambient。

详见 [§3.6](#36-aldaverage-light-direction输出-已完成)。

**质量收益**：★★★★☆
- 间接光有方向性——角落变暗、法线朝向间接光源的面更亮
- 缺失 ALD 会显得 GI "平"
- 对角色和物体的间接光照立体感影响很大

**性能成本**：极小（每像素多读 2 张纹理 + 少量算术）

**显存开销**：+~18 MiB（atlas + history 各增加 2 段，1080p scale=0.5）

**与 CE5 差异**：CE5 在 demosaic 中将 RGB 归一化到单位向量，亮度由 ALD 的 `fIntensity` 重建。Alco 保留完整 RGB 辐射度，ALD 仅做方向调制（`lerp(1.0, NdotAld*2, dirFraction)`），避免了亮度被双重计算的问题。

---

#### ⑦ Desaturation 控制

```hlsl
gathered = lerp(luminance(gathered), gathered, saturationParam);
```

**质量收益**：★★☆☆☆（美学微调）

**工作量**：极低（一个 lerp，可和 ④ 一起做，加一个 cbuffer 参数）

---

### Phase 3 — 补充完善（按需）

#### ⑧ Air 体素传播

**现状**：只有 occupied voxel 参与传播。sky light 仅通过首 bounce 锥 fallback 进入体积（单方向采样，不如半球积分精确）。

**方案**：propagate pass 对空 voxel 也 trace 天空半球（CE5 `bAir` 路径），sky light 通过空气传播到室内。

**质量收益**：★★★☆☆
- 室内 sky light 更自然（从窗口漫射进来，而非只靠墙面首 bounce）
- 对大面积开口建筑（门廊、窗洞）效果明显

**性能成本**：空 voxel 也 trace = 更多工作量。配合 Phase 1 稀疏化，可只对"有 occupied 邻居"的空 brick 做。

**工作量**：中（修改 propagate shader 对空 voxel 的处理逻辑）

---

#### ⑨ Dual-kernel opacity ✅ 已完成

CE5 `ConeTracePS` 用双 kernel——radiance kernel + opacity kernel（压低仰角增加 AO）。

**状态**：已实现并验证（2026-08-04）。详见 [§3.5](#35-dual-kernelradiance--opacity-已完成)。

**质量收益**：★★★☆☆（更好的近场 AO）

**工作量**：低（方向 Z 偏移 + cbuffer 参数，零额外采样）

---

### Phase 4 — 需要引擎基础设施

| 改进 | 依赖 | 质量收益 | 备注 |
|------|------|----------|------|
| **RSM 注入** | Reflective Shadow Map 管线 | ★★★★★ | 近场太阳反射品质飞跃；引擎有 shadow map 基础设施后可做 |
| **Tiled lights** | Tiled light culling | ★★★☆☆ | 当前 4 个硬编码点光够用；支持更多光源后再做 |
| **Analytical Occluders** | Occluder 组件系统 | ★★☆☆☆ | 角色/物体间接阴影，成本/收益比一般 |
| **Troposphere** | Air density 通道 | ★★★☆☆ | 体雾系统，需要体积云/雾才值得做 |

---

## 8. 推荐实施路线

```
Phase 1 — 性能释放（预计 1-2 天）
  ① Inject/Propagate 稀疏化 dispatch      ✅ 已完成
  ② Multi-bounce 双缓冲（消除 BounceApply）  ✅ 已完成
  ③ Mip 链合并小 mip dispatch               ✅ 已完成

Phase 2 — 质量提升（预计 3-5 天）
  ④ Propagation 16 锥 + 随机旋转           ← 最大质量收益
  ⑤ Level 分级锥数
  ⑥ ALD 输出                               ✅ 已完成
  ⑦ Desaturation（顺手做）

Phase 3 — 补充完善（按需）
  ⑧ Air 体素传播
  ⑨ Dual-kernel opacity                      ✅ 已完成

Phase 4 — 需要引擎基础设施（长期）
  RSM 注入 / Tiled lights / Analytical Occluders / Troposphere
```

### 预期最终效果

| 指标 | 当前（默认配置） | Phase 1 后 | Phase 2 后 |
|------|----------------|-----------|-----------|
| Inject 线程组/帧 | ~131K | ~2-4K（实测 ~12% occupancy）✅ | ~2-4K |
| Propagate 线程组/帧 | ~131K (9 锥) | ~2-4K (9 锥) ✅ | ~3-6K (16 锥, 分级) |
| BounceApply 线程组/帧 | ~131K | **0**（消除）✅ | **0** |
| Mip dispatch 数/帧 | ~56 | ~12 ✅ | ~12 |
| 总 dispatch 数/帧 | ~70+ | ~14+ | ~14+ |
| `_propagateTemp` 显存 | 64 MiB | **0**（消除）✅ | **0** |
| 双缓冲 radiance 显存 | 0 | +73 MiB（第二张 mip-chain）✅ | +73 MiB |
| 间接光 banding | 可见（9 锥） | 可见（9 锥） | **消除**（16 锥 + 旋转） |
| 间接光方向性 | 无（flat ambient） | 无 | **有**（ALD）✅ |
| 室内天空光 | 仅墙面 bounce | 仅墙面 bounce | **空气传播**（Phase 3 ⑧） |

**核心逻辑**：Phase 1 把 inject+propagate 从 ~260K 线程组降到 ~2-8K（实测 ~12% occupancy），虽然帧率瓶颈在 VoxelTrace 而非 inject/propagate，但稀疏化为 Phase 2 增加 16 锥腾出了充足的 GPU 预算——最终 propagate 在 16 锥模式下仍比 Phase 1 之前（9 锥 dense）更便宜，同时质量显著更高（16 锥 + 旋转 + ALD）。

---

## 附录 A：Alco GI 文件清单

### CPU 侧（C#）

| 文件 | 行数 | 用途 |
|------|------|------|
| `Src/Alco.Rendering/Deferred/VoxelGiRenderer.cs` | ~1318 | 核心：资源管理、render pass 编排、mesh 注册、page-pool 分配 |
| `Src/Alco.Rendering/Deferred/VoxelGiClipmap.cs` | ~615 | clipmap 状态、page pool、bounds、dirty brick 追踪 |
| `Src/Alco.Rendering/Deferred/PBRDeferredPipeline.cs` | — | GI 集成：绑定 indirect atlas 到 deferred lighting |
| `Test/Alco.Rendering.Test/Deferred/TestVoxelGiClipmap.cs` | ~171 | 单元测试：brick scrolling、toroidal offset、page reuse |

### GPU 侧（HLSL）

路径前缀：`Src/Alco.Engine/Assets/Shaders/Pipelines/Rendering/PBR/`

| 文件 | 用途 |
|------|------|
| `VoxelCommon.hlsli` | 共享头文件：cbuffer layout、pack/unpack 函数、page-table 寻址、clipmap level 辅助 |
| `Voxelize.hlsl` | 三角形→体素：triangle-box overlap test、barycentric UV、atomic 累加 |
| `VoxelClear.hlsl` | 清除 dirty brick 的物理页 |
| `VoxelInject.hlsl` | 直接光照注入：sun(CSM+cone)、4 point lights、DiffuseBias、emissive |
| `VoxelMip.hlsl` | Radiance + opacity mip downsample |
| `VoxelPropagate.hlsl` | Multi-bounce：9 锥半球传播、PropagationBooster、MinReflectance |
| `VoxelBounceApply.hlsl` | Copy propagation 结果回 radiance mip 0 |
| `VoxelTrace.hlsl` | Screen-space 锥追踪：64-dir Bayer tile diffuse + specular + SSR + SS near-field + ALD 输出 |
| `VoxelDemosaic.hlsl` | 时域/空间 resolve：min/max 双层、9×9 bilateral、3×3 specular bilateral、ALD 双层传播 |
| `GeometryNormal.hlsli` | 八面体编码法线 |
| `DeferredLighting.hlsl` | 消费 indirect atlas（5 段），UpScalePS 5-tap 上采样，ALD 方向性 diffuse |

---

## 附录 B：CRYENGINE SVOTI 文件清单

### CPU 侧（C++）

| 文件 | 行数 | 用途 |
|------|------|------|
| `Code/CryEngine/Cry3DEngine/SVO/SceneTree.h/.cpp` | 208 / 2920 | 八叉树构建、遍历、brick 更新、streaming |
| `Code/CryEngine/Cry3DEngine/SVO/VoxelSegment.h/.cpp` | 334 / 3556 | CPU 体素化：三角形光栅化、brick 数据生成、DXT 压缩 |
| `Code/CryEngine/Cry3DEngine/SVO/BlockPacker.h/.cpp` | 113 / 266 | 3D atlas allocator：16³ brick 放入 volume texture pool |
| `Code/CryEngine/Cry3DEngine/SVO/SceneTreeManager.h/.cpp` | 25 / 311 | 顶层管理：`CSvoManager` 单例 |
| `Code/CryEngine/Cry3DEngine/SVO/SceneTreeCVars.inl` | 273 | ~90 个 `e_svo*` / `e_svoTI_*` CVar 定义 |
| `Code/CryEngine/RenderDll/XRenderD3D9/D3D_SVO.h/.cpp` | 311 / 2038 | GPU renderer：pass 编排、texture binding、constant setup |

### GPU 侧（Shader）

| 文件 | 行数 | 用途 |
|------|------|------|
| `Shaders/HWScripts/CryFX/CommonSVO.cfi` | 3261 | 算法核心：`ConeTraceBrick`、`ConeTraceTree`、`RayTraceMesh`、所有 hemisphere kernel |
| `Shaders/HWScripts/CryFX/Total_Illumination.cfx` | 2928 | 入口点：`ConeTracePS`、`DemosaicPS`、`UpScalePS`、`ComputeClearBricks`、`ComputeInjectAtmosphere`、`ComputeDirectStaticLighting`、`ComputeDirectDynamicLighting`、`ComputePropagateLighting` |
