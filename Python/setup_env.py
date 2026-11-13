"""One-command setup for Reboot The Earth body tracking.

Works on Windows, macOS and Linux after cloning the repo (GitHub Desktop or git).

    python setup_env.py          # create venv + install requirements
    python setup_env.py --cuda   # also install PyTorch with CUDA (NVIDIA GPU)
    python setup_env.py --run    # setup (if needed) then start body_tracking_sender.py

Anything after --run is passed to body_tracking_sender.py, for example:
    python setup_env.py --run --source webcam --camera 1
"""
import os
import subprocess
import sys
import venv

HERE = os.path.dirname(os.path.abspath(__file__))
VENV_DIR = os.path.join(HERE, "venv")
REQUIREMENTS = os.path.join(HERE, "requirements.txt")
CUDA_INDEX = "https://download.pytorch.org/whl/cu128"


def venv_python():
    if os.name == "nt":
        return os.path.join(VENV_DIR, "Scripts", "python.exe")
    return os.path.join(VENV_DIR, "bin", "python")


def run(cmd):
    print(">", " ".join(cmd))
    subprocess.check_call(cmd, cwd=HERE)


def setup(cuda=False):
    if sys.version_info < (3, 10):
        sys.exit("Python 3.10+ required (3.12 or 3.13 recommended).")
    if not os.path.exists(venv_python()):
        print("Creating virtual environment in", VENV_DIR)
        venv.EnvBuilder(with_pip=True).create(VENV_DIR)
    py = venv_python()
    run([py, "-m", "pip", "install", "--upgrade", "pip"])
    run([py, "-m", "pip", "install", "-r", REQUIREMENTS])
    if cuda:
        run([py, "-m", "pip", "install", "--force-reinstall",
             "torch", "torchvision", "--index-url", CUDA_INDEX])
    # Downloads hand_landmarker.task if it is missing.
    run([py, "-c", "import body_tracking_sender as b; b.HandDetector()"])
    print("\nSetup done. Start tracking with:  python setup_env.py --run")


def main():
    args = sys.argv[1:]
    if "--run" in args:
        i = args.index("--run")
        own, passthrough = args[:i], args[i + 1:]
        if not os.path.exists(venv_python()):
            setup(cuda="--cuda" in own)
        run([venv_python(), "body_tracking_sender.py", *passthrough])
        return
    setup(cuda="--cuda" in args)


if __name__ == "__main__":
    main()
