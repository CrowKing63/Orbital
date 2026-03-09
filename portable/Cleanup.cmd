@echo off
:: Orbital Portable - Settings Cleanup
:: Removes %APPDATA%\Orbital (settings, API key) left by the portable version.

setlocal

set "DATA_DIR=%APPDATA%\Orbital"

echo Orbital Portable Cleanup
echo ========================
echo.

if not exist "%DATA_DIR%" (
    echo No Orbital data found at:
    echo   %DATA_DIR%
    echo Nothing to remove.
    goto :done
)

echo The following folder will be permanently deleted:
echo   %DATA_DIR%
echo.
set /p CONFIRM=Are you sure? [Y/N]:

if /i "%CONFIRM%"=="Y" (
    rmdir /s /q "%DATA_DIR%"
    if errorlevel 1 (
        echo.
        echo ERROR: Could not remove the folder. It may be in use.
        echo Please close Orbital and try again, or delete manually:
        echo   %DATA_DIR%
    ) else (
        echo.
        echo Settings removed successfully.
    )
) else (
    echo.
    echo Cancelled. No files were removed.
)

:done
echo.
pause
endlocal
