@echo off
cd /d D:\codex_dk\ApiMonitor
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" ApiMonitor.csproj /noautoresponse /p:Configuration=Release /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:AppxPackage=true /p:GenerateAppxPackageOnBuild=true /p:UapAppxPackageBuildMode=SideloadOnly /p:CreateAppxBundle=false /verbosity:minimal /nologo
echo MSBUILD_EXIT=%ERRORLEVEL%
