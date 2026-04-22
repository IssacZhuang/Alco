param(
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64"
)

$rid = "win-$Arch"
$buildDir = "build/$rid"
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

$cmakeArch = if ($Arch -eq "x64") { "x64" } else { "ARM64" }

cmake -S . -B $buildDir `
    -DTARGET_RID="$rid" `
    -A $cmakeArch

cmake --build $buildDir --config Release

Write-Host "Built for $rid. Output: Alco/Src/Alco.ImGUI/runtimes/$rid/native/"
