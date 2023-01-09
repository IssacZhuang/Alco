$FolderAssemblies = "../Assemblies/Rimworld"
$FolderVodkaTool = "../VodkaRimworld/"
$Modules = @("MuzzleFlash", "MechTakeAmmoCE")

rm $FolderAssemblies/*.dll

foreach($module in $Modules){
    dotnet build $FolderVodkaTool/$module/$module.csproj  --configuration Release
    cp $FolderVodkaTool/$module/bin/Release/$module.dll $FolderAssemblies
}