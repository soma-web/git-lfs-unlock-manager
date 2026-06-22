@echo off
REM ============================================================
REM  Build script for Git LFS Lock Manager
REM  Run this from the project root (e:\Projects\LFS\)
REM ============================================================

echo [1/2] Publishing application...
dotnet publish -c Release -r win-x64 --self-contained true -o publish\
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: dotnet publish failed.
    exit /b 1
)

echo.
echo [2/2] Building installer...
REM Try both common Inno Setup install locations
set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set ISCC="C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if exist "C:\Program Files\Inno Setup 6\ISCC.exe"       set ISCC="C:\Program Files\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
    echo ERROR: Inno Setup 6 not found. Download from https://jrsoftware.org/isinfo.php
    exit /b 1
)

%ISCC% setup.iss
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Inno Setup compilation failed.
    exit /b 1
)

echo.
echo Done! Installer is in dist\
