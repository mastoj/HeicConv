#!/bin/bash
dotnet publish -r win-x64 --configuration Release -o output/win-x64 src/HeiConv/HeiConv.fsproj 
dotnet publish -r osx-x64 --configuration Release -o output/osx-x64 src/HeiConv/HeiConv.fsproj 
