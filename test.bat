::buidl Vocore.Test and run
setlocal

set "mode=Release"
if "%1"=="debug" set "mode=Debug"

dotnet build Vocore.Test/Vocore.Test.csproj  --configuration %mode%
cd Vocore.Test/bin/%mode%/
Vocore.Test.exe
pause