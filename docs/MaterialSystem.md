# Material System

Alco 的材质系统是引擎基础设施，由三层组成：

- **`MaterialComposer`(Alco.Rendering)** — 引擎级 slang 组合基础设施，与具体渲染管线无关。
- **`MaterialCompiler` + `MaterialAsset` + `IMaterialPass`(Alco.Rendering)** — 管线无关的材质编译器：数据资产 + 开放 pass 注册表 + 每 pass 编译（无状态工厂，产物由调用方持有）。
- **管线家族(World3D / 未来的 2D / 游戏自己的管线）** — 派生资产类型 + surface 契约 + pass 实现（`.amat` 的 `$type` 判别靠程序集扫描自动发现，无注册）。

目标：游戏侧不修改引擎就能定义新材质（surface）和新 pass（渲染设施），双方各自独立演进，由 slang 的泛型特化在编译期组合。2D 还是 3D、G-buffer 里有哪些元素，都是管线家族自己的事，基础设施不耦合其中任何一个。

## 分层

```
游戏侧 surface（Materials/pbr-standard.slang 或游戏自己的 .slang）
        │  public struct Surface : ISurface { override ... }
        ▼
MaterialCompiler, Alco.Rendering（pass 注册表 + (asset, pass) 纯工厂 + 纹理槽/参数块打包）
        │  RegisterPass(IMaterialPass)   —— 接口实现，不是回调
        ▼
MaterialComposer.ComposeGraphics / ComposeCompute（slang 泛型特化 + 链接 + 反射）
        ▼
渲染设施（GBufferRenderer / ShadowRenderer / RGNode_Forward / RGNode_VoxelGI / 游戏自定义）
```

## 材质资产（`MaterialAsset`, Alco.Rendering)

`MaterialAsset` 是数据-only 的材质描述（.amat 的运行时形态，jsonc 直接反序列化，无 DTO 层），只携带管线无关概念：

| 属性 | 含义 |
| --- | --- |
| `Name` | 材质名（缺省取文件名） |
| `Surface` | surface 的 `ShaderLibrary` 引用；null 选编译器的默认 surface |
| `Defines` | surface 的特化 define |
| `Textures` | 纹理槽 → `Texture2D`（反序列化时即经资产系统加载；未设置的槽走兜底策略） |
| `Parameters` | surface `[MaterialParams]` 块的成员名 → `Vector4` |
| `GetTextureFallback(slot)` | **虚方法**，槽位的兜底纹理策略（白/黑/flat normal)，基类恒白 |

管线家族数据（PBR 因子、alpha 路由……）不在基类上——派生类携带，pass 通过 `IMaterialPass<TAsset>` 静态地拿到派生类型。World3D 的派生资产是 `PbrMaterialAsset`(glTF metallic-roughness 因子 + `AlphaMode`/`DoubleSided` 路由字段 + PBR 兜底策略：`normal*` → flat normal,`emissive*` → 黑，其余 → 白）。

### `ShaderLibrary`：类型化的模块引用

`ShaderLibrary`(Alco.Rendering）是 shader 模块的类型化引用，材质与 pass 以它互相指认，替代过去的字符串路径：

- 按模块名驻留：`ShaderSystem.GetLibrary(name)` 一名一实例；创建时校验模块可解析（与编译相同的探测路径），但**不编译**。
- 热重载安全：引用不持有编译产物，模块系统重建后按名重解析。
- 命名编译器无关——用户看到的是"shader 模块"，不暴露内部是 slang module 还是别的什么；未来换编译器时内部表示可以变，引用不变。

### `.amat` 文件格式

jsonc 直接反序列化成 `MaterialAsset`：加载器 `AssetLoaderMaterialAsset`（在 **Alco.Engine**,GameEngine 默认 loader）用引擎的 `PolymorphicJsonTypeResolver` 做多态——`"$type"` 判别值是 **CLR 全名**，派生类型靠程序集扫描发现，用户写完一个类型即可在 jsonc 里使用，零注册：

```jsonc
{
    "$type": "Alco.World3D.PbrMaterialAsset",   // 省略 → 解析为 MaterialAsset 基类
    "version": "1.0",
    "name": "mossy_rock",                        // 省略 → 取文件名
    "surface": "mossy_rock",                     // surface 模块名(ShaderLibrary);省略 → 编译器默认 surface
    "defines": ["MOSS_ANIMATE"],
    "textures": {
        // 槽名 → 纹理资产路径,反序列化时即加载;路径写错/文件缺失在 .amat 加载期报错
        "albedoTexture": "Textures/mossy_albedo.png"
    },
    "parameters": {
        // [MaterialParams] 成员名 → Vector4,三种形态:
        "pulseSpeed": 2.0,                              // 数字:广播到各分量
        "pulseColor": { "r": 1, "g": 0.5, "b": 0.25 },  // 对象:xyzw 或 rgba 键,缺省分量补 0
        "bandColor": "#FF8040"                          // hex 颜色(#RRGGBB / #RRGGBBAA),按字节值归一化到 [0,1]
    }
}
```

- **未知字段报错**(`UnmappedMemberHandling.Disallow`)——改名/删掉的旧字段、拼写错误不会被静默忽略。
- PBR 因子（`baseColorFactor`/`emissiveFactor` 等）与 `parameters` 共享同一套 vector 形态（对象/数字/hex)。
- 枚举（`alphaMode`）走 `JsonStringEnumConverter`，非法值报错。
- 纹理是加载即解析的强类型引用：`""`/`null` 槽位视为未设置；缺失文件直接加载失败（`AssetLoadException`)，不再静默回退白图。
- `surface` 写**模块名**（源文件里的 `module` 声明），不是文件路径；未知模块名同样在加载期报错。
- 版本校验（`AssetJson.ValidateVersion`）与名称回填在加载器里做。

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

> 契约是管线家族自己的：World3D 的 `ISurface` 住在这里；2D 管线可以定义完全不同的 surface 契约（比如只有 `GetSpriteColor`),`MaterialCompiler`/`MaterialComposer` 不依赖契约的具体形状——它们只按 `ShaderLibrary` 组合、按反射读槽位和参数块。

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
    ShaderLibrary Template { get; }       // pass 模板(渲染设施从自己持有的 ShaderSystem 取)
    GraphicsMaterial CreateMaterial(MaterialAsset asset, Shader shader);  // pass mandated 状态
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
- `Accepts` 拒绝的材质**不编译**该 pass（如 OPAQUE 材质不编 glass),`TryCompile` 返回 null，直接 `Compile` 抛 `InvalidDataException`。
- 同 surface module 的不同特化（如 shadow 的 AlphaTest true/false）是独立缓存条目。
- 重复注册同一个 pass id 抛 `ArgumentException`。
- 编译器构造时收一个可选的**默认 surface**(`ShaderLibrary`，如 World3D 的 PbrStandard):`World3DAssetPipeline.CreateMaterialCompiler(rendering)` 一步到位；不带默认的编译器要求每个材质都指名 surface。

## 编译产物的所有权与生命周期

`MaterialCompiler` 是无状态的 (asset, pass) 工厂：每次 `Compile` 新编一份材质，**调用方持有**——引擎资源原则（不好控制生命周期就 GC 自动回收，能确保安全就手动 dispose）在这里的落法：

- **共享靠调用方**：同一资产被多个 mesh 使用时，由消费方（场景/模型的材质表）编译一次、把同一个 `GraphicsMaterial` 分发给各 renderable，而不是靠编译器里的中央缓存。渲染器经 renderable 收材质，自己不编译。
- **回收靠 GC**：编译器不持有任何 per-asset 状态；场景卸载 = 弃掉材质表 = 资产、编译产物、参数 buffer 全链路由 GC 回收（`AssetSystem` 弱句柄 + `BaseGPUObject`/`AutoDisposable` finalizer)。手动 `Dispose` 只适用于能证明独占的部分：材质自身（其参数集不逃逸）由场景 teardown 释放；绑进槽位的值（纹理、参数 buffer）可被外部经参数集访问器（`TryGetBuffer` 等）取得，是生命周期不确定的共享引用——一律不随材质显式释放，由 finalizer 在真正无引用时回收。
- **流式不改变绑定**：纹理流式是"按 header 预创建 + 内容原位上传"(`RenderingSystem.CreateTexture2DStreaming`)，纹理对象身份从创建即终态、从不替换，所以材质与管线不需要任何流式适配——加载方在资产完成前拿到纹理对象，直接填进 `Textures` 表。
- **热重载 = 新资产实例**：旧实例的编译产物随旧实例被 GC，消费方用新实例重新 `Compile`；编译器没有 Invalidate。

## 纹理槽规则

- surface 里的 `Texture2D _albedoTexture` → 槽名 **`albedoTexture`**（去前导下划线，大小写敏感）。
- 资产的 `Textures` 在加载期即解析成 `Texture2D`，资产到此完整，**不认识流式**。glTF 场景加载同理：loader 在场景返回前实现所有纹理（外部文件内容原位异步上传），由适配器直接填进资产描述符的 `Textures`；图像缺失或解码失败的槽留空，走资产兜底策略。
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

- 发现靠标记不靠名字：`GetParamsLayouts` 枚举模块里所有带标记的 cbuffer，从反射读每个成员的类型和字节偏移；`PackParamsBuffers` 把 `Parameters` 按成员名跨块分发、逐块打包成 GPU buffer，按块名绑定。参数值是 `Vector4`，每个成员读取自己宽度的前导分量（标量读 x,float3 读 xyz)，多余分量忽略。
- 未标记的块天然排除——surface 重声明的引擎数据块（如 `_globalRenderData`）不需要进排除名单。
- 块里可以混声明纹理/sampler 成员（自描述资源块），只有标量/向量 float 成员参与参数打包；标记块一个 float 成员都没有 → `NotSupportedException`。
- 快速失败：参数名对不上任何块的成员 → `InvalidDataException`（列出有效成员）；同一成员名出现在两个块 → 跨块歧义报错；传了 `Parameters` 但 surface 没标记任何参数块 → `InvalidDataException`。

## 值特化优先于 define

shader 内行为分支用 slang 值特化而不是字符串 define:

```slang
public MainPOut MainPS<T : ISurface>(MainVOut v) where let AlphaTest : bool { ... }
```

C# 侧 pass 的 `IMaterialPass.GetValueSpecArgs` 返回 `["true"]` / `["false"]`,composer 以 `let AlphaTest : bool = true` 实例化特化类型，特化参数进程序缓存标识。define 只留给真正需要整个 module 级文本开关的组合期场景（如 `MaterialAsset.Defines`、`SHADOW_CUTOUT` 切 varying 结构形状）——它们在组合前烘进材质键，运行时不存在 define 变体轴。

## GI 体素化（compute feed)

`RGNode_VoxelGI` 不是单一手写 shader:`RegisterMesh(mesh, stride, bounds, materialAsset, textures)` 按材质组合 `voxelize` 模板（compute,`IVoxelFeedSurface`),surface 的纹理槽/参数块规则与 graphics pass 完全一致，兜底纹理由 `MaterialCompiler.ResolveFallbackTexture(asset, resource)` 按资产策略解析。同材质的多 mesh 共享一个 feed;feed 是 per-asset 派生状态，feed 表（`ConditionalWeakTable`）弱持有——寿命跟随资产，不被这个长寿命节点钉住，存活的注册项自己持有自己的 feed。compute pass 不进 `IMaterialPass` 注册表——它直用 `MaterialComposer`。

## 接入一个新管线家族（2D / Game / 游戏自定义）

1. **资产**：从 `MaterialAsset` 派生（如 `SpriteMaterialAsset`)，携带家族数据，重写 `GetTextureFallback` 定义家族兜底策略。类型写完即可在 .amat 里用 `"$type"` 全名引用，无需注册。
2. **surface 契约**：写一个契约 .slang（细粒度接口 + 全默认实现）；surface 若用参数块，`import alco_rendering_core;` 拿 `[MaterialParams]`。
3. **pass**：每个渲染设施实现 `IMaterialPass<MyAsset>`，在自己的构造函数里 `RegisterPass(this)`；模板 module 是磁盘上的 .slang 文件，`Template` 返回 `shaderSystem.GetLibrary(...)`。
4. **编译器**:`new MaterialCompiler(rendering, defaultSurface)`（默认 surface 是 `ShaderLibrary`，可选）。

两侧都不需要改引擎代码。

## 给游戏侧的清单（World3D 家族内）

新增一个材质效果：

1. 写一个 .slang:`import alco_world3d_surface; public struct Surface : ISurface { override ... }`（文件名 kebab-case,module 名下划线）。
2. 在模块作用域声明需要的纹理/参数块（space2，自描述；参数块记得 `import alco_rendering_core;`)。
3. `.amat` 里 `"surface": "my_shader"`（模块名），或代码里 `PbrMaterialAsset { Surface = shaderSystem.GetLibrary("my_shader") }`;pass 自动可用，纹理槽按名绑。

新增一个渲染设施（新 pass):

1. 写模板 module（泛型入口点）。
2. 设施实现 `IMaterialPass<PbrMaterialAsset>`，构造函数里 `RegisterPass(this)`。
3. 用 `Get/TryGet` 拿材质画。
