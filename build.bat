@echo off
REM Build script for USIM C# conversion

echo Building USIM and Chaos C# projects...
echo.

REM Check if .NET SDK is installed
dotnet --version >nul 2>&1
if %errorlevel% neq 0 (
    echo Error: .NET SDK not found. Please install .NET 8.0 SDK or later.
    exit /b 1
)

REM Display .NET version
echo Using .NET version:
dotnet --version
echo.

REM Clean previous builds
echo Cleaning previous builds...
dotnet clean >nul 2>&1
echo.

REM Restore dependencies
echo Restoring dependencies...
dotnet restore
if %errorlevel% neq 0 (
    echo Error: Failed to restore dependencies
    exit /b 1
)
echo.

REM Build all projects
echo Building all projects...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo Error: Build failed
    exit /b 1
)
echo.

echo Build completed successfully!
echo.
echo To run USIM:
echo   dotnet run --project usim-cs --configuration Release
echo.
echo To run with options:
echo   dotnet run --project usim-cs --configuration Release -- --help
echo.

exit /b 0
