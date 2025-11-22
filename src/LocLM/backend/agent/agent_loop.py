"""Main agent loop that coordinates between LLM providers and tools"""
import json
import os
import re
import importlib.util
from pathlib import Path
from typing import Optional
import httpx
from .platform_utils import run_command
from .tool_middleware import ToolMiddleware
from .models import ToolCall, ToolResult, ToolCallStatus, AgentResponse, AgentPlan
from .planner import AgentPlanner
from .file_tracker import FileChangeTracker

class AgentLoop:
    def __init__(self):
        self.tools_dir = Path(__file__).parent.parent / "local_tools"
        self.conversation_history = []
        self.mode = "agent"  # "chat" or "agent" - default to agent mode
        self.workspace_dir: Path | None = None  # Current workspace directory

        # Tool middleware for normalization and validation
        self.middleware = ToolMiddleware()

        # Agent planner for multi-step operations
        self.planner = AgentPlanner()
        self.planning_enabled = True  # Can be toggled
        self.current_plan: Optional[AgentPlan] = None

        # File change tracker for diff view
        self.file_tracker = FileChangeTracker()

        # Provider configuration
        self.provider = os.getenv("LLM_PROVIDER", "groq")  # groq, ollama, openai
        self.groq_api_key = os.getenv("GROQ_API_KEY", "")
        self.groq_model = os.getenv("GROQ_MODEL", "llama-3.3-70b-versatile")
        self.ollama_url = os.getenv("OLLAMA_URL", "http://localhost:11434")
        self.ollama_model = os.getenv("OLLAMA_MODEL", "llama3.2:3b")

        self.chat_system_prompt = """You are a helpful AI coding assistant. Respond directly and conversationally to user questions. Do not attempt to use any tools or execute any commands."""

    def _build_agent_prompt(self) -> str:
        """Build agent system prompt with available tools and workspace context"""
        tools_info = self._get_tools_documentation()
        workspace = self._get_effective_workspace()
        workspace_info = str(workspace)

        return f"""You are a helpful AI coding assistant with access to tools to interact with the user's workspace.

## Current Workspace
{workspace_info}

## Available Tools

### File System Tools
- `list_directory(path)`: List contents of a directory. Use "." for current workspace root.
- `read_file(path)`: Read the contents of a file. Paths are relative to workspace.
- `write_file(path, content)`: Write content to a file. Creates parent directories if needed.

### Command Execution
- `run_command(command)`: Execute a shell command in the workspace directory.
  Examples: "dotnet build", "git status", "npm install", "python script.py"

### Tool Discovery
- `list_tools`: List all available tools
- `read_tool(name)`: Read a tool's source code to understand its usage

{tools_info}

## How to Use Tools

When you need to perform an action, respond with a JSON block:
```tool
{{
    "tool": "tool_name",
    "args": {{"param1": "value1", "param2": "value2"}}
}}
```

## Examples

**List workspace files:**
```tool
{{"tool": "list_directory", "args": {{"path": "."}}}}
```

**Read a file:**
```tool
{{"tool": "read_file", "args": {{"path": "src/main.py"}}}}
```

**Run a build:**
```tool
{{"tool": "run_command", "args": {{"command": "dotnet build"}}}}
```

**Write a file:**
```tool
{{"tool": "write_file", "args": {{"path": "src/utils.py", "content": "def hello():\\n    return 'Hello!'"}}}}
```

## Important Guidelines
1. All file paths are relative to the workspace root
2. Explore the workspace structure before making changes
3. Read existing files before modifying them
4. After tool execution, you'll receive the result and can continue or respond
5. When you have enough information, provide a helpful response WITHOUT a tool block
6. **CRITICAL**: For multiline content in write_file, use \\n for newlines. Do NOT use triple quotes or raw strings.
   Example: {{"tool": "write_file", "args": {{"path": "test.py", "content": "def main():\\n    print('hello')\\n"}}}}
"""

    def _get_tools_documentation(self) -> str:
        """Get documentation for custom tools in local_tools directory"""
        custom_tools = []

        if self.tools_dir.exists():
            for tool_file in sorted(self.tools_dir.glob("*.py")):
                if tool_file.name == "__init__.py":
                    continue

                try:
                    content = tool_file.read_text()
                    # Extract first line of docstring
                    if '"""' in content:
                        doc_start = content.find('"""') + 3
                        doc_end = content.find('"""', doc_start)
                        if doc_end > doc_start:
                            desc = content[doc_start:doc_end].strip().split('\n')[0]
                            custom_tools.append(f"- `{tool_file.stem}`: {desc}")
                except Exception as e:
                    print(f"[Tool Discovery] Error reading {tool_file.name}: {e}")

        if custom_tools:
            return "### Custom Tools\n" + "\n".join(custom_tools)
        return ""

    def set_workspace(self, path: str) -> bool:
        """Set the current workspace directory"""
        try:
            workspace_path = Path(path).resolve()
            if workspace_path.exists() and workspace_path.is_dir():
                self.workspace_dir = workspace_path
                self.middleware.set_workspace(workspace_path)
                print(f"[AgentLoop] Workspace set to: {self.workspace_dir}")
                return True
        except Exception as e:
            print(f"[AgentLoop] Error setting workspace: {e}")
        return False

    def get_workspace(self) -> str | None:
        """Get the current workspace directory"""
        return str(self.workspace_dir) if self.workspace_dir else None

    def _get_effective_workspace(self) -> Path:
        """Get the effective workspace, falling back to cwd if not set"""
        if self.workspace_dir and self.workspace_dir.exists():
            return self.workspace_dir
        return Path.cwd()

    def _resolve_path(self, path: str) -> Path:
        """Resolve a path relative to workspace, with safety checks"""
        workspace = self._get_effective_workspace()

        if not path or path == ".":
            return workspace

        # Handle absolute paths - check if within workspace
        path_obj = Path(path)
        if path_obj.is_absolute():
            resolved = path_obj.resolve()
        else:
            # Relative path - resolve from workspace
            resolved = (workspace / path).resolve()

        # Security: ensure path is within workspace
        try:
            resolved.relative_to(workspace)
        except ValueError:
            print(f"[Security] Path '{path}' is outside workspace, restricting to workspace")
            return workspace

        return resolved

    def set_mode(self, mode: str):
        """Set the agent mode: 'chat' or 'agent'"""
        if mode in ["chat", "agent"]:
            self.mode = mode
            return True
        return False

    def get_mode(self) -> str:
        return self.mode

    def get_model_info(self) -> dict:
        """Return current model configuration"""
        if self.provider == "groq":
            return {"provider": "Groq", "model": self.groq_model}
        elif self.provider == "ollama":
            return {"provider": "Ollama", "model": self.ollama_model}
        return {"provider": "Unknown", "model": "N/A"}

    def clear_history(self):
        """Clear conversation history for a new session"""
        self.conversation_history = []
        self.current_plan = None
        self.file_tracker.clear()

    def get_file_changes(self) -> dict:
        """Get file changes for diff view"""
        return self.file_tracker.get_summary()

    def get_file_diff(self, path: str) -> str | None:
        """Get unified diff for a specific file"""
        return self.file_tracker.generate_diff(path)

    def get_all_diffs(self) -> str:
        """Get all file diffs"""
        return self.file_tracker.generate_all_diffs()

    def set_planning_enabled(self, enabled: bool):
        """Enable or disable the planning phase"""
        self.planning_enabled = enabled

    def get_planning_enabled(self) -> bool:
        """Check if planning phase is enabled"""
        return self.planning_enabled

    def get_current_plan(self) -> Optional[AgentPlan]:
        """Get the current execution plan if any"""
        return self.current_plan

    async def generate_plan(self, user_request: str) -> Optional[AgentPlan]:
        """Generate an execution plan for a user request"""
        # Build context for planner
        workspace = self._get_effective_workspace()
        context_lines = [f"Workspace: {workspace}"]

        # Get a quick directory listing for context
        try:
            items = list(workspace.iterdir())[:20]
            context_lines.append("Top-level contents:")
            for item in items:
                item_type = "dir" if item.is_dir() else "file"
                context_lines.append(f"  {item_type}: {item.name}")
        except Exception:
            pass

        self.planner.set_context("\n".join(context_lines))

        # Generate plan using LLM
        planning_prompt = self.planner.build_planning_prompt(user_request)

        # Temporarily add planning prompt to get LLM response
        temp_history = self.conversation_history.copy()
        self.conversation_history.append({"role": "user", "content": planning_prompt})

        response = await self._call_llm()
        self.conversation_history = temp_history  # Restore history

        if response:
            plan = self.planner.parse_plan(response)
            if plan:
                self.current_plan = plan
                print(f"[Planner] Generated plan with {len(plan.steps)} steps")
                return plan

        return None

    async def check_provider_health(self) -> dict:
        """Check if the current provider is available"""
        if self.provider == "groq":
            try:
                async with httpx.AsyncClient() as client:
                    response = await client.get(
                        "https://api.groq.com/openai/v1/models",
                        headers={"Authorization": f"Bearer {self.groq_api_key}"},
                        timeout=5.0
                    )
                    return {"available": response.status_code == 200, "provider": "Groq"}
            except:
                return {"available": False, "provider": "Groq"}

        elif self.provider == "ollama":
            try:
                async with httpx.AsyncClient() as client:
                    # Check if Ollama server is running
                    response = await client.get(f"{self.ollama_url}/api/tags", timeout=5.0)
                    if response.status_code != 200:
                        return {"available": False, "provider": "Ollama", "error": "Ollama server not responding"}

                    # Check if the configured model is available
                    data = response.json()
                    models = data.get("models", [])
                    model_names = [m.get("name", "").split(":")[0] for m in models]

                    # Check if our model is in the list (handle both "qwen2.5-coder" and "qwen2.5-coder:7b")
                    model_base = self.ollama_model.split(":")[0]
                    model_available = any(model_base in name or name in model_base for name in model_names)

                    if not model_available:
                        return {
                            "available": False,
                            "provider": "Ollama",
                            "error": f"Model '{self.ollama_model}' not found. Run: ollama pull {self.ollama_model}"
                        }

                    return {"available": True, "provider": "Ollama"}
            except httpx.ConnectError:
                return {"available": False, "provider": "Ollama", "error": "Cannot connect to Ollama. Run: ollama serve"}
            except Exception as e:
                return {"available": False, "provider": "Ollama", "error": str(e)}

        return {"available": False, "provider": "Unknown"}

    async def process(self, user_prompt: str) -> dict:
        """Process a user prompt and return a response.

        Returns:
            dict with 'response' (str) and 'tool_calls' (list) for API compatibility
        """
        print(f"[Agent] Processing prompt in mode: {self.mode}")
        print(f"[Agent] Workspace: {self.workspace_dir}")
        self.conversation_history.append({"role": "user", "content": user_prompt})
        tool_results: list[ToolResult] = []

        # Chat mode: simple single response, no tool processing
        if self.mode == "chat":
            print("[Agent] Running in CHAT mode - no tools")
            response = await self._call_llm()
            if not response:
                return self._build_response(
                    f"Failed to get response from {self.provider} - no response received",
                    tool_results, 0
                )
            self.conversation_history.append({"role": "assistant", "content": response})
            return self._build_response(response, tool_results, 0)

        # Agent mode: allow tool calling
        print("[Agent] Running in AGENT mode - tools enabled!")
        max_iterations = 15
        response = ""
        for iteration in range(max_iterations):
            print(f"[Agent] Iteration {iteration + 1} starting...")
            response = await self._call_llm()
            print(f"[Agent] Got LLM response of length: {len(response) if response else 0}")
            if not response:
                return self._build_response(
                    f"Failed to get response from {self.provider} - no response received",
                    tool_results, iteration
                )

            tool_call = self._extract_tool_call(response)

            if tool_call:
                # Normalize tool call through middleware
                normalized_call, warnings = self.middleware.normalize_tool_call(tool_call)
                for warning in warnings:
                    print(f"[Middleware] {warning}")

                tool_name = normalized_call.get("tool", "unknown")
                tool_args = normalized_call.get("args", {})

                print(f"[Agent] Iteration {iteration + 1}: Executing tool '{tool_name}'")
                result_text = await self._execute_tool(normalized_call)

                # Determine status based on result
                status = ToolCallStatus.ERROR if result_text.startswith("Error:") else ToolCallStatus.SUCCESS

                tool_results.append(ToolResult(
                    tool=tool_name,
                    args=tool_args,
                    result=result_text,
                    status=status,
                    warnings=warnings
                ))

                self.conversation_history.append({"role": "assistant", "content": response})
                self.conversation_history.append({"role": "user", "content": f"Tool result:\n{result_text}"})
            else:
                # No tool call found - this is the final response
                self.conversation_history.append({"role": "assistant", "content": response})
                clean_response = self._clean_response(response)
                return self._build_response(clean_response, tool_results, iteration + 1)

        # Only reached if we hit max iterations without getting a final response
        clean_response = self._clean_response(response)
        return self._build_response(
            f"I've completed {max_iterations} tool operations. Here's what I found:\n\n{clean_response}",
            tool_results, max_iterations
        )

    def _build_response(self, response: str, tool_results: list[ToolResult], iterations: int) -> dict:
        """Build API-compatible response dict from structured data"""
        return {
            "response": response,
            "tool_calls": [
                {"tool": tr.tool, "args": tr.args, "result": tr.result}
                for tr in tool_results
            ],
            "iterations": iterations,
            "mode": self.mode
        }

    async def _call_llm(self) -> str:
        """Route to appropriate LLM provider"""
        if self.provider == "groq":
            return await self._call_groq()
        elif self.provider == "ollama":
            return await self._call_ollama()
        return ""

    async def _call_groq(self) -> str:
        """Call Groq API (OpenAI-compatible)"""
        system_prompt = self.chat_system_prompt if self.mode == "chat" else self._build_agent_prompt()
        messages = [{"role": "system", "content": system_prompt}]
        messages.extend(self.conversation_history)

        print(f"[Groq] Calling API with model: {self.groq_model}")
        print(f"[Groq] API key present: {bool(self.groq_api_key)}")
        print(f"[Groq] Messages count: {len(messages)}")

        try:
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    "https://api.groq.com/openai/v1/chat/completions",
                    headers={
                        "Authorization": f"Bearer {self.groq_api_key}",
                        "Content-Type": "application/json"
                    },
                    json={
                        "model": self.groq_model,
                        "messages": messages,
                        "temperature": 0.7,
                        "max_tokens": 4096
                    },
                    timeout=60.0
                )
                print(f"[Groq] Response status: {response.status_code}")
                if response.status_code == 200:
                    data = response.json()
                    content = data["choices"][0]["message"]["content"]
                    print(f"[Groq] Success, response length: {len(content)}")
                    return content
                else:
                    error_text = response.text
                    print(f"[Groq] Error {response.status_code}: {error_text}")
                    # Return error message instead of empty string for debugging
                    return f"API Error ({response.status_code}): {error_text[:200]}"
        except Exception as e:
            print(f"[Groq] Exception: {e}")
            import traceback
            traceback.print_exc()
            return f"Connection error: {str(e)}"

    async def _call_ollama(self) -> str:
        """Call Ollama API"""
        system_prompt = self.chat_system_prompt if self.mode == "chat" else self._build_agent_prompt()
        messages = [{"role": "system", "content": system_prompt}]
        messages.extend(self.conversation_history)

        print(f"[Ollama] Calling API at: {self.ollama_url}")
        print(f"[Ollama] Using model: {self.ollama_model}")
        print(f"[Ollama] Messages count: {len(messages)}")

        try:
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    f"{self.ollama_url}/api/chat",
                    json={"model": self.ollama_model, "messages": messages, "stream": False},
                    timeout=120.0
                )
                print(f"[Ollama] Response status: {response.status_code}")

                if response.status_code == 200:
                    data = response.json()
                    content = data.get("message", {}).get("content", "")
                    if content:
                        print(f"[Ollama] Success, response length: {len(content)}")
                        return content
                    else:
                        error_msg = "Ollama returned empty response. Model may not be loaded properly."
                        print(f"[Ollama] {error_msg}")
                        return f"Error: {error_msg}"
                else:
                    error_text = response.text
                    print(f"[Ollama] Error {response.status_code}: {error_text}")
                    return f"Ollama API Error ({response.status_code}): {error_text[:200]}"

        except httpx.ConnectError as e:
            error_msg = f"Cannot connect to Ollama at {self.ollama_url}. Is Ollama running? Try 'ollama serve' in terminal."
            print(f"[Ollama] Connection Error: {e}")
            return f"Error: {error_msg}"
        except httpx.TimeoutException as e:
            error_msg = "Ollama request timed out. The model might be loading or the request is too complex."
            print(f"[Ollama] Timeout Error: {e}")
            return f"Error: {error_msg}"
        except Exception as e:
            print(f"[Ollama] Unexpected error: {e}")
            import traceback
            traceback.print_exc()
            return f"Ollama Error: {str(e)}"

    def _extract_tool_call(self, response: str) -> dict | None:
        """Extract tool call JSON from response"""
        print(f"[Tool Extract] Looking for tool call in response of length {len(response)}")
        print(f"[Tool Extract] Response preview: {response[:500]}...")

        try:
            # Try different code block formats that LLMs might use
            for marker in ["```tool", "```json", "```"]:
                if marker in response:
                    print(f"[Tool Extract] Found marker: {marker}")
                    start = response.find(marker) + len(marker)
                    end = response.find("```", start)
                    if end > start:
                        json_str = response[start:end].strip()
                        print(f"[Tool Extract] Extracted JSON string: {json_str[:200]}...")
                        # Skip if it doesn't look like a tool call JSON
                        if not json_str.startswith("{"):
                            print(f"[Tool Extract] Skipping - doesn't start with {{")
                            continue

                        # Fix Python triple-quoted strings in JSON (""" or ''')
                        # Replace them with escaped newlines
                        json_str = self._fix_multiline_json(json_str)

                        parsed = json.loads(json_str)
                        # Validate it has required tool structure
                        if isinstance(parsed, dict) and "tool" in parsed:
                            print(f"[Tool Extract] Successfully parsed tool call: {parsed.get('tool')}")
                            return parsed
                        else:
                            print(f"[Tool Extract] Parsed but missing 'tool' key: {parsed.keys()}")
        except json.JSONDecodeError as e:
            print(f"[Tool Parse Error] Invalid JSON: {e}")
        except Exception as e:
            print(f"[Tool Parse Error] Unexpected error: {e}")

        print("[Tool Extract] No tool call found")
        return None

    def _fix_multiline_json(self, json_str: str) -> str:
        """Fix Python triple-quoted strings in JSON by converting them to valid JSON strings"""
        import re

        # Pattern to match: "key": """multiline content"""
        # We need to replace the triple-quoted strings with properly escaped JSON strings
        def replace_triple_quotes(match):
            key = match.group(1)
            content = match.group(2)
            # Escape the content for JSON
            escaped = content.replace('\\', '\\\\').replace('"', '\\"').replace('\n', '\\n').replace('\r', '\\r').replace('\t', '\\t')
            return f'"{key}": "{escaped}"'

        # Match both """ and ''' variants
        json_str = re.sub(r'"(\w+)":\s*"""(.*?)"""', replace_triple_quotes, json_str, flags=re.DOTALL)
        json_str = re.sub(r'"(\w+)":\s*\'\'\'(.*?)\'\'\'', replace_triple_quotes, json_str, flags=re.DOTALL)

        return json_str

    def _clean_response(self, response: str) -> str:
        """Remove tool blocks from response for cleaner display"""
        # Remove ```tool ... ``` blocks
        cleaned = re.sub(r'```tool\s*\{[^`]*\}\s*```', '', response, flags=re.DOTALL)
        # Remove ```json ... ``` blocks that look like tool calls
        cleaned = re.sub(r'```json\s*\{\s*"tool"[^`]*\}\s*```', '', cleaned, flags=re.DOTALL)
        # Remove empty ``` blocks that might be tool calls
        cleaned = re.sub(r'```\s*\{\s*"tool"[^`]*\}\s*```', '', cleaned, flags=re.DOTALL)
        # Clean up extra whitespace
        cleaned = re.sub(r'\n{3,}', '\n\n', cleaned)
        return cleaned.strip()

    async def _execute_tool(self, tool_call: dict) -> str:
        """Execute a tool and return the result.

        Note: Tool call should already be normalized by middleware before reaching here.
        """
        tool_name = tool_call.get("tool", "")
        args = tool_call.get("args", {})

        print(f"[Tool Execute] {tool_name} with args: {args}")

        try:
            # Built-in tools
            if tool_name == "list_tools":
                return self._list_available_tools()

            if tool_name == "read_tool":
                return self._read_tool_file(args.get("name", ""))

            if tool_name == "run_command":
                cwd = str(self.workspace_dir) if self.workspace_dir else None
                result = await run_command(args.get("command", ""), cwd=cwd)
                output = []
                if result.stdout:
                    output.append(f"stdout:\n{result.stdout}")
                if result.stderr:
                    output.append(f"stderr:\n{result.stderr}")
                output.append(f"exit code: {result.return_code}")
                return "\n".join(output)

            # File system tools with workspace awareness
            # Note: Middleware already normalized argument names to canonical form
            if tool_name == "list_directory":
                return self._tool_list_directory(args.get("path", "."))

            if tool_name == "read_file":
                return self._tool_read_file(args.get("path", ""))

            if tool_name == "write_file":
                return self._tool_write_file(args.get("path", ""), args.get("content", ""))

            # Dynamic tool loading from local_tools directory
            tool_path = self.tools_dir / f"{tool_name}.py"
            if tool_path.exists():
                try:
                    spec = importlib.util.spec_from_file_location(tool_name, tool_path)
                    module = importlib.util.module_from_spec(spec)
                    spec.loader.exec_module(module)
                    if hasattr(module, tool_name):
                        # Inject workspace path for tools that need it
                        if self.workspace_dir:
                            args["_workspace"] = str(self.workspace_dir)
                        return str(getattr(module, tool_name)(**args))
                except Exception as e:
                    return f"Tool execution error: {e}"

            return f"Tool '{tool_name}' not found. Use 'list_tools' to see available tools."

        except Exception as e:
            return f"Error executing tool '{tool_name}': {e}"

    def _tool_list_directory(self, path: str) -> str:
        """List directory contents with workspace awareness"""
        try:
            resolved = self._resolve_path(path)
            if not resolved.exists():
                return f"Error: Directory '{path}' does not exist"
            if not resolved.is_dir():
                return f"Error: '{path}' is not a directory"

            items = []
            for item in sorted(resolved.iterdir()):
                item_type = "dir" if item.is_dir() else "file"
                # Show relative path from workspace
                try:
                    rel_path = item.relative_to(self.workspace_dir) if self.workspace_dir else item.name
                    items.append(f"{item_type}: {rel_path}")
                except ValueError:
                    items.append(f"{item_type}: {item.name}")

            if not items:
                return f"Directory '{path}' is empty"

            return f"Contents of '{path}':\n" + "\n".join(items)
        except PermissionError:
            return f"Error: Permission denied accessing '{path}'"
        except Exception as e:
            return f"Error listing directory: {e}"

    def _tool_read_file(self, path: str) -> str:
        """Read file contents with workspace awareness"""
        try:
            if not path:
                return "Error: No file path provided"

            resolved = self._resolve_path(path)
            exists = resolved.exists()

            if not exists:
                # Track that file doesn't exist (for new file creation)
                self.file_tracker.record_read(path, "", exists=False)
                return f"Error: File '{path}' does not exist"

            if not resolved.is_file():
                return f"Error: '{path}' is not a file"

            # Check file size (limit to 100KB for safety)
            size = resolved.stat().st_size
            if size > 100 * 1024:
                return f"Error: File '{path}' is too large ({size} bytes). Maximum is 100KB."

            content = resolved.read_text(encoding='utf-8', errors='replace')

            # Track original content for diff
            self.file_tracker.record_read(path, content, exists=True)

            return f"Contents of '{path}':\n```\n{content}\n```"
        except PermissionError:
            return f"Error: Permission denied reading '{path}'"
        except Exception as e:
            return f"Error reading file: {e}"

    def _tool_write_file(self, path: str, content: str) -> str:
        """Write content to file with workspace awareness"""
        try:
            if not path:
                return "Error: No file path provided"
            if content is None:
                return "Error: No content provided"

            resolved = self._resolve_path(path)

            # Check if file exists and track original content if not already tracked
            if resolved.exists() and path not in self.file_tracker.tracked_files:
                try:
                    original = resolved.read_text(encoding='utf-8', errors='replace')
                    self.file_tracker.record_read(path, original, exists=True)
                except Exception:
                    pass

            # Create parent directories if needed
            resolved.parent.mkdir(parents=True, exist_ok=True)

            resolved.write_text(content, encoding='utf-8')

            # Track new content for diff
            self.file_tracker.record_write(path, content)

            return f"Successfully wrote {len(content)} characters to '{path}'"
        except PermissionError:
            return f"Error: Permission denied writing to '{path}'"
        except Exception as e:
            return f"Error writing file: {e}"

    def _list_available_tools(self) -> str:
        """List all available tools with descriptions"""
        tools = [
            "Built-in tools:",
            "  - list_tools: List all available tools",
            "  - read_tool(name): Read a tool's source code",
            "  - run_command(command): Execute a shell command in workspace",
            "  - list_directory(path): List directory contents",
            "  - read_file(path): Read file contents",
            "  - write_file(path, content): Write content to a file",
            "",
            "Custom tools from local_tools/:"
        ]

        if self.tools_dir.exists():
            for f in sorted(self.tools_dir.glob("*.py")):
                if f.name != "__init__.py":
                    tools.append(f"  - {f.stem}")
        else:
            tools.append("  (no custom tools found)")

        return "\n".join(tools)

    def _read_tool_file(self, name: str) -> str:
        """Read a tool's source code"""
        if not name:
            return "Error: No tool name provided"
        tool_path = self.tools_dir / f"{name}.py"
        if tool_path.exists():
            return f"Source code for '{name}':\n```python\n{tool_path.read_text()}\n```"
        return f"Tool '{name}' not found in local_tools directory"
