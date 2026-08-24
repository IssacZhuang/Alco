# Material System

Alco 的材质系统是引擎基础设施，由三层组成：

- **`MaterialComposer`(Alco.Rendering)** — 引擎级 slang 组合基础设施，与具体渲染管线无关。
- **`MaterialCompiler` + `MaterialAsset` + `IMaterialPass`(Alco.Rendering)** — 管线无关的材质编译器：数据资产 + 开放 pass 注册表 + 每 pass 编译/缓存。
- **管线家族(World3D / 未来的 2D / 游戏自己的管线）** — 派生资产类型 + surface 契约 + pass 实现 + `.amat` schema 注册。

目标：游戏侧不修改引擎就能定义新材质（surface）和新 pass（渲染设施），双方各自独立演进，由 slang 的泛型特化在编译期组合。2D 还是 3D、G-buffer 里有哪些元素，都是管线家族自己的事，基础设施不耦合其中任何一个。

## 分层

```
游戏侧 surface（Materials/pbr-standard.slang 或游戏自己的 .slang）
        │  public struct Surface : ISurface { override ... }
        ▼
MaterialCompiler, Alco.Rendering（pass 注册表 + 每 pass 编译/缓存 + 纹理槽/参数块打包）
        │  RegisterPass(IMaterialPass)   —— 接口实现，不是回调
        ▼
MaterialComposer.ComposeGraphics / ComposeCompute（slang 泛型特化 + 链接 + 反射）
        ▼
渲染设施（GBufferRenderer / ShadowRenderer / RGNode_Forward / RGNode_VoxelGI / 游戏自定义）
```

## 材质资产（`MaterialAsset`, Alco.Rendering)

`MaterialAsset` 是数据-only 的材质描述（.amat 的运行时形态），只携带管线无关概念：

| 属性 | 含义 |
| --- | --- |
| `Name` | 材质名（缺省取文件名） |
| `SurfaceShader` | surface 模块的资产路径；null 选编译器的默认 surface |
| `Defines` | surface 的特化 define |
| `Textures` | 纹理槽 → 路径（解析期不加载） |
| `Parameters` | surface `[MaterialParams]` 块的成员名 → 1-4 个 float |
| `GetTextureFallback(slot)` | **虚方法**，槽位的兜底纹理策略（白/黑/flat normal)，基类恒白 |

管线家族数据（PBR 因子、alpha 路由……）不在基类上——派生类携带，pass 通过 `IMaterialPass<TAsset>` 静态地拿到派生类型。World3D 的派生资产是 `PbrMaterialAsset`(glTF metallic-roughness 因子 + `AlphaMode`/`DoubleSided` 路由字段 + PBR 兜底策略：`normal*` → flat normal,`emissive*` → 黑，其余 → 白）。

### `.amat` 文件与 `type` 判别

文件 schema 与资产类同构：`MaterialAssetJson`(Alco.Rendering）是管线无关基 schema,**管线家族派生 DTO 并注册判别值**:

```csharp
MaterialAssetJson.RegisterType<PbrMaterialAssetJson>("pbr");   // World3DAssetPipeline.RegisterLoaders 里
```

文件带 `"type": "pbr"` → 解析成 `PbrMaterialAsset`；不带 `type` → 基 schema（只有通用字段）。重复注册同一映射是 no-op，冲突注册抛错；未注册的 type 报错并列出已注册值。加载器 `AssetLoaderMaterialAsset` 在 **Alco.Engine**(GameEngine 默认 loader)，解析不碰 GPU、不加载纹理。

## Surface 契约（`Libs/alco-world3d-surface.slang`,module `alco_world3d_surface`)

材质是一个实现 `ISurface` 的 struct。`ISurface` 聚合六个细粒度接口，**全部默认实现**——新 surface 可以从空 struct 开始，只重写自己关心的部分：

| 接口 | 方法 | 默认行为 |
| --- | --- | --- |
| `IVertexSurface` | `ModifyVertex(inout worldPos, inout normalWS, uv)` | 恒等（实例变换后、投影前调用，所有 pass 一致） |
| `IAlbedoSurface` | `GetBaseColor(SurfaceInput)` | `baseColorFactor` |
| `INormalSurface` | `GetNormalTS(SurfaceInput)` | `(0,0,1)`（切线空间，模板负责 TBN 提升到世界空间） |
| `IMaterialPropsSurface` | `GetMetallicRoughnessAO(SurfaceInput)` | `metallicRoughnessAO` 因子 |
| `IEmissiveSurface` | `GetEmissive(SurfaceInput)` | `emissiveFactor` |
| `IVoxelFeedSurface` | `GetVoxelBaseColor/GetVoxelEmissive(SurfaceInput, lod)` | 纯因子（compute 域，必须 `SampleLevel` 显式 lod) |

覆盖接口方法**必须写 `override`**（否则 slang 报 error 36107)——surface 作者的意图显式化。pass 不调用的方法在特化后是死代码，零开销。

`SurfaceInput` 由模板填充：`worldPos / normalWS / tangentWS / uv` + 每实例因子（`baseColorFactor / metallicRoughnessAO / emissiveFactor / alphaCutoff`)。需要时间等全局数据时，surface 在自己模块作用域声明 engine 约定的 `_globalRenderData` cbuffer。

surface 声明的资源是它的**自描述数据需求**：模块全局作用域的 set-scoped cbuffer 块（space2，材质集），引擎按成员名绑定，未绑的走**资产自己的兜底策略**(`GetTextureFallback`)。内置示例是 `Materials/pbr-standard.slang` 的 `Surface`(glTF metallic-roughness 四纹理槽 + 因子相乘）。

> 契约是管线家族自己的：World3D 的 `ISurface` 住在这里；2D 管线可以定义完全不同的 surface 契约（比如只有 `GetSpriteColor`),`MaterialCompiler`/`MaterialComposer` 不依赖契约的具体形状——它们只按模块名组合、按反射读槽位和参数块。

## Pass 模板约定（`Pipelines/*.slang`)

一个 pass = 一个 slang module，自带**泛型入口点**：

```slang
[shader("vertex")] public MainVOut MainVS<T : ISurface>(MainVIn v) { ... }
[shader("fragment")] public MainPOut MainPS<T : ISurface>(MainVOut v) { ... }
```

组合 = composite + link-time specialization(`specialize(entryPoint, surfaceType)`)，没有字符串拼接的 wrapper shader。World3D 的内置 pass:

| pass id | 模板 module | 入口 | 消费的 surface 接口 | 引擎资源 |
| --- | --- | --- | --- | --- |
| `gbuffer` | `gbuffer` | MainVS/MainPS | 除体素化外全部 | camera(space0)+ instance(space1) |
| `shadow` | `shadow_depth` | 同上 | IVertexSurface(AlphaTest 时 + IAlbedoSurface) | light VP + instance;`MainPS<T> where let AlphaTest : bool` 值特化 |
| `rsm` | `rsm` | 同上 | IVertex + IAlbedo + INormal | camera(light VP)+ rsmData |
| `glass` | `glass` | 同上 | 同 gbuffer | camera + lightingData（前向玻璃） |
| `voxelize` | `voxelize` | `MainCS<T : IVoxelFeedSurface>` compute | IVoxelFeedSurface | `_data` + `_vertices/_indices/_attrOut/_pageTable` |

新管线可以注册自己的 pass——模板 module 只是磁盘上的一个 .slang 文件。

## IMaterialPass:开放注册，接口而非回调

pass 是一个**接口实现**，renderer/设施在自己的构造函数里注册自己：

```csharp
public interface IMaterialPass
{
    string Id { get; }                    // "gbuffer"、"shadow"……编译器内唯一
    string TemplateModule { get; }        // pass 模板 slang module 名
    GraphicsMaterial CreateMaterial(MaterialAsset asset, Shader shader);  // pass  mandated 状态
    IReadOnlyList<string>? GetValueSpecArgs(MaterialAsset asset) => null; // 可选，值特化参数
    bool Accepts(MaterialAsset asset) => true;                            // 可选，参与路由
}
```

泛型版本 `IMaterialPass<TAsset>` 让 pass 静态地收到自己家族的资产类型；基接口成员以**受检 cast** 转发——外家族资产 `Accepts` 直接 false,`CreateMaterial` 永远收不到错误类型（防御性 `InvalidDataException`)。

```csharp
public sealed class GBufferRenderer : ..., IMaterialPass<PbrMaterialAsset>
{
    public GBufferRenderer(RenderingSystem rendering, MaterialCompiler compiler)
    {
        ...
        compiler.RegisterPass(this);
    }

    GraphicsMaterial IMaterialPass<PbrMaterialAsset>.CreateMaterial(PbrMaterialAsset asset, Shader shader)
        => CreateMaterial(shader, asset.DoubleSided, $"{asset.Name}_gbuffer");

    bool IMaterialPass<PbrMaterialAsset>.Accepts(PbrMaterialAsset asset)
        => asset.AlphaMode != MeshAlphaMode.Blend;
}
```

- 每个 renderer 实现 pass 接口并在构造函数里注册；材质工厂（布局/缓冲）是 renderer 私有知识。
- `Accepts` 拒绝的材质**不编译**该 pass（如 OPAQUE 材质不编 glass),`TryGet` 返回 null，直接 `Get` 抛 `InvalidDataException`。
- 同 surface module 的不同特化（如 shadow 的 AlphaTest true/false）是独立缓存条目。
- 重复注册同一个 pass id 抛 `ArgumentException`。
- 编译器构造时收一个可选的**默认 surface 路径**（如 World3D 的 PbrStandard):`World3DAssetPipeline.CreateMaterialCompiler(rendering)` 一步到位；不带默认的编译器要求每个材质都指名 surface。

## 纹理槽规则

- surface 里的 `Texture2D _albedoTexture` → 槽名 **`albedoTexture`**（去前导下划线，大小写敏感）。
- `BindTextures(asset, { ["albedoTexture"] = tex })` 对持有该 asset 材质的所有 pass 批量绑纹。
- 未绑的槽走**资产自己的兜底策略**:`MaterialAsset.GetTextureFallback(slot)` 返回 `White/Black/FlatNormal`，编译器映射到 `RenderingSystem.TextureWhite/TextureBlack/TextureFlatNormal`。基类恒白；`PbrMaterialAsset` 按槽名前缀给 `normal*` → flat normal、`emissive*` → 黑。不同材质家族可以有不同的兜底策略，不需要编译器知道任何槽名约定。
- surface 资源在 **space2**(`MaterialCompiler.SurfaceResourceSet`),set-scoped cbuffer 块约定与引擎其它部分一致（`cbuffer _material : register(b0, space2)`);pass 模板的引擎资源占用低位集，互不冲突。

## 参数块规则

- surface 用 `[MaterialParams]` 标记自己的参数 cbuffer——**属性由引擎核心库 `alco_rendering_core` 声明**，用它的 surface 模块 `import alco_rendering_core;`。块名自由，可以有多块：

  ```slang
  import alco_rendering_core;

  [MaterialParams]
  cbuffer PulseParams : register(b1, space2)
  {
      float pulseSpeed;
      float3 pulseColor;
  }
  ```

- 发现靠标记不靠名字：`GetParamsLayouts` 枚举模块里所有带标记的 cbuffer，从反射读每个成员的类型和字节偏移；`PackParamsBuffers` 把 `Parameters` 按成员名跨块分发、逐块打包成 GPU buffer，按块名绑定。
- 未标记的块天然排除——surface 重声明的引擎数据块（如 `_globalRenderData`）不需要进排除名单。
- 块里可以混声明纹理/sampler 成员（自描述资源块），只有标量/向量 float 成员参与参数打包；标记块一个 float 成员都没有 → `NotSupportedException`。
- 快速失败：参数名对不上任何块的成员 → `InvalidDataException`（列出有效成员）；同一成员名出现在两个块 → 跨块歧义报错；传了 `Parameters` 但 surface 没标记任何参数块 → `InvalidDataException`。

## 值特化优先于 define

shader 内行为分支用 slang 值特化而不是字符串 define:

```slang
public MainPOut MainPS<T : ISurface>(MainVOut v) where let AlphaTest : bool { ... }
```

C# 侧 pass 的 `IMaterialPass.GetValueSpecArgs` 返回 `["true"]` / `["false"]`,composer 以 `let AlphaTest : bool = true` 实例化特化类型，特化参数进程序缓存标识。define 只留给真正需要整个 module 级文本开关的场景（如 `MaterialAsset.Defines`、sprite 的 `REPEATED`)。

## GI 体素化（compute feed)

`RGNode_VoxelGI` 不是单一手写 shader:`RegisterMesh(mesh, stride, bounds, materialAsset, textures)` 按材质组合 `voxelize` 模板（compute,`IVoxelFeedSurface`),surface 的纹理槽/参数块规则与 graphics pass 完全一致，兜底纹理由 `MaterialCompiler.ResolveFallbackTexture(asset, resource)` 按资产策略解析。同材质的多 mesh 共享一个 feed。compute pass 不进 `IMaterialPass` 注册表——它直用 `MaterialComposer`。

## 接入一个新管线家族（2D / Game / 游戏自定义）

1. **资产**：从 `MaterialAsset` 派生（如 `SpriteMaterialAsset`)，携带家族数据，重写 `GetTextureFallback` 定义家族兜底策略。
2. **schema**：派生 `MaterialAssetJson`，`MaterialAssetJson.RegisterType<MyJson>("my")` 注册判别值（在该家族的 asset-pipeline 注册里调一次）。
3. **surface 契约**：写一个契约 .slang（细粒度接口 + 全默认实现）；surface 若用参数块，`import alco_rendering_core;` 拿 `[MaterialParams]`。
4. **pass**：每个渲染设施实现 `IMaterialPass<MyAsset>`，在自己的构造函数里 `RegisterPass(this)`；模板 module 是磁盘上的 .slang 文件。
5. **编译器**:`new MaterialCompiler(rendering, defaultSurfacePath)`（默认 surface 可选）。

两侧都不需要改引擎代码。

## 给游戏侧的清单（World3D 家族内）

新增一个材质效果：

1. 写一个 .slang:`import alco_world3d_surface; public struct Surface : ISurface { override ... }`（文件名 kebab-case,module 名下划线）。
2. 在模块作用域声明需要的纹理/参数块（space2，自描述；参数块记得 `import alco_rendering_core;`)。
3. `PbrMaterialAsset { SurfaceShader = myShader }`,pass 自动可用；`BindTextures` 绑纹。

新增一个渲染设施（新 pass):

1. 写模板 module（泛型入口点）。
2. 设施实现 `IMaterialPass<PbrMaterialAsset>`，构造函数里 `RegisterPass(this)`。
3. 用 `Get/TryGet` 拿材质画。
