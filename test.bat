::buidl Vocore.Test and run
setlocal

set "mode=Release"

dotnet build Vocore.Test/Vocore.Test.csproj  --configuration %mode%
cd Vocore.Test/bin/%mode%/net6.0
Vocore.Test.exe
pause