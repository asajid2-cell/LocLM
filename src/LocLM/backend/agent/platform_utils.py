"""Cross-platform utilities"""
import sys
import subprocess
from pathlib import Path
from typing import Optional

def is_windows() -> bool:
    return sys.platform == "win32"

def is_linux() -> bool:
    return sys.platform == "linux"

def get_platform_info() -> dict:
    return {
        "platform": sys.platform,
        "is_windows": is_windows(),
        "is_linux": is_linux(),
    }

def get_home_dir() -> Path:
    return Path.home()

def get_shell_command() -> str:
    return "cmd.exe" if is_windows() else "/bin/bash"

def get_shell_args(command: str) -> list:
    return ["/c", command] if is_windows() else ["-c", command]

class CommandResult:
    def __init__(self, stdout: str, stderr: str, return_code: int):
        self.stdout = stdout
        self.stderr = stderr
        self.return_code = return_code
        self.success = return_code == 0

async def run_command(command: str, cwd: Optional[str] = None, timeout: int = 30) -> CommandResult:
    try:
        if is_windows():
            args = ["cmd.exe", "/c", command]
        else:
            args = ["/bin/bash", "-c", command]

        result = subprocess.run(
            args,
            cwd=cwd,
            capture_output=True,
            text=True,
            timeout=timeout
        )
        return CommandResult(result.stdout, result.stderr, result.returncode)
    except subprocess.TimeoutExpired:
        return CommandResult("", "Command timed out", -1)
    except Exception as e:
        return CommandResult("", str(e), -1)
