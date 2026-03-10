@echo off
echo Building Macro Overlay...
dotnet publish Macro.csproj -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true

