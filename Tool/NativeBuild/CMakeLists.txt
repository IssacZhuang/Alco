cmake_minimum_required(VERSION 3.16)
project(AlcoNativeBuild LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_C_STANDARD 11)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(TARGET_RID "" CACHE STRING "Target runtime identifier (e.g., win-x64)")
if(NOT TARGET_RID)
    message(FATAL_ERROR "TARGET_RID must be set. Example: -DTARGET_RID=win-x64")
endif()

# cimgui (has its own CMakeLists.txt)
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/../../ThirdParty/cimgui cimgui-build)

# cimguizmo (no CMakeLists.txt — build manually)
set(CIMGUIZMO_DIR ${CMAKE_CURRENT_SOURCE_DIR}/../../ThirdParty/cimguizmo)
add_library(cimguizmo SHARED
    ${CIMGUIZMO_DIR}/cimguizmo.cpp
    ${CIMGUIZMO_DIR}/ImGuizmo/ImGuizmo.cpp
)
target_include_directories(cimguizmo PUBLIC
    ${CIMGUIZMO_DIR}
    ${CIMGUIZMO_DIR}/ImGuizmo
    ${CMAKE_CURRENT_SOURCE_DIR}/../../ThirdParty/cimgui
    ${CMAKE_CURRENT_SOURCE_DIR}/../../ThirdParty/cimgui/imgui
)
target_compile_definitions(cimguizmo PUBLIC
    IMGUI_DISABLE_OBSOLETE_FUNCTIONS=1
    IMGUI_USER_CONFIG="../imconfig_alco.h"
)
if(WIN32)
    target_compile_definitions(cimguizmo PUBLIC IMGUI_IMPL_API=extern\t\"C\"\t__declspec\(dllexport\))
else()
    target_compile_definitions(cimguizmo PUBLIC IMGUI_IMPL_API=extern\t\"C\"\t)
endif()
set_target_properties(cimguizmo PROPERTIES PREFIX "")

# Export all symbols from cimgui so cimguizmo can use ImGui C++ symbols
set_target_properties(cimgui PROPERTIES WINDOWS_EXPORT_ALL_SYMBOLS ON)
target_link_libraries(cimguizmo cimgui)

# Output to Alco.ImGUI runtimes directory
set(OUTPUT_DIR ${CMAKE_CURRENT_SOURCE_DIR}/../../Src/Alco.ImGUI/runtimes/${TARGET_RID}/native)
file(MAKE_DIRECTORY ${OUTPUT_DIR})

set(_props
    RUNTIME_OUTPUT_DIRECTORY
    LIBRARY_OUTPUT_DIRECTORY
    ARCHIVE_OUTPUT_DIRECTORY
)
foreach(_prop ${_props})
    set_target_properties(cimgui cimguizmo PROPERTIES ${_prop} ${OUTPUT_DIR})
    set_target_properties(cimgui cimguizmo PROPERTIES ${_prop}_RELEASE ${OUTPUT_DIR})
    set_target_properties(cimgui cimguizmo PROPERTIES ${_prop}_DEBUG ${OUTPUT_DIR})
endforeach()
