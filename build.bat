

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


