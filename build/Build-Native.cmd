@echo off
setlocal
cd /d "%~dp0"

set "PROJECT_ROOT=%~dp0.."
set "SOURCE_DIR=%PROJECT_ROOT%\src\native"
set "SFIZZ_DIR=%PROJECT_ROOT%\third_party\sfizz"
set "OUTPUT_DIR=%PROJECT_ROOT%\build\out\native"

where cl >nul 2>nul
if errorlevel 1 (
    echo Run this script from an x64 Visual Studio developer command prompt.
    exit /b 2
)

if not exist "%SFIZZ_DIR%\include\vendor\sfizz.h" (
    echo Missing sfizz headers.
    exit /b 3
)
if not exist "%SFIZZ_DIR%\lib\sfizz.lib" (
    echo Missing sfizz.lib.
    exit /b 4
)

if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

rem EventQueue intentionally uses cache-line alignment; C4324 reports that padding.
cl /nologo /c /O2 /std:c++17 /EHsc /MT /W4 /wd4324 /permissive- /I"%SFIZZ_DIR%\include" /Fo:"%OUTPUT_DIR%\PianoCore.obj" "%SOURCE_DIR%\PianoCore.cpp"
if errorlevel 1 exit /b 1

link /nologo /DLL /OUT:"%OUTPUT_DIR%\RaftPianoRebornCore.dll" /IMPLIB:"%OUTPUT_DIR%\RaftPianoRebornCore.lib" "%OUTPUT_DIR%\PianoCore.obj" /LIBPATH:"%SFIZZ_DIR%\lib" sfizz.lib user32.lib winmm.lib
exit /b %errorlevel%
