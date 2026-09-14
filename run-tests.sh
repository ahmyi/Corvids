#!/usr/bin/env bash
# Run all Corvids tests before pushing.
set -e
cd "$(dirname "$0")"
dotnet test Corvids.sln -c Release --nologo
