$FolderAssemblies = "../Assemblies/Rimworld"
$FolderRimworld = "../RimAssemblies/"
$Modules = @("MuzzleFlash", "AirDefenseSystem", "EquipmentEx", "EquipmentEx.CE" ,"AssemblyHotReload")

rm $FolderAssemblies/*.dll

foreach($module in $Modules){
    dotnet build $FolderRimworld/$module/$module.csproj  --configuration Release
    cp $FolderRimworld/$module/bin/Release/$module.dll $FolderAssemblies
}