

::search all  .csproj exlude 'test'

setlocal

mkdir .\Assemblies

set "mode=Release"
if "%1"=="debug" set "mode=Debug"

del /q /s .\Assemblies\*
mkdir .\Assemblies

dotnet build UnityToolBox\UnityToolBox.csproj --configuration %mode% --output ./Assemblies
dotnet build Vocore\Vocore.csproj --configuration %mode% --output ./Assemblies
dotnet build Vocore.Framework\Vocore.Framework.csproj --configuration %mode% --output ./Assemblies

rmdir /q /s Vocore\obj
rmdir /q /s Vocore\bin  

if "%1"=="unity" (
    mkdir .\UnityContainer\Assets\Plugins
    del /q /s .\UnityContainer\Assets\Plugins\*
    copy .\Assemblies\Vocore.dll .\UnityContainer\Assets\Plugins
    copy .\Assemblies\Vocore.Framework.dll .\UnityContainer\Assets\Plugins
    copy .\Assemblies\UnityToolBox.dll .\UnityContainer\Assets\Plugins
)

if "%1"=="game" (
    copy .\Assemblies\Vocore.dll .\Game\CoreAssemblies\Vocore.dll
    copy .\Assemblies\Vocore.Framework.dll .\Game\CoreAssemblies\Vocore.Framework.dll
    copy .\Assemblies\Vocore.Test.Unity.dll .\Game\CoreAssemblies\Vocore.Test.Unity.dll
) 


