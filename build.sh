#!/bin/bash
# Build script for USIM C# conversion

echo "Building USIM and Chaos C# projects..."
echo ""

# Check if .NET SDK is installed
if ! command -v dotnet &> /dev/null; then
    echo "Error: .NET SDK not found. Please install .NET 8.0 SDK or later."
    exit 1
fi

# Display .NET version
echo "Using .NET version:"
dotnet --version
echo ""

# Clean previous builds
echo "Cleaning previous builds..."
dotnet clean > /dev/null 2>&1
echo ""

# Restore dependencies
echo "Restoring dependencies..."
if ! dotnet restore; then
    echo "Error: Failed to restore dependencies"
    exit 1
fi
echo ""

# Build all projects
echo "Building all projects..."
if ! dotnet build -c Release; then
    echo "Error: Build failed"
    exit 1
fi
echo ""

echo "Build completed successfully!"
echo ""
echo "To run USIM:"
echo "  dotnet run --project usim-cs --configuration Release"
echo ""
echo "To run with options:"
echo "  dotnet run --project usim-cs --configuration Release -- --help"
echo ""

exit 0
