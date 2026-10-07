//! Single-point mapping between Alco C ABI enum values and `wgpu-types` enums:
//! raw `u32` values are validated into the ABI enums in `crate::abi`
//! (`TryFrom<u32>`), then mapped with exhaustive variant matches.

use crate::abi::{Status, NONE};
use crate::entry::set_error;
use wgpu_types as wgt;

/// `abi::PixelFormat` (0..95) -> `wgt::TextureFormat`.
pub(crate) fn pixel_format(v: u32) -> Result<wgt::TextureFormat, Status> {
    use crate::abi::PixelFormat as P;
    use wgt::TextureFormat as F;
    let p = P::try_from(v).map_err(|v| invalid_enum_what("pixel format", v))?;
    let f = match p {
        P::Undefined => return Err(invalid_enum_what("pixel format", v)),
        P::R8Unorm => F::R8Unorm,
        P::R8Snorm => F::R8Snorm,
        P::R8Uint => F::R8Uint,
        P::R8Sint => F::R8Sint,
        P::R16Uint => F::R16Uint,
        P::R16Sint => F::R16Sint,
        P::R16Float => F::R16Float,
        P::RG8Unorm => F::Rg8Unorm,
        P::RG8Snorm => F::Rg8Snorm,
        P::RG8Uint => F::Rg8Uint,
        P::RG8Sint => F::Rg8Sint,
        P::R32Float => F::R32Float,
        P::R32Uint => F::R32Uint,
        P::R32Sint => F::R32Sint,
        P::RG16Uint => F::Rg16Uint,
        P::RG16Sint => F::Rg16Sint,
        P::RG16Float => F::Rg16Float,
        P::RGBA8Unorm => F::Rgba8Unorm,
        P::RGBA8UnormSrgb => F::Rgba8UnormSrgb,
        P::RGBA8Snorm => F::Rgba8Snorm,
        P::RGBA8Uint => F::Rgba8Uint,
        P::RGBA8Sint => F::Rgba8Sint,
        P::BGRA8Unorm => F::Bgra8Unorm,
        P::BGRA8UnormSrgb => F::Bgra8UnormSrgb,
        P::RGB10A2Uint => F::Rgb10a2Uint,
        P::RGB10A2Unorm => F::Rgb10a2Unorm,
        P::RG11B10Ufloat => F::Rg11b10Ufloat,
        P::RGB9E5Ufloat => F::Rgb9e5Ufloat,
        P::RG32Float => F::Rg32Float,
        P::RG32Uint => F::Rg32Uint,
        P::RG32Sint => F::Rg32Sint,
        P::RGBA16Uint => F::Rgba16Uint,
        P::RGBA16Sint => F::Rgba16Sint,
        P::RGBA16Float => F::Rgba16Float,
        P::RGBA32Float => F::Rgba32Float,
        P::RGBA32Uint => F::Rgba32Uint,
        P::RGBA32Sint => F::Rgba32Sint,
        P::Stencil8 => F::Stencil8, // unsupported format; wgpu still defines it
        P::Depth16Unorm => F::Depth16Unorm,
        P::Depth24Plus => F::Depth24Plus,
        P::Depth24PlusStencil8 => F::Depth24PlusStencil8,
        P::Depth32Float => F::Depth32Float,
        P::Depth32FloatStencil8 => F::Depth32FloatStencil8,
        P::BC1RGBAUnorm => F::Bc1RgbaUnorm,
        P::BC1RGBAUnormSrgb => F::Bc1RgbaUnormSrgb,
        P::BC2RGBAUnorm => F::Bc2RgbaUnorm,
        P::BC2RGBAUnormSrgb => F::Bc2RgbaUnormSrgb,
        P::BC3RGBAUnorm => F::Bc3RgbaUnorm,
        P::BC3RGBAUnormSrgb => F::Bc3RgbaUnormSrgb,
        P::BC4RUnorm => F::Bc4RUnorm,
        P::BC4RSnorm => F::Bc4RSnorm,
        P::BC5RGUnorm => F::Bc5RgUnorm,
        P::BC5RGSnorm => F::Bc5RgSnorm,
        P::BC6HRGBUfloat => F::Bc6hRgbUfloat,
        P::BC6HRGBFloat => F::Bc6hRgbFloat,
        P::BC7RGBAUnorm => F::Bc7RgbaUnorm,
        P::BC7RGBAUnormSrgb => F::Bc7RgbaUnormSrgb,
        P::ETC2RGB8Unorm => F::Etc2Rgb8Unorm,
        P::ETC2RGB8UnormSrgb => F::Etc2Rgb8UnormSrgb,
        P::ETC2RGB8A1Unorm => F::Etc2Rgb8A1Unorm,
        P::ETC2RGB8A1UnormSrgb => F::Etc2Rgb8A1UnormSrgb,
        P::ETC2RGBA8Unorm => F::Etc2Rgba8Unorm,
        P::ETC2RGBA8UnormSrgb => F::Etc2Rgba8UnormSrgb,
        P::EACR11Unorm => F::EacR11Unorm,
        P::EACR11Snorm => F::EacR11Snorm,
        P::EACRG11Unorm => F::EacRg11Unorm,
        P::EACRG11Snorm => F::EacRg11Snorm,
        P::ASTC4x4Unorm => F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC4x4UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC5x4Unorm => F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC5x4UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC5x5Unorm => F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC5x5UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC6x5Unorm => F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC6x5UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC6x6Unorm => F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC6x6UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC8x5Unorm => F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC8x5UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC8x6Unorm => F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC8x6UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC8x8Unorm => F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC8x8UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC10x5Unorm => F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC10x5UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC10x6Unorm => F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC10x6UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC10x8Unorm => F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC10x8UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC10x10Unorm => F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC10x10UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC12x10Unorm => F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC12x10UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        P::ASTC12x12Unorm => F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::Unorm,
        },
        P::ASTC12x12UnormSrgb => F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::UnormSrgb,
        },
    };
    Ok(f)
}

/// Records an `INVALID_ARGUMENT` diagnostic for an unmapped enum value.
pub(crate) fn invalid_enum_what(what: &str, value: u32) -> Status {
    set_error(
        Status::INVALID_ARGUMENT,
        format!("invalid {what} value {value}"),
    );
    Status::INVALID_ARGUMENT
}

/// `wgt::TextureFormat` -> `abi::PixelFormat`; cast at the ABI boundary.
pub(crate) fn pixel_format_to_abi(f: wgt::TextureFormat) -> crate::abi::PixelFormat {
    use crate::abi::PixelFormat as P;
    use wgt::TextureFormat as F;
    match f {
        F::R8Unorm => P::R8Unorm,
        F::R8Snorm => P::R8Snorm,
        F::R8Uint => P::R8Uint,
        F::R8Sint => P::R8Sint,
        F::R16Uint => P::R16Uint,
        F::R16Sint => P::R16Sint,
        F::R16Float => P::R16Float,
        F::Rg8Unorm => P::RG8Unorm,
        F::Rg8Snorm => P::RG8Snorm,
        F::Rg8Uint => P::RG8Uint,
        F::Rg8Sint => P::RG8Sint,
        F::R32Float => P::R32Float,
        F::R32Uint => P::R32Uint,
        F::R32Sint => P::R32Sint,
        F::Rg16Uint => P::RG16Uint,
        F::Rg16Sint => P::RG16Sint,
        F::Rg16Float => P::RG16Float,
        F::Rgba8Unorm => P::RGBA8Unorm,
        F::Rgba8UnormSrgb => P::RGBA8UnormSrgb,
        F::Rgba8Snorm => P::RGBA8Snorm,
        F::Rgba8Uint => P::RGBA8Uint,
        F::Rgba8Sint => P::RGBA8Sint,
        F::Bgra8Unorm => P::BGRA8Unorm,
        F::Bgra8UnormSrgb => P::BGRA8UnormSrgb,
        F::Rgb10a2Uint => P::RGB10A2Uint,
        F::Rgb10a2Unorm => P::RGB10A2Unorm,
        F::Rg11b10Ufloat => P::RG11B10Ufloat,
        F::Rgb9e5Ufloat => P::RGB9E5Ufloat,
        F::Rg32Float => P::RG32Float,
        F::Rg32Uint => P::RG32Uint,
        F::Rg32Sint => P::RG32Sint,
        F::Rgba16Uint => P::RGBA16Uint,
        F::Rgba16Sint => P::RGBA16Sint,
        F::Rgba16Float => P::RGBA16Float,
        F::Rgba32Float => P::RGBA32Float,
        F::Rgba32Uint => P::RGBA32Uint,
        F::Rgba32Sint => P::RGBA32Sint,
        F::Stencil8 => P::Stencil8,
        F::Depth16Unorm => P::Depth16Unorm,
        F::Depth24Plus => P::Depth24Plus,
        F::Depth24PlusStencil8 => P::Depth24PlusStencil8,
        F::Depth32Float => P::Depth32Float,
        F::Depth32FloatStencil8 => P::Depth32FloatStencil8,
        F::Bc1RgbaUnorm => P::BC1RGBAUnorm,
        F::Bc1RgbaUnormSrgb => P::BC1RGBAUnormSrgb,
        F::Bc2RgbaUnorm => P::BC2RGBAUnorm,
        F::Bc2RgbaUnormSrgb => P::BC2RGBAUnormSrgb,
        F::Bc3RgbaUnorm => P::BC3RGBAUnorm,
        F::Bc3RgbaUnormSrgb => P::BC3RGBAUnormSrgb,
        F::Bc4RUnorm => P::BC4RUnorm,
        F::Bc4RSnorm => P::BC4RSnorm,
        F::Bc5RgUnorm => P::BC5RGUnorm,
        F::Bc5RgSnorm => P::BC5RGSnorm,
        F::Bc6hRgbUfloat => P::BC6HRGBUfloat,
        F::Bc6hRgbFloat => P::BC6HRGBFloat,
        F::Bc7RgbaUnorm => P::BC7RGBAUnorm,
        F::Bc7RgbaUnormSrgb => P::BC7RGBAUnormSrgb,
        F::Etc2Rgb8Unorm => P::ETC2RGB8Unorm,
        F::Etc2Rgb8UnormSrgb => P::ETC2RGB8UnormSrgb,
        F::Etc2Rgb8A1Unorm => P::ETC2RGB8A1Unorm,
        F::Etc2Rgb8A1UnormSrgb => P::ETC2RGB8A1UnormSrgb,
        F::Etc2Rgba8Unorm => P::ETC2RGBA8Unorm,
        F::Etc2Rgba8UnormSrgb => P::ETC2RGBA8UnormSrgb,
        F::EacR11Unorm => P::EACR11Unorm,
        F::EacR11Snorm => P::EACR11Snorm,
        F::EacRg11Unorm => P::EACRG11Unorm,
        F::EacRg11Snorm => P::EACRG11Snorm,
        F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC4x4Unorm,
        F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC4x4UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC5x4Unorm,
        F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC5x4UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC5x5Unorm,
        F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC5x5UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC6x5Unorm,
        F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC6x5UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC6x6Unorm,
        F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC6x6UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC8x5Unorm,
        F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC8x5UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC8x6Unorm,
        F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC8x6UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC8x8Unorm,
        F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC8x8UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC10x5Unorm,
        F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC10x5UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC10x6Unorm,
        F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC10x6UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC10x8Unorm,
        F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC10x8UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC10x10Unorm,
        F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC10x10UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC12x10Unorm,
        F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC12x10UnormSrgb,
        F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::Unorm,
        } => P::ASTC12x12Unorm,
        F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::UnormSrgb,
        } => P::ASTC12x12UnormSrgb,
        _ => P::Undefined,
    }
}

pub(crate) fn texture_dimension(v: u32) -> Result<wgt::TextureDimension, Status> {
    use crate::abi::TextureDimension;
    let d = TextureDimension::try_from(v).map_err(|v| invalid_enum_what("texture dimension", v))?;
    Ok(match d {
        TextureDimension::Texture1D => wgt::TextureDimension::D1,
        TextureDimension::Texture2D => wgt::TextureDimension::D2,
        TextureDimension::Texture3D => wgt::TextureDimension::D3,
    })
}

/// Texture usage bits: Read=1<<0, Write=1<<1, TextureBinding=1<<2,
/// StorageBinding=1<<3, ColorAttachment=1<<4, DepthAttachment=1<<5.
pub(crate) fn texture_usage(v: u32) -> wgt::TextureUsages {
    let mut usage = wgt::TextureUsages::empty();
    if v & (1 << 0) != 0 {
        usage |= wgt::TextureUsages::COPY_SRC;
    }
    if v & (1 << 1) != 0 {
        usage |= wgt::TextureUsages::COPY_DST;
    }
    if v & (1 << 2) != 0 {
        usage |= wgt::TextureUsages::TEXTURE_BINDING;
    }
    if v & (1 << 3) != 0 {
        usage |= wgt::TextureUsages::STORAGE_BINDING;
    }
    if v & (1 << 4) != 0 {
        usage |= wgt::TextureUsages::RENDER_ATTACHMENT;
    }
    if v & (1 << 5) != 0 {
        usage |= wgt::TextureUsages::RENDER_ATTACHMENT;
    }
    usage
}

/// Buffer usage bits, 1:1 with WebGPU: MapRead..QueryResolve.
pub(crate) fn buffer_usage(v: u32) -> Result<wgt::BufferUsages, Status> {
    const MAP_READ: u32 = 1 << 0;
    const MAP_WRITE: u32 = 1 << 1;
    const COPY_SRC: u32 = 1 << 2;
    const COPY_DST: u32 = 1 << 3;
    const INDEX: u32 = 1 << 4;
    const VERTEX: u32 = 1 << 5;
    const UNIFORM: u32 = 1 << 6;
    const STORAGE: u32 = 1 << 7;
    const INDIRECT: u32 = 1 << 8;
    const QUERY_RESOLVE: u32 = 1 << 9;
    let mut usage = wgt::BufferUsages::empty();
    if v & MAP_READ != 0 {
        usage |= wgt::BufferUsages::MAP_READ;
    }
    if v & MAP_WRITE != 0 {
        usage |= wgt::BufferUsages::MAP_WRITE;
    }
    if v & COPY_SRC != 0 {
        usage |= wgt::BufferUsages::COPY_SRC;
    }
    if v & COPY_DST != 0 {
        usage |= wgt::BufferUsages::COPY_DST;
    }
    if v & INDEX != 0 {
        usage |= wgt::BufferUsages::INDEX;
    }
    if v & VERTEX != 0 {
        usage |= wgt::BufferUsages::VERTEX;
    }
    if v & UNIFORM != 0 {
        usage |= wgt::BufferUsages::UNIFORM;
    }
    if v & STORAGE != 0 {
        usage |= wgt::BufferUsages::STORAGE;
    }
    if v & INDIRECT != 0 {
        usage |= wgt::BufferUsages::INDIRECT;
    }
    if v & QUERY_RESOLVE != 0 {
        usage |= wgt::BufferUsages::QUERY_RESOLVE;
    }
    Ok(usage)
}

pub(crate) fn texture_view_dimension(v: u32) -> Result<wgt::TextureViewDimension, Status> {
    use crate::abi::TextureViewDimension;
    let d = TextureViewDimension::try_from(v)
        .map_err(|v| invalid_enum_what("texture view dimension", v))?;
    Ok(match d {
        TextureViewDimension::Undefined => wgt::TextureViewDimension::D1,
        TextureViewDimension::Texture1D => wgt::TextureViewDimension::D1,
        // Texture1DArray is unsupported by wgpu; treat as D1.
        TextureViewDimension::Texture1DArray => wgt::TextureViewDimension::D1,
        TextureViewDimension::Texture2D => wgt::TextureViewDimension::D2,
        TextureViewDimension::Texture2DArray => wgt::TextureViewDimension::D2Array,
        TextureViewDimension::Texture3D => wgt::TextureViewDimension::D3,
        TextureViewDimension::Cube => wgt::TextureViewDimension::Cube,
        TextureViewDimension::CubeArray => wgt::TextureViewDimension::CubeArray,
    })
}

pub(crate) fn texture_aspect(v: u32) -> Result<wgt::TextureAspect, Status> {
    use crate::abi::TextureAspect;
    let a = TextureAspect::try_from(v).map_err(|v| invalid_enum_what("texture aspect", v))?;
    Ok(match a {
        // None reads as All (lenient default).
        TextureAspect::None | TextureAspect::All => wgt::TextureAspect::All,
        TextureAspect::StencilOnly => wgt::TextureAspect::StencilOnly,
        TextureAspect::DepthOnly => wgt::TextureAspect::DepthOnly,
    })
}

pub(crate) fn address_mode(v: u32) -> Result<wgt::AddressMode, Status> {
    use crate::abi::AddressMode;
    let m = AddressMode::try_from(v).map_err(|v| invalid_enum_what("address mode", v))?;
    Ok(match m {
        AddressMode::Repeat => wgt::AddressMode::Repeat,
        AddressMode::MirrorRepeat => wgt::AddressMode::MirrorRepeat,
        AddressMode::ClampToEdge => wgt::AddressMode::ClampToEdge,
    })
}

pub(crate) fn filter_mode(v: u32) -> Result<wgt::FilterMode, Status> {
    use crate::abi::FilterMode;
    let m = FilterMode::try_from(v).map_err(|v| invalid_enum_what("filter mode", v))?;
    Ok(match m {
        FilterMode::None => return Err(invalid_enum_what("filter mode", v)),
        FilterMode::Nearest => wgt::FilterMode::Nearest,
        FilterMode::Linear => wgt::FilterMode::Linear,
    })
}

pub(crate) fn mipmap_filter_mode(v: u32) -> Result<wgt::MipmapFilterMode, Status> {
    use crate::abi::FilterMode;
    let m = FilterMode::try_from(v).map_err(|v| invalid_enum_what("mipmap filter mode", v))?;
    Ok(match m {
        FilterMode::None => return Err(invalid_enum_what("mipmap filter mode", v)),
        FilterMode::Nearest => wgt::MipmapFilterMode::Nearest,
        FilterMode::Linear => wgt::MipmapFilterMode::Linear,
    })
}

pub(crate) fn compare_function(v: u32) -> Result<wgt::CompareFunction, Status> {
    use crate::abi::CompareFunction;
    let c = CompareFunction::try_from(v).map_err(|v| invalid_enum_what("compare function", v))?;
    Ok(match c {
        CompareFunction::Undefined => return Err(invalid_enum_what("compare function", v)),
        CompareFunction::Never => wgt::CompareFunction::Never,
        CompareFunction::Less => wgt::CompareFunction::Less,
        CompareFunction::LessEqual => wgt::CompareFunction::LessEqual,
        CompareFunction::Equal => wgt::CompareFunction::Equal,
        CompareFunction::Greater => wgt::CompareFunction::Greater,
        CompareFunction::NotEqual => wgt::CompareFunction::NotEqual,
        CompareFunction::GreaterEqual => wgt::CompareFunction::GreaterEqual,
        CompareFunction::Always => wgt::CompareFunction::Always,
    })
}

pub(crate) fn optional_compare_function(v: u32) -> Result<Option<wgt::CompareFunction>, Status> {
    if v == 0 || v == NONE {
        return Ok(None);
    }
    compare_function(v).map(Some)
}

pub(crate) fn blend_factor(v: u32) -> Result<wgt::BlendFactor, Status> {
    use crate::abi::BlendFactor as B;
    let f = B::try_from(v).map_err(|v| invalid_enum_what("blend factor", v))?;
    Ok(match f {
        B::Zero => wgt::BlendFactor::Zero,
        B::One => wgt::BlendFactor::One,
        B::Src => wgt::BlendFactor::Src,
        B::OneMinusSrc => wgt::BlendFactor::OneMinusSrc,
        B::SrcAlpha => wgt::BlendFactor::SrcAlpha,
        B::OneMinusSrcAlpha => wgt::BlendFactor::OneMinusSrcAlpha,
        B::Dst => wgt::BlendFactor::Dst,
        B::OneMinusDst => wgt::BlendFactor::OneMinusDst,
        B::DstAlpha => wgt::BlendFactor::DstAlpha,
        B::OneMinusDstAlpha => wgt::BlendFactor::OneMinusDstAlpha,
        B::SrcAlphaSaturated => wgt::BlendFactor::SrcAlphaSaturated,
        B::Constant => wgt::BlendFactor::Constant,
        B::OneMinusConstant => wgt::BlendFactor::OneMinusConstant,
    })
}

pub(crate) fn blend_operation(v: u32) -> Result<wgt::BlendOperation, Status> {
    use crate::abi::BlendOperation;
    let op = BlendOperation::try_from(v).map_err(|v| invalid_enum_what("blend operation", v))?;
    Ok(match op {
        BlendOperation::Add => wgt::BlendOperation::Add,
        BlendOperation::Subtract => wgt::BlendOperation::Subtract,
        BlendOperation::ReverseSubtract => wgt::BlendOperation::ReverseSubtract,
        BlendOperation::Min => wgt::BlendOperation::Min,
        BlendOperation::Max => wgt::BlendOperation::Max,
    })
}

pub(crate) fn cull_mode(v: u32) -> Result<Option<wgt::Face>, Status> {
    use crate::abi::CullMode;
    let m = CullMode::try_from(v).map_err(|v| invalid_enum_what("cull mode", v))?;
    Ok(match m {
        CullMode::None => None,
        CullMode::Front => Some(wgt::Face::Front),
        CullMode::Back => Some(wgt::Face::Back),
    })
}

pub(crate) fn front_face(v: u32) -> Result<wgt::FrontFace, Status> {
    use crate::abi::FrontFace;
    let f = FrontFace::try_from(v).map_err(|v| invalid_enum_what("front face", v))?;
    Ok(match f {
        FrontFace::CounterClockwise => wgt::FrontFace::Ccw,
        FrontFace::Clockwise => wgt::FrontFace::Cw,
    })
}

pub(crate) fn primitive_topology(v: u32) -> Result<wgt::PrimitiveTopology, Status> {
    use crate::abi::PrimitiveTopology;
    let t =
        PrimitiveTopology::try_from(v).map_err(|v| invalid_enum_what("primitive topology", v))?;
    Ok(match t {
        PrimitiveTopology::PointList => wgt::PrimitiveTopology::PointList,
        PrimitiveTopology::LineList => wgt::PrimitiveTopology::LineList,
        PrimitiveTopology::LineStrip => wgt::PrimitiveTopology::LineStrip,
        PrimitiveTopology::TriangleList => wgt::PrimitiveTopology::TriangleList,
        PrimitiveTopology::TriangleStrip => wgt::PrimitiveTopology::TriangleStrip,
    })
}

pub(crate) fn index_format(v: u32) -> Result<wgt::IndexFormat, Status> {
    use crate::abi::IndexFormat;
    let f = IndexFormat::try_from(v).map_err(|v| invalid_enum_what("index format", v))?;
    Ok(match f {
        // Undefined is only valid with non-indexed draws; wgpu still wants a format.
        IndexFormat::Undefined => wgt::IndexFormat::Uint16,
        IndexFormat::UInt16 => wgt::IndexFormat::Uint16,
        IndexFormat::UInt32 => wgt::IndexFormat::Uint32,
    })
}

pub(crate) fn stencil_operation(v: u32) -> Result<wgt::StencilOperation, Status> {
    use crate::abi::StencilOperation;
    let op =
        StencilOperation::try_from(v).map_err(|v| invalid_enum_what("stencil operation", v))?;
    Ok(match op {
        StencilOperation::Keep => wgt::StencilOperation::Keep,
        StencilOperation::Zero => wgt::StencilOperation::Zero,
        StencilOperation::Replace => wgt::StencilOperation::Replace,
        StencilOperation::Invert => wgt::StencilOperation::Invert,
        StencilOperation::IncrementClamp => wgt::StencilOperation::IncrementClamp,
        StencilOperation::DecrementClamp => wgt::StencilOperation::DecrementClamp,
        StencilOperation::IncrementWrap => wgt::StencilOperation::IncrementWrap,
        StencilOperation::DecrementWrap => wgt::StencilOperation::DecrementWrap,
    })
}

pub(crate) fn vertex_format(v: u32) -> Result<wgt::VertexFormat, Status> {
    use crate::abi::VertexFormat as VF;
    use wgt::VertexFormat as F;
    let f = VF::try_from(v).map_err(|v| invalid_enum_what("vertex format", v))?;
    Ok(match f {
        // Undefined never appears in real layouts; degrade to Float32.
        VF::Undefined => F::Float32,
        VF::Uint8x2 => F::Uint8x2,
        VF::Uint8x4 => F::Uint8x4,
        VF::Sint8x2 => F::Sint8x2,
        VF::Sint8x4 => F::Sint8x4,
        VF::Unorm8x2 => F::Unorm8x2,
        VF::Unorm8x4 => F::Unorm8x4,
        VF::Snorm8x2 => F::Snorm8x2,
        VF::Snorm8x4 => F::Snorm8x4,
        VF::Uint16x2 => F::Uint16x2,
        VF::Uint16x4 => F::Uint16x4,
        VF::Sint16x2 => F::Sint16x2,
        VF::Sint16x4 => F::Sint16x4,
        VF::Unorm16x2 => F::Unorm16x2,
        VF::Unorm16x4 => F::Unorm16x4,
        VF::Snorm16x2 => F::Snorm16x2,
        VF::Snorm16x4 => F::Snorm16x4,
        VF::Float16x2 => F::Float16x2,
        VF::Float16x4 => F::Float16x4,
        VF::Float32 => F::Float32,
        VF::Float32x2 => F::Float32x2,
        VF::Float32x3 => F::Float32x3,
        VF::Float32x4 => F::Float32x4,
        VF::Uint32 => F::Uint32,
        VF::Uint32x2 => F::Uint32x2,
        VF::Uint32x3 => F::Uint32x3,
        VF::Uint32x4 => F::Uint32x4,
        VF::Sint32 => F::Sint32,
        VF::Sint32x2 => F::Sint32x2,
        VF::Sint32x3 => F::Sint32x3,
        VF::Sint32x4 => F::Sint32x4,
    })
}

pub(crate) fn vertex_step_mode(v: u32) -> Result<wgt::VertexStepMode, Status> {
    use crate::abi::VertexStepMode;
    let m = VertexStepMode::try_from(v).map_err(|v| invalid_enum_what("vertex step mode", v))?;
    Ok(match m {
        VertexStepMode::Vertex => wgt::VertexStepMode::Vertex,
        VertexStepMode::Instance => wgt::VertexStepMode::Instance,
        // NotUsed has no wgpu counterpart; treat as Vertex.
        VertexStepMode::NotUsed => wgt::VertexStepMode::Vertex,
    })
}

/// Shader stage visibility bits → `wgt::ShaderStages`.
pub(crate) fn shader_stages(v: u32) -> wgt::ShaderStages {
    let mut stages = wgt::ShaderStages::empty();
    if v & (1 << 0) != 0 {
        stages |= wgt::ShaderStages::VERTEX;
    }
    if v & (1 << 4) != 0 {
        stages |= wgt::ShaderStages::FRAGMENT;
    }
    if v & (1 << 5) != 0 {
        stages |= wgt::ShaderStages::COMPUTE;
    }
    stages
}

/// `TextureSampleType` -> bind-group texture binding sample type.
/// Lenient: `None` and unmapped raw values fall back to filterable Float.
pub(crate) fn texture_sample_type(v: u32) -> Result<wgt::TextureSampleType, Status> {
    use crate::abi::TextureSampleType;
    let t = TextureSampleType::try_from(v).unwrap_or(TextureSampleType::Float);
    Ok(match t {
        TextureSampleType::None | TextureSampleType::Float => {
            wgt::TextureSampleType::Float { filterable: true }
        }
        TextureSampleType::UnfilterableFloat => wgt::TextureSampleType::Float { filterable: false },
        TextureSampleType::Depth => wgt::TextureSampleType::Depth,
        TextureSampleType::Sint => wgt::TextureSampleType::Sint,
        TextureSampleType::Uint => wgt::TextureSampleType::Uint,
    })
}

pub(crate) fn storage_texture_access(v: u32) -> Result<wgt::StorageTextureAccess, Status> {
    Ok(match v & 0x3 {
        0b01 => wgt::StorageTextureAccess::ReadOnly,
        0b10 => wgt::StorageTextureAccess::WriteOnly,
        0b11 => wgt::StorageTextureAccess::ReadWrite,
        _ => wgt::StorageTextureAccess::WriteOnly,
    })
}

/// `abi::AttachmentStoreOp` -> `wgt::StoreOp`.
pub(crate) fn store_op(v: u32) -> Result<wgt::StoreOp, Status> {
    use crate::abi::AttachmentStoreOp;
    let op = AttachmentStoreOp::try_from(v).map_err(|v| invalid_enum_what("store op", v))?;
    Ok(match op {
        AttachmentStoreOp::Store => wgt::StoreOp::Store,
        AttachmentStoreOp::Discard => wgt::StoreOp::Discard,
    })
}

pub(crate) fn present_mode_to_abi(m: wgt::PresentMode) -> u32 {
    match m {
        wgt::PresentMode::Fifo => 0,
        wgt::PresentMode::Immediate => 1,
        wgt::PresentMode::Mailbox => 2,
        wgt::PresentMode::AutoVsync => 0,
        wgt::PresentMode::AutoNoVsync => 1,
        wgt::PresentMode::FifoRelaxed => 0,
    }
}
