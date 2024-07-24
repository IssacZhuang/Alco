namespace Vocore.ShaderCompiler;

/// <summary>
/// The result code of the DirectX Shader Compiler. 
/// </summary> 
public readonly struct DxcResultCode
{
    public static readonly DxcResultCode S_OK = new DxcResultCode(0, "The operation succeeded.");
    public static readonly DxcResultCode E_OVERLAPPING_SEMANTICS = new DxcResultCode(0x80AA0001, "The operation failed because overlapping semantics were found.");
    public static readonly DxcResultCode E_MULTIPLE_DEPTH_SEMANTICS = new DxcResultCode(0x80AA0002, "The operation failed because multiple depth semantics were found.");
    public static readonly DxcResultCode E_INPUT_FILE_TOO_LARGE = new DxcResultCode(0x80AA0003, "Input file is too large.");
    public static readonly DxcResultCode E_INCORRECT_DXBC = new DxcResultCode(0x80AA0004, "Error parsing DXBC container.");
    public static readonly DxcResultCode E_ERROR_PARSING_DXBC_BYTECODE = new DxcResultCode(0x80AA0005, "Error parsing DXBC bytecode.");
    public static readonly DxcResultCode E_DATA_TOO_LARGE = new DxcResultCode(0x80AA0006, "Data is too large.");
    public static readonly DxcResultCode E_INCOMPATIBLE_CONVERTER_OPTIONS = new DxcResultCode(0x80AA0007, "Incompatible converter options.");
    public static readonly DxcResultCode E_IRREDUCIBLE_CFG = new DxcResultCode(0x80AA0008, "Irreducible control flow graph.");
    public static readonly DxcResultCode E_IR_VERIFICATION_FAILED = new DxcResultCode(0x80AA0009, "IR verification error.");
    public static readonly DxcResultCode E_SCOPE_NESTED_FAILED = new DxcResultCode(0x80AA000A, "Scope-nested control flow recovery failed.");
    public static readonly DxcResultCode E_NOT_SUPPORTED = new DxcResultCode(0x80AA000B, "Operation is not supported.");
    public static readonly DxcResultCode E_STRING_ENCODING_FAILED = new DxcResultCode(0x80AA000C, "Unable to encode string.");
    public static readonly DxcResultCode E_CONTAINER_INVALID = new DxcResultCode(0x80AA000D, "DXIL container is invalid.");
    public static readonly DxcResultCode E_CONTAINER_MISSING_DXIL = new DxcResultCode(0x80AA000E, "DXIL container is missing the DXIL part.");
    public static readonly DxcResultCode E_INCORRECT_DXIL_METADATA = new DxcResultCode(0x80AA000F, "Unable to parse DxilModule metadata.");
    public static readonly DxcResultCode E_INCORRECT_DDI_SIGNATURE = new DxcResultCode(0x80AA0010, "Error parsing DDI signature.");
    public static readonly DxcResultCode E_DUPLICATE_PART = new DxcResultCode(0x80AA0011, "Duplicate part exists in dxil container.");
    public static readonly DxcResultCode E_MISSING_PART = new DxcResultCode(0x80AA0012, "Error finding part in dxil container.");
    public static readonly DxcResultCode E_MALFORMED_CONTAINER = new DxcResultCode(0x80AA0013, "Malformed DXIL Container.");
    public static readonly DxcResultCode E_INCORRECT_ROOT_SIGNATURE = new DxcResultCode(0x80AA0014, "Incorrect Root Signature for shader.");
    public static readonly DxcResultCode E_CONTAINER_MISSING_DEBUG = new DxcResultCode(0x80AA0015, "DXIL container is missing DebugInfo part.");
    public static readonly DxcResultCode E_MACRO_EXPANSION_FAILURE = new DxcResultCode(0x80AA0016, "Unexpected failure in macro expansion.");
    public static readonly DxcResultCode E_OPTIMIZATION_FAILED = new DxcResultCode(0x80AA0017, "DXIL optimization pass failed.");
    public static readonly DxcResultCode E_GENERAL_INTERNAL_ERROR = new DxcResultCode(0x80AA0018, "General internal error.");
    public static readonly DxcResultCode E_ABORT_COMPILATION_ERROR = new DxcResultCode(0x80AA0019, "Abort compilation error.");
    public static readonly DxcResultCode E_EXTENSION_ERROR = new DxcResultCode(0x80AA001A, "Error in extension mechanism.");
    public static readonly DxcResultCode E_LLVM_FATAL_ERROR = new DxcResultCode(0x80AA001B, "LLVM Fatal Error");
    public static readonly DxcResultCode E_LLVM_UNREACHABLE = new DxcResultCode(0x80AA001C, "LLVM Unreachable code");
    public static readonly DxcResultCode E_LLVM_CAST_ERROR = new DxcResultCode(0x80AA001D, "LLVM Cast Failure");
    public static readonly DxcResultCode E_VALIDATOR_MISSING = new DxcResultCode(0x80AA001E, "External validator (DXIL.dll) required, and missing.");

    public static readonly DxcResultCode Unknown = new DxcResultCode(0xFFFFFFFF, "Unknown dxc result.");

    public readonly string Desc;
    public readonly uint Code;
    public DxcResultCode(uint code, string desc)
    {
        Desc = desc;
        Code = code;
    }

    public static DxcResultCode GetDxcResult(int code)
    {
        return GetDxcResult((uint)code);
    }

    public static DxcResultCode GetDxcResult(uint code)
    {
        switch (code)
        {
            case 0: return DxcResultCode.S_OK;
            case 0x80AA0001: return DxcResultCode.E_OVERLAPPING_SEMANTICS;
            case 0x80AA0002: return DxcResultCode.E_MULTIPLE_DEPTH_SEMANTICS;
            case 0x80AA0003: return DxcResultCode.E_INPUT_FILE_TOO_LARGE;
            case 0x80AA0004: return DxcResultCode.E_INCORRECT_DXBC;
            case 0x80AA0005: return DxcResultCode.E_ERROR_PARSING_DXBC_BYTECODE;
            case 0x80AA0006: return DxcResultCode.E_DATA_TOO_LARGE;
            case 0x80AA0007: return DxcResultCode.E_INCOMPATIBLE_CONVERTER_OPTIONS;
            case 0x80AA0008: return DxcResultCode.E_IRREDUCIBLE_CFG;
            case 0x80AA0009: return DxcResultCode.E_IR_VERIFICATION_FAILED;
            case 0x80AA000A: return DxcResultCode.E_SCOPE_NESTED_FAILED;
            case 0x80AA000B: return DxcResultCode.E_NOT_SUPPORTED;
            case 0x80AA000C: return DxcResultCode.E_STRING_ENCODING_FAILED;
            case 0x80AA000D: return DxcResultCode.E_CONTAINER_INVALID;
            case 0x80AA000E: return DxcResultCode.E_CONTAINER_MISSING_DXIL;
            case 0x80AA000F: return DxcResultCode.E_INCORRECT_DXIL_METADATA;
            case 0x80AA0010: return DxcResultCode.E_INCORRECT_DDI_SIGNATURE;
            case 0x80AA0011: return DxcResultCode.E_DUPLICATE_PART;
            case 0x80AA0012: return DxcResultCode.E_MISSING_PART;
            case 0x80AA0013: return DxcResultCode.E_MALFORMED_CONTAINER;
            case 0x80AA0014: return DxcResultCode.E_INCORRECT_ROOT_SIGNATURE;
            case 0x80AA0015: return DxcResultCode.E_CONTAINER_MISSING_DEBUG;
            case 0x80AA0016: return DxcResultCode.E_MACRO_EXPANSION_FAILURE;
            case 0x80AA0017: return DxcResultCode.E_OPTIMIZATION_FAILED;
            case 0x80AA0018: return DxcResultCode.E_GENERAL_INTERNAL_ERROR;
            case 0x80AA0019: return DxcResultCode.E_ABORT_COMPILATION_ERROR;
            case 0x80AA001A: return DxcResultCode.E_EXTENSION_ERROR;
            case 0x80AA001B: return DxcResultCode.E_LLVM_FATAL_ERROR;
            case 0x80AA001C: return DxcResultCode.E_LLVM_UNREACHABLE;
            case 0x80AA001D: return DxcResultCode.E_LLVM_CAST_ERROR;
            case 0x80AA001E: return DxcResultCode.E_VALIDATOR_MISSING;
            default: return DxcResultCode.Unknown;
        }
    }

    public override string ToString()
    {
        return $"Dxc result: {Code:X} - {Desc}";
    }
}

// from https://github.com/microsoft/DirectXShaderCompiler/blob/5852de760be2e0e70ee62fa427e0ff5a86dafc23/include/dxc/Support/ErrorCodes.h
/*

#pragma once

// Redeclare some macros to not depend on winerror.h
#define DXC_SEVERITY_ERROR 1
#define DXC_MAKE_HRESULT(sev, fac, code)                                       \
  ((HRESULT)(((unsigned long)(sev) << 31) | ((unsigned long)(fac) << 16) |     \
             ((unsigned long)(code))))

#define HRESULT_IS_WIN32ERR(hr)                                                \
  ((HRESULT)(hr & 0xFFFF0000) ==                                               \
   MAKE_HRESULT(SEVERITY_ERROR, FACILITY_WIN32, 0))
#define HRESULT_AS_WIN32ERR(hr) (HRESULT_CODE(hr))

// Error codes from C libraries (0n150) - 0x8096xxxx
#define FACILITY_ERRNO (0x96)
#define HRESULT_FROM_ERRNO(x)                                                  \
  MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_ERRNO, (x))

// Error codes from DXC libraries (0n170) - 0x8013xxxx
#define FACILITY_DXC (0xAA)

// 0x00000000 - The operation succeeded.
#define DXC_S_OK 0 // _HRESULT_TYPEDEF_(0x00000000L)

// 0x80AA0001 - The operation failed because overlapping semantics were found.
#define DXC_E_OVERLAPPING_SEMANTICS                                            \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0001))

// 0x80AA0002 - The operation failed because multiple depth semantics were
// found.
#define DXC_E_MULTIPLE_DEPTH_SEMANTICS                                         \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0002))

// 0x80AA0003 - Input file is too large.
#define DXC_E_INPUT_FILE_TOO_LARGE                                             \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0003))

// 0x80AA0004 - Error parsing DXBC container.
#define DXC_E_INCORRECT_DXBC                                                   \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0004))

// 0x80AA0005 - Error parsing DXBC bytecode.
#define DXC_E_ERROR_PARSING_DXBC_BYTECODE                                      \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0005))

// 0x80AA0006 - Data is too large.
#define DXC_E_DATA_TOO_LARGE                                                   \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0006))

// 0x80AA0007 - Incompatible converter options.
#define DXC_E_INCOMPATIBLE_CONVERTER_OPTIONS                                   \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0007))

// 0x80AA0008 - Irreducible control flow graph.
#define DXC_E_IRREDUCIBLE_CFG                                                  \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0008))

// 0x80AA0009 - IR verification error.
#define DXC_E_IR_VERIFICATION_FAILED                                           \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0009))

// 0x80AA000A - Scope-nested control flow recovery failed.
#define DXC_E_SCOPE_NESTED_FAILED                                              \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x000A))

// 0x80AA000B - Operation is not supported.
#define DXC_E_NOT_SUPPORTED                                                    \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x000B))

// 0x80AA000C - Unable to encode string.
#define DXC_E_STRING_ENCODING_FAILED                                           \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x000C))

// 0x80AA000D - DXIL container is invalid.
#define DXC_E_CONTAINER_INVALID                                                \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x000D))

// 0x80AA000E - DXIL container is missing the DXIL part.
#define DXC_E_CONTAINER_MISSING_DXIL                                           \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x000E))

// 0x80AA000F - Unable to parse DxilModule metadata.
#define DXC_E_INCORRECT_DXIL_METADATA                                          \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x000F))

// 0x80AA0010 - Error parsing DDI signature.
#define DXC_E_INCORRECT_DDI_SIGNATURE                                          \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0010))

// 0x80AA0011 - Duplicate part exists in dxil container.
#define DXC_E_DUPLICATE_PART                                                   \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0011))

// 0x80AA0012 - Error finding part in dxil container.
#define DXC_E_MISSING_PART                                                     \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0012))

// 0x80AA0013 - Malformed DXIL Container.
#define DXC_E_MALFORMED_CONTAINER                                              \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0013))

// 0x80AA0014 - Incorrect Root Signature for shader.
#define DXC_E_INCORRECT_ROOT_SIGNATURE                                         \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0014))

// 0X80AA0015 - DXIL container is missing DebugInfo part.
#define DXC_E_CONTAINER_MISSING_DEBUG                                          \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0015))

// 0X80AA0016 - Unexpected failure in macro expansion.
#define DXC_E_MACRO_EXPANSION_FAILURE                                          \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0016))

// 0X80AA0017 - DXIL optimization pass failed.
#define DXC_E_OPTIMIZATION_FAILED                                              \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0017))

// 0X80AA0018 - General internal error.
#define DXC_E_GENERAL_INTERNAL_ERROR                                           \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0018))

// 0X80AA0019 - Abort compilation error.
#define DXC_E_ABORT_COMPILATION_ERROR                                          \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x0019))

// 0X80AA001A - Error in extension mechanism.
#define DXC_E_EXTENSION_ERROR                                                  \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x001A))

// 0X80AA001B - LLVM Fatal Error
#define DXC_E_LLVM_FATAL_ERROR                                                 \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x001B))

// 0X80AA001C - LLVM Unreachable code
#define DXC_E_LLVM_UNREACHABLE                                                 \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x001C))

// 0X80AA001D - LLVM Cast Failure
#define DXC_E_LLVM_CAST_ERROR                                                  \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x001D))

// 0X80AA001E - External validator (DXIL.dll) required, and missing.
#define DXC_E_VALIDATOR_MISSING                                                \
  DXC_MAKE_HRESULT(DXC_SEVERITY_ERROR, FACILITY_DXC, (0x001E))

*/