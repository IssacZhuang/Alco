del /q /s .\Assemblies\*

::search all  .csproj exlude 'test'

setlocal

set "mode=Release"
if "%1"=="debug" set "mode=Debug"


for /f "delims=" %%a in ('dir /b /s Vocore*.csproj ^| findstr /v /i "test"') do (
    echo %%a
    dotnet build "%%a" --configuration %mode% --output ./Assemblies
)

move .\Assemblies.\Vocore.dll .\Assemblies.\0-Vocore.dll
move .\Assemblies.\Vocore.pdb .\Assemblies.\0-Vocore.pdb

rmdir /q /s obj
rmdir /q /s bin

rmdir /q /s Vocore\obj
rmdir /q /s Vocore\bin