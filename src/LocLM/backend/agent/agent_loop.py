"""Main agent loop that coordinates between LLM providers and tools"""
import json
import os
import importlib.util
from pathlib import Path
import httpx
from .platform_utils import run_command

class AgentLoop:
    def __init__(self):
        self.tools_dir = Path(__file__).parent.parent / "local_tools"
        self.conversation_history = []
        self.mode = "chat"  # "chat" or "agent"

        # Provider configuration
        self.provider = os.getenv("LLM_PROVIDER", "groq")  # groq, ollama, openai
        self.groq_api_key = os.getenv("GROQ_API_KEY", "")
        self.groq_model = os.getenv("GROQ_MODEL", "llama-3.3-70b-versatile")
        self.ollama_url = os.getenv("OLLAMA_URL", "http://localhost:11434")
        self.ollama_model = os.getenv("OLLAMA_MODEL", "llama3.2:3b")

        self.chat_system_prompt = """You are a helpful AI coding assistant. Respond directly and conversationally to user questions. Do not attempt to use any tools or execute any commands."""

        self.agent_system_prompt = """You are a helpful AI coding assistant with access to local tools.

You have a directory called ./local_tools that contains Python functions you can use.
To discover available tools, list the directory and read the tool files.

When you need to execute a tool, respond with a JSON block:
```tool
{
    "tool": "tool_name",
    "args": {"arg1": "value1"}
}
```

After tool execution, you'll receive the result and can continue.

Built-in tools: list_tools, read_tool, run_command
"""

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
                    response = await client.get(f"{self.ollama_url}/api/tags", timeout=5.0)
                    return {"available": response.status_code == 200, "provider": "Ollama"}
            except:
                return {"available": False, "provider": "Ollama"}

        return {"available": False, "provider": "Unknown"}

    async def process(self, user_prompt: str) -> dict:
        self.conversation_history.append({"role": "user", "content": user_prompt})
        tool_calls = []

        # Chat mode: simple single response, no tool processing
        if self.mode == "chat":
            response = await self._call_llm()
            if not response:
                return {"response": f"Failed to get response from {self.provider} - no response received", "tool_calls": []}
            self.conversation_history.append({"role": "assistant", "content": response})
            return {"response": response, "tool_calls": []}

        # Agent mode: allow tool calling
        max_iterations = 5
        for _ in range(max_iterations):
            response = await self._call_llm()
            if not response:
                return {"response": f"Failed to get response from {self.provider} - no response received", "tool_calls": tool_calls}

            tool_call = self._extract_tool_call(response)

            if tool_call:
                tool_result = await self._execute_tool(tool_call)
                tool_calls.append({
                    "tool": tool_call["tool"],
                    "args": tool_call.get("args", {}),
                    "result": tool_result
                })
                self.conversation_history.append({"role": "assistant", "content": response})
                self.conversation_history.append({"role": "user", "content": f"Tool result: {tool_result}"})
            else:
                self.conversation_history.append({"role": "assistant", "content": response})
                return {"response": response, "tool_calls": tool_calls}

        return {"response": "Reached maximum tool iterations", "tool_calls": tool_calls}

    async def _call_llm(self) -> str:
        """Route to appropriate LLM provider"""
        if self.provider == "groq":
            return await self._call_groq()
        elif self.provider == "ollama":
            return await self._call_ollama()
        return ""

    async def _call_groq(self) -> str:
        """Call Groq API (OpenAI-compatible)"""
        system_prompt = self.chat_system_prompt if self.mode == "chat" else self.agent_system_prompt
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
        system_prompt = self.chat_system_prompt if self.mode == "chat" else self.agent_system_prompt
        messages = [{"role": "system", "content": system_prompt}]
        messages.extend(self.conversation_history)

        try:
            async with httpx.AsyncClient() as client:
                response = await client.post(
                    f"{self.ollama_url}/api/chat",
                    json={"model": self.ollama_model, "messages": messages, "stream": False},
                    timeout=60.0
                )
                if response.status_code == 200:
                    return response.json().get("message", {}).get("content", "")
        except Exception as e:
            print(f"Ollama error: {e}")
        return ""

    def _extract_tool_call(self, response: str) -> dict | None:
        try:
            if "```tool" in response:
                start = response.find("```tool") + 7
                end = response.find("```", start)
                if end > start:
                    return json.loads(response[start:end].strip())
        except json.JSONDecodeError:
            pass
        return None

    async def _execute_tool(self, tool_call: dict) -> str:
        tool_name = tool_call.get("tool", "")
        args = tool_call.get("args", {})

        if tool_name == "list_tools":
            return self._list_available_tools()
        if tool_name == "read_tool":
            return self._read_tool_file(args.get("name", ""))
        if tool_name == "run_command":
            result = await run_command(args.get("command", ""))
            return f"stdout: {result.stdout}\nstderr: {result.stderr}\ncode: {result.return_code}"

        tool_path = self.tools_dir / f"{tool_name}.py"
        if tool_path.exists():
            try:
                spec = importlib.util.spec_from_file_location(tool_name, tool_path)
                module = importlib.util.module_from_spec(spec)
                spec.loader.exec_module(module)
                if hasattr(module, tool_name):
                    return str(getattr(module, tool_name)(**args))
            except Exception as e:
                return f"Tool error: {e}"

        return f"Tool '{tool_name}' not found"

    def _list_available_tools(self) -> str:
        tools = ["list_tools", "read_tool", "run_command"]
        if self.tools_dir.exists():
            for f in self.tools_dir.glob("*.py"):
                if f.name != "__init__.py":
                    tools.append(f.stem)
        return f"Available tools: {', '.join(tools)}"

    def _read_tool_file(self, name: str) -> str:
        tool_path = self.tools_dir / f"{name}.py"
        return tool_path.read_text() if tool_path.exists() else f"Tool '{name}' not found"
