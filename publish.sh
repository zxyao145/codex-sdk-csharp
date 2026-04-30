
#!/bin/bash

# 版本号参数，默认为 0.0.1-preview.1
VERSION="${1:-0.0.1-preview.1}"

dotnet restore
dotnet build ./codex.slnx -c Release --no-restore -p:PackageVersion=$VERSION
dotnet pack ./codex.slnx -o ./releases -c Release --no-build -p:PackageVersion=$VERSION
