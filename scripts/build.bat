@echo off
REM Build and publish script for 9router-plus

echo Building and publishing 9router-plus...
echo.

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Navigate to solution root (one level up from scripts)
cd /d "%SCRIPT_DIR%.."

REM Set output directory to artifacts/publish
set OUTPUT_DIR=%CD%\artifacts\publish

echo Publishing to: %OUTPUT_DIR%
echo.

REM Clean the output directory first
if exist "%OUTPUT_DIR%\win-x64" (
    echo Cleaning old build...
    rmdir /s /q "%OUTPUT_DIR%\win-x64"
)

REM Publish the application
.dotnet\dotnet.exe publish src\RouterPlus.App\RouterPlus.App.csproj ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained false ^
    --output "%OUTPUT_DIR%\win-x64"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed!
    exit /b %ERRORLEVEL%
)

echo.
echo Build completed successfully!
echo Output: %OUTPUT_DIR%\win-x64\
