#pragma once
#undef NDEBUG

#include <cstdlib>

#ifndef CIMGUI_ERROR_CALLBACK_DEFINED
typedef void (*CimguiErrorCallback)(const char* expr, const char* file, int line);
#define CIMGUI_ERROR_CALLBACK_DEFINED
#endif
extern CimguiErrorCallback g_errorCallback;

#define IM_ASSERT(EXPR) do { \
    if (!(EXPR)) { \
        if (g_errorCallback) g_errorCallback(#EXPR, __FILE__, __LINE__); \
        else abort(); \
    } \
} while(0)
