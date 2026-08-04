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

### 3.5 Dual-kernel（radiance + opacity）

CE5 的 `ConeTracePS` 用双 kernel——一个采样 radiance，一个采样 opacity（压低仰角增加 AO）：

```hlsl
// CE5 Total_Illumination.cfx:1522-1551
float3 kern = GetDiffuseKernel(tiling, i);          // radiance 方向
float3 kernOpa = GetDiffuseKernel(tiling, i, false); // opacity 方向
kernOpa.z -= SvoParamsCommon.y;                       // 压低仰角
kern = lerp(kernOpa, kern, saturate(transmittance * 4)); // 透明度混合
```

Alco 用单 kernel。

**可移植性**：✅ 可以直接移植。多采一组方向，lerp 混合即可。需要为 cbuffer 增加一个 `DiffuseSpreading` 参数。

---

### 3.6 ALD（Average Light Direction）输出

CE5 的 diffuse trace 输出 ALD——方向加权平均：
```hlsl
// CE5 Total_Illumination.cfx:1635-1636
vALD.xyz += r.direction * brightness;  // 方向加权
vALD.w += brightness;                  // 亮度累加
```
然后在材质 shader 里用 ALD 做有方向的 diffuse 响应（间接光不是纯 flat ambient）。

Alco 当前输出 `float4(diffuse, visibility)`，没有 ALD。

**可移植性**：✅ 可以移植。需要：
1. 修改 trace atlas 格式（diffuse 段从 rgb+visibility 改为 ald.xyz+ald.w）
2. 修改 `DeferredLighting.hlsl` 采样 ALD 做方向性 diffuse
3. 在 `VoxelDemosaic.hlsl` 的双层 gather 中也传播 ALD

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

### 能移植但需要引擎侧基础设施的

- ⚠️ RSM 注入（需要 Reflective Shadow Map 管线）
- ⚠️ Tiled lights（需要 tiled light culling）
- ⚠️ Portal 灯变形（需要 portal 系统）
- ⚠️ ALD 输出（需要修改 atlas 格式和 deferred lighting shader）
- ⚠️ Dual-kernel opacity（多一组 kernel 方向 + cbuffer 参数）
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

| 优先级 | 改进项 | 预期收益 | 工作量 |
|--------|--------|----------|--------|
| **1** | 传播锥从 9 增到 32 + 随机旋转 | ★★★★★ | 中（kernel 表 + 旋转矩阵 + 性能调优） |
| **2** | ALD 输出 | ★★★★☆ | 中（atlas 格式 + deferred lighting 改造） |
| **3** | Air 体素传播 | ★★★☆☆ | 中（体素数据格式扩展 + 传播 pass 修改） |
| **4** | Multi-bounce 双缓冲 | ★★☆☆☆ | 低（多一张 Texture3D，去掉 copy pass） |
| **5** | Desaturation 控制 | ★★☆☆☆ | 低（一个 lerp） |
| **6** | Dual-kernel opacity | ★★★☆☆ | 中（kernel 扩展 + cbuffer 参数） |
| **7** | RSM 注入 | ★★★★★ | 高（需要 RSM 管线） |
| **8** | Tiled lights | ★★★☆☆ | 高（需要 tiled light 基础设施） |

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
| `VoxelTrace.hlsl` | Screen-space 锥追踪：64-dir Bayer tile diffuse + specular + SSR + SS near-field |
| `VoxelDemosaic.hlsl` | 时域/空间 resolve：min/max 双层、9×9 bilateral、3×3 specular bilateral |
| `GeometryNormal.hlsli` | 八面体编码法线 |
| `DeferredLighting.hlsl` | 消费 indirect atlas（3 段），UpScalePS 5-tap 上采样 |

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
