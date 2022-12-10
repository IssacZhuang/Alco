$FolderAssemblies = "./Assemblies/"
$FolderPG2019 = "./Playground/2019/Assets/Plugins/Vocore"
$Modules = @("Vocore", "Vocore.Compute", "Vocore.Renderer")

foreach($module in $Modules){
    dotnet build $module/$module.csproj  --configuration Release
    cp ./$module/bin/Release/$module.dll $FolderAssemblies

    cp $FolderAssemblies/*dll $FolderPG2019
}