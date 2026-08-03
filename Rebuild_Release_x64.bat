@echo off
setlocal
cd /d "%~dp0"

echo [1/3] NuGet restore...
dotnet restore "RadioPulseViewer\RadioPulseViewer.csproj"
if errorlevel 1 goto :error

echo [2/3] Clean...
dotnet clean "RadioPulseViewer\RadioPulseViewer.csproj" -c Release -p:Platform=x64 --no-restore
if errorlevel 1 goto :error

echo [3/3] Build...
dotnet build "RadioPulseViewer\RadioPulseViewer.csproj" -c Release -p:Platform=x64 --no-restore
if errorlevel 1 goto :error

echo.
echo Build completed.
echo EXE: "%~dp0RadioPulseViewer\bin\Release\net10.0-windows\RadioPulseViewer.exe"
pause
exit /b 0

:error
echo.
echo Build failed. Check the messages above.
pause
exit /b 1
