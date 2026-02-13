@echo off
setlocal

set "ROOT_DIR=%~dp0"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "VENV_DIR=%ROOT_DIR%\.venv"
set "SCRIPT_PATH=%ROOT_DIR%\enrich_word_lists.py"
set "REQUIREMENTS_PATH=%ROOT_DIR%\requirements.txt"

if not exist "%SCRIPT_PATH%" (
  echo Python script not found: "%SCRIPT_PATH%"
  exit /b 1
)

set "VENV_PYTHON="
if exist "%VENV_DIR%\Scripts\python.exe" set "VENV_PYTHON=%VENV_DIR%\Scripts\python.exe"
if not defined VENV_PYTHON if exist "%VENV_DIR%\bin\python" set "VENV_PYTHON=%VENV_DIR%\bin\python"
if not defined VENV_PYTHON if exist "%VENV_DIR%\bin\python3" set "VENV_PYTHON=%VENV_DIR%\bin\python3"

if not defined VENV_PYTHON (
  echo Creating virtual environment in "%VENV_DIR%" ...
  call :create_venv "%VENV_DIR%"
  if errorlevel 1 exit /b 1

  if exist "%VENV_DIR%\Scripts\python.exe" set "VENV_PYTHON=%VENV_DIR%\Scripts\python.exe"
  if not defined VENV_PYTHON if exist "%VENV_DIR%\bin\python" set "VENV_PYTHON=%VENV_DIR%\bin\python"
  if not defined VENV_PYTHON if exist "%VENV_DIR%\bin\python3" set "VENV_PYTHON=%VENV_DIR%\bin\python3"
)

if not defined VENV_PYTHON (
  echo Could not resolve Python executable inside ".venv".
  exit /b 1
)

"%VENV_PYTHON%" -m pip install --upgrade pip || exit /b 1
if exist "%REQUIREMENTS_PATH%" (
  "%VENV_PYTHON%" -m pip install -r "%REQUIREMENTS_PATH%" || exit /b 1
)

"%VENV_PYTHON%" "%SCRIPT_PATH%" %*
exit /b %errorlevel%

:create_venv
where python >nul 2>nul && python -m venv "%~1" && goto :eof
where py >nul 2>nul && py -3 -m venv "%~1" && goto :eof
where python3 >nul 2>nul && python3 -m venv "%~1" && goto :eof
echo Python is not available. Install Python 3 and retry.
exit /b 1
goto :eof
