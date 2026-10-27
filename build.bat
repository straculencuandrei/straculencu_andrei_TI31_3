@echo off
setlocal

set "VS_WHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VS_WHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VS_WHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do (
        set "VS_DIR=%%i"
    )
)

if defined VS_DIR (
    if exist "%VS_DIR%\VC\Auxiliary\Build\vcvars64.bat" (
        call "%VS_DIR%\VC\Auxiliary\Build\vcvars64.bat" >nul 2>&1
    )
)

echo Compiling extract_text.cpp...
cl /EHsc /O2 /std:c++17 extract_text.cpp
if %ERRORLEVEL% equ 0 (
    del extract_text.obj >nul 2>&1
    echo Compilation successful: extract_text.exe
) else (
    echo Compilation failed: extract_text.cpp
)

echo.
echo Compiling PorterStemmerRunner.cs...
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=csc.exe"
"%CSC%" /nologo /optimize+ /out:PorterStemmerRunner.exe PorterStemmer.cs Document.cs PorterStemmerRunner.cs
if %ERRORLEVEL% equ 0 (
    echo Compilation successful: PorterStemmerRunner.exe
) else (
    echo Compilation failed: PorterStemmerRunner.cs
)

