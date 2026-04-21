#pragma once
#undef NDEBUG

#include <cstdlib>

typedef void (*CimguiErrorCallback)(const char* expr, const char* file, int line);
extern CimguiErrorCallback g_errorCallback;

#define IM_ASSERT(EXPR) do { \
    if (!(EXPR)) { \
        if (g_errorCallback) g_errorCallback(#EXPR, __FILE__, __LINE__); \
        else abort(); \
    } \
} while(0)
