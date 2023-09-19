::buidl Vocore.Test and run
setlocal

set "mode=Release"

if "%1"=="unity" (
    mkdir .\Game\CoreAssemblies
    dotnet build Vocore.Test.Unity/Vocore.Test.Unity.csproj  --configuration %mode% --output ./Assemblies
    copy .\Assemblies\Vocore.Test.Unity.dll .\Game\CoreAssemblies\Vocore.Test.Unity.dll
    .\Game\Game.exe test
    exit
)


if "%1"=="debug" set "mode=Debug"

dotnet build Vocore.Test/Vocore.Test.csproj  --configuration %mode%
cd Vocore.Test/bin/%mode%/net6.0
Vocore.Test.exe
pause