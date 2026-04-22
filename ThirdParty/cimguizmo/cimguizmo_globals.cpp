// Provides g_errorCallback for the cimguizmo DLL on Windows, where PE/COFF
// cannot import data symbols across DLLs. Alco-specific, not part of upstream cimguizmo.
#include "imgui.h"
CimguiErrorCallback g_errorCallback = nullptr;
