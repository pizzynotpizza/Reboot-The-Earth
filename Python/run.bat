@echo off
REM Start body tracking. Extra args pass through, e.g. run.bat --source webcam
cd /d "%~dp0"
if not exist venv\Scripts\python.exe (
    echo venv not found. Run setup.bat first.
    pause
    exit /b 1
)
venv\Scripts\python.exe body_tracking_sender.py %*
