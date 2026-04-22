#!/bin/bash
set -e

ARCH=${1:-arm64}
RID="android-$ARCH"

case $ARCH in
    arm64)
        ANDROID_ABI="arm64-v8a"
        ;;
    x64)
        ANDROID_ABI="x86_64"
        ;;
    *)
        echo "Unsupported Android arch: $ARCH"
        exit 1
        ;;
esac

if [ -z "$ANDROID_NDK_HOME" ]; then
    echo "ANDROID_NDK_HOME is not set"
    exit 1
fi

BUILD_DIR="build/$RID"
mkdir -p "$BUILD_DIR"

cmake -S . -B "$BUILD_DIR" \
    -DTARGET_RID="$RID" \
    -DCMAKE_TOOLCHAIN_FILE="$ANDROID_NDK_HOME/build/cmake/android.toolchain.cmake" \
    -DANDROID_ABI="$ANDROID_ABI" \
    -DANDROID_PLATFORM=android-28

cmake --build "$BUILD_DIR" --config Release

echo "Built for $RID. Output: Alco/Src/Alco.ImGUI/runtimes/$RID/native/"
