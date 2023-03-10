del /q /s .\Assemblies\*

::search all  .csproj exlude 'test'



for /f "delims=" %%a in ('dir /b /s Vocore*.csproj ^| findstr /v /i "test"') do (

    echo %%a
    dotnet build "%%a" --configuration Release --output ./Assemblies
)

for /f "delims=" %%a in ('dir /b /s RimAssemblies\*.csproj') do (
    echo %%a
    dotnet build "%%a" --configuration Release --output ./Assemblies/Rimworld
)

move .\Assemblies.\Vocore.dll .\Assemblies.\0-Vocore.dll
move .\Assemblies.\Vocore.pdb .\Assemblies.\0-Vocore.pdb

del /q /s .\Assemblies\Rimworld\Vocore*