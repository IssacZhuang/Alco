//! Single-point mapping between Alco C ABI enum values (numeric values mirror
//! the C# enums in `Alco.Graphics/Enums`) and `wgpu-types` enums.

use crate::abi::{Status, NONE};
use crate::entry::set_error;
use wgpu_types as wgt;

/// C# `PixelFormat` (0..95) → `wgt::TextureFormat`.
pub(crate) fn pixel_format(v: u32) -> Result<wgt::TextureFormat, Status> {
    use wgt::TextureFormat as F;
    let f = match v {
        0 => return Err(invalid_enum_what("pixel format", 0)),
        1 => F::R8Unorm,
        2 => F::R8Snorm,
        3 => F::R8Uint,
        4 => F::R8Sint,
        5 => F::R16Uint,
        6 => F::R16Sint,
        7 => F::R16Float,
        8 => F::Rg8Unorm,
        9 => F::Rg8Snorm,
        10 => F::Rg8Uint,
        11 => F::Rg8Sint,
        12 => F::R32Float,
        13 => F::R32Uint,
        14 => F::R32Sint,
        15 => F::Rg16Uint,
        16 => F::Rg16Sint,
        17 => F::Rg16Float,
        18 => F::Rgba8Unorm,
        19 => F::Rgba8UnormSrgb,
        20 => F::Rgba8Snorm,
        21 => F::Rgba8Uint,
        22 => F::Rgba8Sint,
        23 => F::Bgra8Unorm,
        24 => F::Bgra8UnormSrgb,
        25 => F::Rgb10a2Uint,
        26 => F::Rgb10a2Unorm,
        27 => F::Rg11b10Ufloat,
        28 => F::Rgb9e5Ufloat,
        29 => F::Rg32Float,
        30 => F::Rg32Uint,
        31 => F::Rg32Sint,
        32 => F::Rgba16Uint,
        33 => F::Rgba16Sint,
        34 => F::Rgba16Float,
        35 => F::Rgba32Float,
        36 => F::Rgba32Uint,
        37 => F::Rgba32Sint,
        38 => F::Stencil8, // C# marks unsupported; wgpu still defines it
        39 => F::Depth16Unorm,
        40 => F::Depth24Plus,
        41 => F::Depth24PlusStencil8,
        42 => F::Depth32Float,
        43 => F::Depth32FloatStencil8,
        44 => F::Bc1RgbaUnorm,
        45 => F::Bc1RgbaUnormSrgb,
        46 => F::Bc2RgbaUnorm,
        47 => F::Bc2RgbaUnormSrgb,
        48 => F::Bc3RgbaUnorm,
        49 => F::Bc3RgbaUnormSrgb,
        50 => F::Bc4RUnorm,
        51 => F::Bc4RSnorm,
        52 => F::Bc5RgUnorm,
        53 => F::Bc5RgSnorm,
        54 => F::Bc6hRgbUfloat,
        55 => F::Bc6hRgbFloat,
        56 => F::Bc7RgbaUnorm,
        57 => F::Bc7RgbaUnormSrgb,
        58 => F::Etc2Rgb8Unorm,
        59 => F::Etc2Rgb8UnormSrgb,
        60 => F::Etc2Rgb8A1Unorm,
        61 => F::Etc2Rgb8A1UnormSrgb,
        62 => F::Etc2Rgba8Unorm,
        63 => F::Etc2Rgba8UnormSrgb,
        64 => F::EacR11Unorm,
        65 => F::EacR11Snorm,
        66 => F::EacRg11Unorm,
        67 => F::EacRg11Snorm,
        68 => F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::Unorm,
        },
        69 => F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        70 => F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::Unorm,
        },
        71 => F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        72 => F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::Unorm,
        },
        73 => F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        74 => F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::Unorm,
        },
        75 => F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        76 => F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::Unorm,
        },
        77 => F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        78 => F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::Unorm,
        },
        79 => F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        80 => F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::Unorm,
        },
        81 => F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        82 => F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::Unorm,
        },
        83 => F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        84 => F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::Unorm,
        },
        85 => F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        86 => F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::Unorm,
        },
        87 => F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        88 => F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::Unorm,
        },
        89 => F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        90 => F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::Unorm,
        },
        91 => F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        92 => F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::Unorm,
        },
        93 => F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        94 => F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::Unorm,
        },
        95 => F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::UnormSrgb,
        },
        other => return Err(invalid_enum_what("pixel format", other)),
    };
    Ok(f)
}

fn invalid_enum_what(what: &str, value: u32) -> Status {
    set_error(
        Status::INVALID_ARGUMENT,
        format!("invalid {what} value {value}"),
    );
    Status::INVALID_ARGUMENT
}

/// `wgt::TextureFormat` → C# `PixelFormat`.
pub(crate) fn pixel_format_to_abi(f: wgt::TextureFormat) -> u32 {
    use wgt::TextureFormat as F;
    match f {
        F::R8Unorm => 1,
        F::R8Snorm => 2,
        F::R8Uint => 3,
        F::R8Sint => 4,
        F::R16Uint => 5,
        F::R16Sint => 6,
        F::R16Float => 7,
        F::Rg8Unorm => 8,
        F::Rg8Snorm => 9,
        F::Rg8Uint => 10,
        F::Rg8Sint => 11,
        F::R32Float => 12,
        F::R32Uint => 13,
        F::R32Sint => 14,
        F::Rg16Uint => 15,
        F::Rg16Sint => 16,
        F::Rg16Float => 17,
        F::Rgba8Unorm => 18,
        F::Rgba8UnormSrgb => 19,
        F::Rgba8Snorm => 20,
        F::Rgba8Uint => 21,
        F::Rgba8Sint => 22,
        F::Bgra8Unorm => 23,
        F::Bgra8UnormSrgb => 24,
        F::Rgb10a2Uint => 25,
        F::Rgb10a2Unorm => 26,
        F::Rg11b10Ufloat => 27,
        F::Rgb9e5Ufloat => 28,
        F::Rg32Float => 29,
        F::Rg32Uint => 30,
        F::Rg32Sint => 31,
        F::Rgba16Uint => 32,
        F::Rgba16Sint => 33,
        F::Rgba16Float => 34,
        F::Rgba32Float => 35,
        F::Rgba32Uint => 36,
        F::Rgba32Sint => 37,
        F::Stencil8 => 38,
        F::Depth16Unorm => 39,
        F::Depth24Plus => 40,
        F::Depth24PlusStencil8 => 41,
        F::Depth32Float => 42,
        F::Depth32FloatStencil8 => 43,
        F::Bc1RgbaUnorm => 44,
        F::Bc1RgbaUnormSrgb => 45,
        F::Bc2RgbaUnorm => 46,
        F::Bc2RgbaUnormSrgb => 47,
        F::Bc3RgbaUnorm => 48,
        F::Bc3RgbaUnormSrgb => 49,
        F::Bc4RUnorm => 50,
        F::Bc4RSnorm => 51,
        F::Bc5RgUnorm => 52,
        F::Bc5RgSnorm => 53,
        F::Bc6hRgbUfloat => 54,
        F::Bc6hRgbFloat => 55,
        F::Bc7RgbaUnorm => 56,
        F::Bc7RgbaUnormSrgb => 57,
        F::Etc2Rgb8Unorm => 58,
        F::Etc2Rgb8UnormSrgb => 59,
        F::Etc2Rgb8A1Unorm => 60,
        F::Etc2Rgb8A1UnormSrgb => 61,
        F::Etc2Rgba8Unorm => 62,
        F::Etc2Rgba8UnormSrgb => 63,
        F::EacR11Unorm => 64,
        F::EacR11Snorm => 65,
        F::EacRg11Unorm => 66,
        F::EacRg11Snorm => 67,
        F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::Unorm,
        } => 68,
        F::Astc {
            block: wgt::AstcBlock::B4x4,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 69,
        F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::Unorm,
        } => 70,
        F::Astc {
            block: wgt::AstcBlock::B5x4,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 71,
        F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::Unorm,
        } => 72,
        F::Astc {
            block: wgt::AstcBlock::B5x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 73,
        F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::Unorm,
        } => 74,
        F::Astc {
            block: wgt::AstcBlock::B6x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 75,
        F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::Unorm,
        } => 76,
        F::Astc {
            block: wgt::AstcBlock::B6x6,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 77,
        F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::Unorm,
        } => 78,
        F::Astc {
            block: wgt::AstcBlock::B8x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 79,
        F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::Unorm,
        } => 80,
        F::Astc {
            block: wgt::AstcBlock::B8x6,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 81,
        F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::Unorm,
        } => 82,
        F::Astc {
            block: wgt::AstcBlock::B8x8,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 83,
        F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::Unorm,
        } => 84,
        F::Astc {
            block: wgt::AstcBlock::B10x5,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 85,
        F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::Unorm,
        } => 86,
        F::Astc {
            block: wgt::AstcBlock::B10x6,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 87,
        F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::Unorm,
        } => 88,
        F::Astc {
            block: wgt::AstcBlock::B10x8,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 89,
        F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::Unorm,
        } => 90,
        F::Astc {
            block: wgt::AstcBlock::B10x10,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 91,
        F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::Unorm,
        } => 92,
        F::Astc {
            block: wgt::AstcBlock::B12x10,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 93,
        F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::Unorm,
        } => 94,
        F::Astc {
            block: wgt::AstcBlock::B12x12,
            channel: wgt::AstcChannel::UnormSrgb,
        } => 95,
        _ => 0,
    }
}

pub(crate) fn texture_dimension(v: u32) -> Result<wgt::TextureDimension, Status> {
    Ok(match v {
        0 => wgt::TextureDimension::D1,
        1 => wgt::TextureDimension::D2,
        2 => wgt::TextureDimension::D3,
        other => return Err(invalid_enum_what("texture dimension", other)),
    })
}

/// C# `TextureUsage` bits: Read=1<<0, Write=1<<1, TextureBinding=1<<2,
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

/// C# `BufferUsage` bits (1:1 with WebGPU: MapRead..QueryResolve).
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
    Ok(match v {
        0 => wgt::TextureViewDimension::D1,
        1 => wgt::TextureViewDimension::D1,
        // C# Texture1DArray (2) is unsupported by wgpu; treat as D1.
        2 => wgt::TextureViewDimension::D1,
        3 => wgt::TextureViewDimension::D2,
        4 => wgt::TextureViewDimension::D2Array,
        5 => wgt::TextureViewDimension::D3,
        6 => wgt::TextureViewDimension::Cube,
        7 => wgt::TextureViewDimension::CubeArray,
        other => return Err(invalid_enum_what("texture view dimension", other)),
    })
}

pub(crate) fn texture_aspect(v: u32) -> Result<wgt::TextureAspect, Status> {
    Ok(match v {
        0 => wgt::TextureAspect::All,
        1 => wgt::TextureAspect::All,
        2 => wgt::TextureAspect::StencilOnly,
        3 => wgt::TextureAspect::DepthOnly,
        other => return Err(invalid_enum_what("texture aspect", other)),
    })
}

pub(crate) fn address_mode(v: u32) -> Result<wgt::AddressMode, Status> {
    Ok(match v {
        0 => wgt::AddressMode::Repeat,
        1 => wgt::AddressMode::MirrorRepeat,
        2 => wgt::AddressMode::ClampToEdge,
        other => return Err(invalid_enum_what("address mode", other)),
    })
}

pub(crate) fn filter_mode(v: u32) -> Result<wgt::FilterMode, Status> {
    Ok(match v {
        1 => wgt::FilterMode::Nearest,
        2 => wgt::FilterMode::Linear,
        other => return Err(invalid_enum_what("filter mode", other)),
    })
}

pub(crate) fn mipmap_filter_mode(v: u32) -> Result<wgt::MipmapFilterMode, Status> {
    Ok(match v {
        1 => wgt::MipmapFilterMode::Nearest,
        2 => wgt::MipmapFilterMode::Linear,
        other => return Err(invalid_enum_what("mipmap filter mode", other)),
    })
}

pub(crate) fn compare_function(v: u32) -> Result<wgt::CompareFunction, Status> {
    Ok(match v {
        1 => wgt::CompareFunction::Never,
        2 => wgt::CompareFunction::Less,
        3 => wgt::CompareFunction::LessEqual,
        4 => wgt::CompareFunction::Equal,
        5 => wgt::CompareFunction::Greater,
        6 => wgt::CompareFunction::NotEqual,
        7 => wgt::CompareFunction::GreaterEqual,
        8 => wgt::CompareFunction::Always,
        other => return Err(invalid_enum_what("compare function", other)),
    })
}

pub(crate) fn optional_compare_function(v: u32) -> Result<Option<wgt::CompareFunction>, Status> {
    if v == 0 || v == NONE {
        return Ok(None);
    }
    compare_function(v).map(Some)
}

pub(crate) fn blend_factor(v: u32) -> Result<wgt::BlendFactor, Status> {
    use wgt::BlendFactor as B;
    Ok(match v {
        0 => B::Zero,
        1 => B::One,
        2 => B::Src,
        3 => B::OneMinusSrc,
        4 => B::SrcAlpha,
        5 => B::OneMinusSrcAlpha,
        6 => B::Dst,
        7 => B::OneMinusDst,
        8 => B::DstAlpha,
        9 => B::OneMinusDstAlpha,
        10 => B::SrcAlphaSaturated,
        11 => B::Constant,
        12 => B::OneMinusConstant,
        other => return Err(invalid_enum_what("blend factor", other)),
    })
}

pub(crate) fn blend_operation(v: u32) -> Result<wgt::BlendOperation, Status> {
    Ok(match v {
        0 => wgt::BlendOperation::Add,
        1 => wgt::BlendOperation::Subtract,
        2 => wgt::BlendOperation::ReverseSubtract,
        3 => wgt::BlendOperation::Min,
        4 => wgt::BlendOperation::Max,
        other => return Err(invalid_enum_what("blend operation", other)),
    })
}

pub(crate) fn cull_mode(v: u32) -> Result<Option<wgt::Face>, Status> {
    Ok(match v {
        0 => None,
        1 => Some(wgt::Face::Front),
        2 => Some(wgt::Face::Back),
        other => return Err(invalid_enum_what("cull mode", other)),
    })
}

pub(crate) fn front_face(v: u32) -> Result<wgt::FrontFace, Status> {
    Ok(match v {
        0 => wgt::FrontFace::Ccw,
        1 => wgt::FrontFace::Cw,
        other => return Err(invalid_enum_what("front face", other)),
    })
}

pub(crate) fn primitive_topology(v: u32) -> Result<wgt::PrimitiveTopology, Status> {
    Ok(match v {
        0 => wgt::PrimitiveTopology::PointList,
        1 => wgt::PrimitiveTopology::LineList,
        2 => wgt::PrimitiveTopology::LineStrip,
        3 => wgt::PrimitiveTopology::TriangleList,
        4 => wgt::PrimitiveTopology::TriangleStrip,
        other => return Err(invalid_enum_what("primitive topology", other)),
    })
}

pub(crate) fn index_format(v: u32) -> Result<wgt::IndexFormat, Status> {
    Ok(match v {
        0 => wgt::IndexFormat::Uint16, // C# Undefined (only used with non-indexed draws)
        1 => wgt::IndexFormat::Uint16,
        2 => wgt::IndexFormat::Uint32,
        other => return Err(invalid_enum_what("index format", other)),
    })
}

pub(crate) fn stencil_operation(v: u32) -> Result<wgt::StencilOperation, Status> {
    Ok(match v {
        0 => wgt::StencilOperation::Keep,
        1 => wgt::StencilOperation::Zero,
        2 => wgt::StencilOperation::Replace,
        3 => wgt::StencilOperation::Invert,
        4 => wgt::StencilOperation::IncrementClamp,
        5 => wgt::StencilOperation::DecrementClamp,
        6 => wgt::StencilOperation::IncrementWrap,
        7 => wgt::StencilOperation::DecrementWrap,
        other => return Err(invalid_enum_what("stencil operation", other)),
    })
}

pub(crate) fn vertex_format(v: u32) -> Result<wgt::VertexFormat, Status> {
    use wgt::VertexFormat as F;
    Ok(match v {
        0 => F::Float32, // Undefined never appears in real layouts
        1 => F::Uint8x2,
        2 => F::Uint8x4,
        3 => F::Sint8x2,
        4 => F::Sint8x4,
        5 => F::Unorm8x2,
        6 => F::Unorm8x4,
        7 => F::Snorm8x2,
        8 => F::Snorm8x4,
        9 => F::Uint16x2,
        10 => F::Uint16x4,
        11 => F::Sint16x2,
        12 => F::Sint16x4,
        13 => F::Unorm16x2,
        14 => F::Unorm16x4,
        15 => F::Snorm16x2,
        16 => F::Snorm16x4,
        17 => F::Float16x2,
        18 => F::Float16x4,
        19 => F::Float32,
        20 => F::Float32x2,
        21 => F::Float32x3,
        22 => F::Float32x4,
        23 => F::Uint32,
        24 => F::Uint32x2,
        25 => F::Uint32x3,
        26 => F::Uint32x4,
        27 => F::Sint32,
        28 => F::Sint32x2,
        29 => F::Sint32x3,
        30 => F::Sint32x4,
        other => return Err(invalid_enum_what("vertex format", other)),
    })
}

pub(crate) fn vertex_step_mode(v: u32) -> Result<wgt::VertexStepMode, Status> {
    Ok(match v {
        0 => wgt::VertexStepMode::Vertex,
        1 => wgt::VertexStepMode::Instance,
        // C# NotUsed (2) has no wgpu counterpart; treat as Vertex.
        2 => wgt::VertexStepMode::Vertex,
        other => return Err(invalid_enum_what("vertex step mode", other)),
    })
}

/// C# `ShaderStage` bits → `wgt::ShaderStages`.
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

/// C# `TextureSampleType` → bind-group texture binding sample type.
pub(crate) fn texture_sample_type(v: u32) -> Result<wgt::TextureSampleType, Status> {
    Ok(match v {
        1 => wgt::TextureSampleType::Float { filterable: true },
        2 => wgt::TextureSampleType::Float { filterable: false },
        3 => wgt::TextureSampleType::Depth,
        4 => wgt::TextureSampleType::Sint,
        5 => wgt::TextureSampleType::Uint,
        _ => wgt::TextureSampleType::Float { filterable: true },
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

pub(crate) fn store_op(v: u32) -> Result<wgt::StoreOp, Status> {
    Ok(match v {
        0 => wgt::StoreOp::Store,
        1 => wgt::StoreOp::Discard,
        other => return Err(invalid_enum_what("store op", other)),
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
