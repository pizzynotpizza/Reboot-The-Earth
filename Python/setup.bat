@echo off
REM One-time setup: create venv and install libraries.
REM Usage: double-click, or "setup.bat cuda" for NVIDIA GPU
cd /d "%~dp0"
if not exist venv (
    echo Creating virtual environment...
    py -3.13 -m venv venv 2>nul || python -m venv venv || (echo Python not found. Install Python 3.12 or 3.13 and tick "Add to PATH". & pause & exit /b 1)
)
venv\Scripts\python.exe -m pip install --upgrade pip
venv\Scripts\python.exe -m pip install -r requirements.txt
if /i "%1"=="cuda" (
    echo Installing PyTorch with CUDA...
    venv\Scripts\python.exe -m pip install --force-reinstall torch torchvision --index-url https://download.pytorch.org/whl/cu128
)
echo Downloading MediaPipe hand model...
venv\Scripts\python.exe -c "import body_tracking_sender as b; b.HandDetector()"
echo.
echo Setup done. Run: run.bat   (force webcam: run.bat --source webcam)
pause
