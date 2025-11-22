"""
File Change Tracker - Captures file modifications for diff view.

Tracks:
- File reads (to capture original content before modifications)
- File writes (to capture new content)
- Generates unified diffs for display
"""
import difflib
from dataclasses import dataclass, field
from typing import Optional
from pathlib import Path
from .models import FileChange


@dataclass
class TrackedFile:
    """A file being tracked for changes"""
    path: str
    original_content: Optional[str] = None
    new_content: Optional[str] = None
    exists_originally: bool = True

    @property
    def has_changes(self) -> bool:
        return self.original_content != self.new_content

    @property
    def operation(self) -> str:
        if not self.exists_originally and self.new_content is not None:
            return "create"
        elif self.original_content is not None and self.new_content is None:
            return "delete"
        elif self.has_changes:
            return "modify"
        return "unchanged"


class FileChangeTracker:
    """Tracks file modifications during agent execution"""

    def __init__(self, workspace: Optional[Path] = None):
        self.workspace = workspace
        self.tracked_files: dict[str, TrackedFile] = {}
        self._enabled = True

    def set_workspace(self, workspace: Path):
        """Update workspace path"""
        self.workspace = workspace

    def set_enabled(self, enabled: bool):
        """Enable or disable tracking"""
        self._enabled = enabled

    def clear(self):
        """Clear all tracked files"""
        self.tracked_files.clear()

    def record_read(self, path: str, content: str, exists: bool = True):
        """Record a file read (captures original content)"""
        if not self._enabled:
            return

        normalized = self._normalize_path(path)
        if normalized not in self.tracked_files:
            self.tracked_files[normalized] = TrackedFile(
                path=normalized,
                original_content=content if exists else None,
                exists_originally=exists
            )

    def record_write(self, path: str, content: str):
        """Record a file write (captures new content)"""
        if not self._enabled:
            return

        normalized = self._normalize_path(path)
        if normalized in self.tracked_files:
            self.tracked_files[normalized].new_content = content
        else:
            # File wasn't read first - it's a new file
            self.tracked_files[normalized] = TrackedFile(
                path=normalized,
                original_content=None,
                new_content=content,
                exists_originally=False
            )

    def record_delete(self, path: str):
        """Record a file deletion"""
        if not self._enabled:
            return

        normalized = self._normalize_path(path)
        if normalized in self.tracked_files:
            self.tracked_files[normalized].new_content = None
        else:
            self.tracked_files[normalized] = TrackedFile(
                path=normalized,
                original_content=None,
                new_content=None,
                exists_originally=True
            )

    def _normalize_path(self, path: str) -> str:
        """Normalize path for consistent tracking"""
        return path.replace("\\", "/").strip("/")

    def get_changes(self) -> list[FileChange]:
        """Get all file changes as FileChange objects"""
        changes = []
        for tracked in self.tracked_files.values():
            if tracked.has_changes or tracked.operation != "unchanged":
                changes.append(FileChange(
                    path=tracked.path,
                    operation=tracked.operation,
                    old_content=tracked.original_content,
                    new_content=tracked.new_content
                ))
        return changes

    def generate_diff(self, path: str) -> Optional[str]:
        """Generate a unified diff for a specific file"""
        normalized = self._normalize_path(path)
        tracked = self.tracked_files.get(normalized)

        if not tracked or not tracked.has_changes:
            return None

        old_lines = (tracked.original_content or "").splitlines(keepends=True)
        new_lines = (tracked.new_content or "").splitlines(keepends=True)

        # Ensure lines end with newline for proper diff
        if old_lines and not old_lines[-1].endswith('\n'):
            old_lines[-1] += '\n'
        if new_lines and not new_lines[-1].endswith('\n'):
            new_lines[-1] += '\n'

        diff = difflib.unified_diff(
            old_lines,
            new_lines,
            fromfile=f"a/{tracked.path}",
            tofile=f"b/{tracked.path}"
        )

        return "".join(diff)

    def generate_all_diffs(self) -> str:
        """Generate diffs for all changed files"""
        diffs = []
        for path in self.tracked_files:
            diff = self.generate_diff(path)
            if diff:
                diffs.append(diff)
        return "\n".join(diffs)

    def get_summary(self) -> dict:
        """Get a summary of all changes"""
        changes = self.get_changes()
        return {
            "total_files": len(changes),
            "created": sum(1 for c in changes if c.operation == "create"),
            "modified": sum(1 for c in changes if c.operation == "modify"),
            "deleted": sum(1 for c in changes if c.operation == "delete"),
            "files": [{"path": c.path, "operation": c.operation} for c in changes]
        }
