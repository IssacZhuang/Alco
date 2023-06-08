

::search all  .csproj exlude 'test'

setlocal

mkdir .\Assemblies

set "mode=Release"
if "%1"=="debug" set "mode=Debug"

if "%1"=="unity" (
    dotnet build Vocore.Test.Unity\Vocore.Test.Unity.csproj --configuration %mode% --output ./Assemblies
    dotnet build UnityToolBox\UnityToolBox.csproj --configuration %mode% --output ./UnityContainer/Assets/Plugins
    copy .\Assemblies\Vocore.dll .\UnityContainer\Assets\Plugins
    exit
)

if "%1"=="all" (
    del /q /s .\Assemblies\*
    dotnet build UnityToolBox\UnityToolBox.csproj --configuration %mode% --output ./Assemblies
    dotnet build Vocore.Test.Unity\Vocore.Test.Unity.csproj --configuration %mode% --output ./Assemblies
    for /f "delims=" %%a in ('dir /b /s Vocore*.csproj ^| findstr /v /i "test"') do (
        echo %%a
        dotnet build "%%a" --configuration %mode% --output ./Assemblies
    )
    rmdir /q /s obj
    rmdir /q /s bin

    rmdir /q /s Vocore\obj
    rmdir /q /s Vocore\bin  
    exit
)


if "%1"=="game" (
    dotnet build Vocore.Test.Unity\Vocore.Test.Unity.csproj --configuration %mode% --output ./Assemblies
    mkdir .\Game\CoreAssemblies
    copy .\Assemblies\Vocore.Test.Unity.dll .\Game\CoreAssemblies\Vocore.Test.Unity.dll
    exit
) 

mkdir .\Assemblies

dotnet build UnityToolBox\UnityToolBox.csproj --configuration %mode% --output ./Assemblies
dotnet build Vocore\Vocore.csproj --configuration %mode% --output ./Assemblies
dotnet build Vocore.Test.Unity\Vocore.Test.Unity.csproj --configuration %mode% --output ./Assemblies


