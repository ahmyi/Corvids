@echo off
REM Run all Corvids tests. Double-click, or run from any shell.
dotnet test "%~dp0Corvids.sln" -c Release --nologo
