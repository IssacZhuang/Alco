#!/bin/bash
set -e

ARCH=${1:-x64}

if [[ "$OSTYPE" == "darwin"* ]]; then
    RID="osx-$ARCH"
else
    RID="linux-$ARCH"
fi

BUILD_DIR="build/$RID"
mkdir -p "$BUILD_DIR"

cmake -S . -B "$BUILD_DIR" -DTARGET_RID="$RID"
cmake --build "$BUILD_DIR" --config Release

echo "Built for $RID. Output: Alco/Src/Alco.ImGUI/runtimes/$RID/native/"
