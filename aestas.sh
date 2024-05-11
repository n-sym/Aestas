#!/bin/bash
dotnet build --configuration Release
dotnet bin/Debug/net8.0/aestas.dll %*