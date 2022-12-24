$FolderAssemblies = "../Assemblies/"
$FolderPG2019 = "../Playground/2019/Assets/Plugins/Vocore"
$Modules = @("Vocore", "Vocore.AssetsLib")

rm $FolderPG2019/*.dll
rm $FolderAssemblies/*.dll

foreach($module in $Modules){
    dotnet build ../$module/$module.csproj  --configuration Release
    cp ../$module/bin/Release/$module.dll $FolderAssemblies
    cp $FolderAssemblies/$module.dll $FolderPG2019
}