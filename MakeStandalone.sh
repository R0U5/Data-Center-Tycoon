#!/usr/bin/env bash
# Build a standalone, portable Linux copy of DATA TYCOON.
# The result needs no .NET install - run ./Standalone-linux/DataCenterTycoon.
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "The .NET 8 SDK is needed to build the standalone copy."
  echo "  https://dotnet.microsoft.com/download/dotnet/8.0"
  exit 1
fi

runtime="${1:-linux-x64}"
out="Standalone-${runtime#linux-}"

echo "Building a standalone copy for ${runtime} (this can take a minute)..."
dotnet publish -c Release -r "${runtime}" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -o "${out}"

echo
echo "Done. Portable build is in ./${out}/"
echo "Run it with:  ./${out}/DataCenterTycoon"
