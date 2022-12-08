dotnet build Vocore/Vocore.csproj  --configuration Release
dotnet build Vocore.Compute/Vocore.Compute.csproj  --configuration Release
dotnet build Vocore.Renderer/Vocore.Renderer.csproj  --configuration Release

cp ./Vocore/bin/Release/Vocore.dll ./Assemblies/
cp ./Vocore.Compute/bin/Release/Vocore.Compute.dll ./Assemblies/
cp ./Vocore.Renderer/bin/Release/Vocore.Renderer.dll ./Assemblies/