"""
Tool Middleware - Normalizes and validates tool calls before execution.

This middleware intercepts LLM tool calls and:
1. Fuzzy-matches tool names to handle typos/variations
2. Normalizes argument names to expected schema
3. Validates required parameters
4. Sanitizes file paths for security
"""
from dataclasses import dataclass, field
from typing import Any, Callable, Optional
from pathlib import Path
import re
from difflib import get_close_matches


@dataclass
class ToolParameter:
    """Definition of a tool parameter"""
    name: str
    required: bool = True
    aliases: list[str] = field(default_factory=list)
    param_type: str = "string"  # string, path, content, command
    default: Any = None


@dataclass
class ToolSchema:
    """Schema definition for a tool"""
    name: str
    description: str
    parameters: list[ToolParameter]
    aliases: list[str] = field(default_factory=list)


# Define schemas for all built-in tools
TOOL_SCHEMAS: dict[str, ToolSchema] = {
    "list_directory": ToolSchema(
        name="list_directory",
        description="List contents of a directory",
        aliases=["ls", "dir", "list_dir", "listdir", "list_files"],
        parameters=[
            ToolParameter(
                name="path",
                required=False,
                aliases=["directory", "dir", "folder", "location"],
                param_type="path",
                default="."
            )
        ]
    ),
    "read_file": ToolSchema(
        name="read_file",
        description="Read the contents of a file",
        aliases=["cat", "view_file", "get_file", "open_file", "read", "view"],
        parameters=[
            ToolParameter(
                name="path",
                required=True,
                aliases=["filename", "file", "filepath", "file_path", "name"],
                param_type="path"
            )
        ]
    ),
    "write_file": ToolSchema(
        name="write_file",
        description="Write content to a file",
        aliases=["create_file", "save_file", "write", "save", "create", "put_file"],
        parameters=[
            ToolParameter(
                name="path",
                required=True,
                aliases=["filename", "file", "filepath", "file_path", "name"],
                param_type="path"
            ),
            ToolParameter(
                name="content",
                required=True,
                aliases=["text", "data", "body", "contents", "code"],
                param_type="content"
            )
        ]
    ),
    "run_command": ToolSchema(
        name="run_command",
        description="Execute a shell command",
        aliases=["exec", "execute", "shell", "bash", "cmd", "run", "terminal"],
        parameters=[
            ToolParameter(
                name="command",
                required=True,
                aliases=["cmd", "shell_command", "script", "commands"],
                param_type="command"
            )
        ]
    ),
    "search_files": ToolSchema(
        name="search_files",
        description="Search for files by pattern",
        aliases=["find", "find_files", "glob", "locate"],
        parameters=[
            ToolParameter(
                name="pattern",
                required=True,
                aliases=["glob", "query", "search", "name"],
                param_type="string"
            ),
            ToolParameter(
                name="path",
                required=False,
                aliases=["directory", "dir", "folder", "in"],
                param_type="path",
                default="."
            )
        ]
    ),
    "grep_files": ToolSchema(
        name="grep_files",
        description="Search for text within files",
        aliases=["grep", "search", "find_text", "search_content", "search_in_files"],
        parameters=[
            ToolParameter(
                name="pattern",
                required=True,
                aliases=["query", "search", "text", "regex", "term"],
                param_type="string"
            ),
            ToolParameter(
                name="path",
                required=False,
                aliases=["directory", "dir", "folder", "in"],
                param_type="path",
                default="."
            ),
            ToolParameter(
                name="file_pattern",
                required=False,
                aliases=["glob", "files", "filter", "extension"],
                param_type="string",
                default="*"
            )
        ]
    ),
    "edit_file": ToolSchema(
        name="edit_file",
        description="Edit a file by replacing text",
        aliases=["replace", "modify", "update_file", "patch"],
        parameters=[
            ToolParameter(
                name="path",
                required=True,
                aliases=["filename", "file", "filepath"],
                param_type="path"
            ),
            ToolParameter(
                name="old_text",
                required=True,
                aliases=["find", "search", "original", "from", "old"],
                param_type="content"
            ),
            ToolParameter(
                name="new_text",
                required=True,
                aliases=["replace", "replacement", "to", "new", "with"],
                param_type="content"
            ),
            ToolParameter(
                name="all_occurrences",
                required=False,
                aliases=["all", "global", "replace_all"],
                param_type="string",
                default=False
            )
        ]
    ),
    "create_directory": ToolSchema(
        name="create_directory",
        description="Create a new directory",
        aliases=["mkdir", "make_dir", "new_folder", "create_folder"],
        parameters=[
            ToolParameter(
                name="path",
                required=True,
                aliases=["directory", "dir", "folder", "name"],
                param_type="path"
            )
        ]
    ),
    "delete_file": ToolSchema(
        name="delete_file",
        description="Delete a file or empty directory",
        aliases=["rm", "remove", "delete", "unlink", "rmdir"],
        parameters=[
            ToolParameter(
                name="path",
                required=True,
                aliases=["filename", "file", "filepath", "target"],
                param_type="path"
            )
        ]
    ),
    "list_tools": ToolSchema(
        name="list_tools",
        description="List all available tools",
        aliases=["help", "tools", "available_tools"],
        parameters=[]
    ),
    "read_tool": ToolSchema(
        name="read_tool",
        description="Read a tool's source code",
        aliases=["view_tool", "tool_source"],
        parameters=[
            ToolParameter(
                name="name",
                required=True,
                aliases=["tool", "tool_name"],
                param_type="string"
            )
        ]
    ),
}


class ToolMiddleware:
    """Middleware that normalizes and validates tool calls"""

    def __init__(self, workspace: Optional[Path] = None):
        self.workspace = workspace
        self._build_alias_map()

    def _build_alias_map(self):
        """Build a reverse lookup map from aliases to canonical tool names"""
        self.tool_alias_map: dict[str, str] = {}
        self.all_tool_names: list[str] = []

        for canonical_name, schema in TOOL_SCHEMAS.items():
            self.tool_alias_map[canonical_name] = canonical_name
            self.all_tool_names.append(canonical_name)
            for alias in schema.aliases:
                self.tool_alias_map[alias.lower()] = canonical_name
                self.all_tool_names.append(alias.lower())

    def set_workspace(self, workspace: Path):
        """Update the workspace path"""
        self.workspace = workspace

    def normalize_tool_call(self, tool_call: dict) -> tuple[dict, list[str]]:
        """
        Normalize a tool call to match expected schema.

        Returns:
            tuple: (normalized_tool_call, list_of_warnings)
        """
        warnings = []

        if not isinstance(tool_call, dict):
            return {"tool": "unknown", "args": {}}, ["Invalid tool call format"]

        raw_tool_name = tool_call.get("tool", "").lower().strip()
        raw_args = tool_call.get("args", {})

        if not isinstance(raw_args, dict):
            raw_args = {}

        # Step 1: Resolve tool name
        canonical_name = self._resolve_tool_name(raw_tool_name)
        if canonical_name != raw_tool_name:
            warnings.append(f"Tool '{raw_tool_name}' resolved to '{canonical_name}'")

        # Step 2: Get schema and normalize arguments
        schema = TOOL_SCHEMAS.get(canonical_name)
        if not schema:
            # Unknown tool - pass through but warn
            warnings.append(f"Unknown tool '{canonical_name}' - passing through")
            return {"tool": canonical_name, "args": raw_args}, warnings

        # Step 3: Normalize arguments
        normalized_args, arg_warnings = self._normalize_arguments(raw_args, schema)
        warnings.extend(arg_warnings)

        # Step 4: Validate paths if workspace is set
        if self.workspace:
            normalized_args, path_warnings = self._validate_paths(normalized_args, schema)
            warnings.extend(path_warnings)

        return {"tool": canonical_name, "args": normalized_args}, warnings

    def _resolve_tool_name(self, raw_name: str) -> str:
        """Resolve a tool name using aliases and fuzzy matching"""
        # Direct match
        if raw_name in self.tool_alias_map:
            return self.tool_alias_map[raw_name]

        # Fuzzy match
        matches = get_close_matches(raw_name, self.all_tool_names, n=1, cutoff=0.6)
        if matches:
            matched = matches[0]
            return self.tool_alias_map.get(matched, matched)

        return raw_name

    def _normalize_arguments(self, raw_args: dict, schema: ToolSchema) -> tuple[dict, list[str]]:
        """Normalize argument names to match schema"""
        normalized = {}
        warnings = []
        used_raw_keys = set()

        for param in schema.parameters:
            value = None
            matched_key = None

            # Check canonical name first
            if param.name in raw_args:
                value = raw_args[param.name]
                matched_key = param.name
            else:
                # Check aliases
                for alias in param.aliases:
                    if alias in raw_args:
                        value = raw_args[alias]
                        matched_key = alias
                        warnings.append(f"Arg '{alias}' normalized to '{param.name}'")
                        break

                # Fuzzy match on remaining keys
                if value is None:
                    remaining_keys = [k for k in raw_args.keys() if k not in used_raw_keys]
                    all_names = [param.name] + param.aliases
                    for key in remaining_keys:
                        matches = get_close_matches(key.lower(), [n.lower() for n in all_names], n=1, cutoff=0.7)
                        if matches:
                            value = raw_args[key]
                            matched_key = key
                            warnings.append(f"Fuzzy matched '{key}' to '{param.name}'")
                            break

            if matched_key:
                used_raw_keys.add(matched_key)

            # Apply default or use found value
            if value is not None:
                normalized[param.name] = value
            elif param.default is not None:
                normalized[param.name] = param.default
            elif param.required:
                warnings.append(f"Missing required parameter '{param.name}'")

        return normalized, warnings

    def _validate_paths(self, args: dict, schema: ToolSchema) -> tuple[dict, list[str]]:
        """Validate and sanitize file paths"""
        warnings = []
        validated = args.copy()

        for param in schema.parameters:
            if param.param_type == "path" and param.name in validated:
                path_value = validated[param.name]
                if isinstance(path_value, str):
                    # Remove any dangerous patterns
                    sanitized = self._sanitize_path(path_value)
                    if sanitized != path_value:
                        warnings.append(f"Path sanitized: '{path_value}' -> '{sanitized}'")
                    validated[param.name] = sanitized

        return validated, warnings

    def _sanitize_path(self, path: str) -> str:
        """Sanitize a file path for security"""
        # Remove null bytes
        path = path.replace('\x00', '')

        # Normalize slashes
        path = path.replace('\\', '/')

        # Remove leading slashes for relative paths
        if self.workspace:
            path = path.lstrip('/')

        # Collapse multiple slashes
        path = re.sub(r'/+', '/', path)

        # Remove dangerous traversal patterns
        parts = path.split('/')
        safe_parts = []
        for part in parts:
            if part == '..':
                if safe_parts:
                    safe_parts.pop()
                # else: trying to go above root, ignore
            elif part and part != '.':
                safe_parts.append(part)

        return '/'.join(safe_parts) or '.'

    def get_tool_help(self, tool_name: Optional[str] = None) -> str:
        """Get help text for tools"""
        if tool_name:
            schema = TOOL_SCHEMAS.get(self._resolve_tool_name(tool_name))
            if schema:
                params = ", ".join(
                    f"{p.name}{'?' if not p.required else ''}"
                    for p in schema.parameters
                )
                aliases = ", ".join(schema.aliases[:3]) if schema.aliases else "none"
                return f"{schema.name}({params}): {schema.description}\n  Aliases: {aliases}"
            return f"Unknown tool: {tool_name}"

        # List all tools
        lines = ["Available tools:"]
        for name, schema in TOOL_SCHEMAS.items():
            params = ", ".join(
                f"{p.name}{'?' if not p.required else ''}"
                for p in schema.parameters
            )
            lines.append(f"  {name}({params}): {schema.description}")
        return "\n".join(lines)
