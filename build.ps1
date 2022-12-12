$FolderAssemblies = "./Assemblies/"
$Modules = @("Vocore", "Vocore.Compute")

rm $FolderAssemblies/*.dll

foreach($module in $Modules){
    dotnet build $module/$module.csproj  --configuration Release
    cp ./$module/bin/Release/$module.dll $FolderAssemblies
}