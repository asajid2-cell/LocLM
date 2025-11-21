"""
MCP Transpiler - Converts MCP tools to static Python files
Implements the "98% approach" - tools as discoverable files instead of prompt bloat
"""
import asyncio
import json
from pathlib import Path

OUTPUT_DIR = Path(__file__).parent / "local_tools"

def ensure_output_dir():
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    init_file = OUTPUT_DIR / "__init__.py"
    if not init_file.exists():
        init_file.write_text("# Auto-generated local tools\n")

def generate_tool_stub(tool_name: str, description: str, input_schema: dict, server_name: str) -> str:
    func_name = tool_name.replace("-", "_").replace(".", "_")

    params_doc = ""
    if "properties" in input_schema:
        params_doc = "\n    Parameters:\n"
        for param, details in input_schema["properties"].items():
            params_doc += f"        {param} ({details.get('type', 'any')}): {details.get('description', '')}\n"

    return f'''"""
{description}

Source: MCP Server '{server_name}'
{params_doc}
"""
import json
from pathlib import Path

SCHEMA = {json.dumps(input_schema, indent=4)}

def {func_name}(**kwargs):
    """
    {description}
    Auto-generated wrapper for MCP tool: {tool_name}
    """
    required = {json.dumps(input_schema.get("required", []))}
    for param in required:
        if param not in kwargs:
            return f"Error: Missing required parameter '{{param}}'"

    print(f"[MCP Tool] {{'{func_name}'}} called with: {{kwargs}}")
    return f"Tool {func_name} executed with {{kwargs}}"
'''

async def transpile_filesystem_tools():
    ensure_output_dir()
    tools_generated = []

    filesystem_tools = [
        {"name": "read_file", "description": "Read file contents", "schema": {
            "type": "object", "properties": {"path": {"type": "string", "description": "File path"}}, "required": ["path"]}},
        {"name": "write_file", "description": "Write content to file", "schema": {
            "type": "object", "properties": {"path": {"type": "string"}, "content": {"type": "string"}}, "required": ["path", "content"]}},
        {"name": "list_directory", "description": "List directory contents", "schema": {
            "type": "object", "properties": {"path": {"type": "string"}}, "required": ["path"]}},
    ]

    implementations = {
        "read_file": '''
    try:
        return Path(kwargs.get("path")).read_text()
    except Exception as e:
        return f"Error: {e}"''',
        "write_file": '''
    try:
        Path(kwargs.get("path")).write_text(kwargs.get("content", ""))
        return f"Written to {kwargs.get('path')}"
    except Exception as e:
        return f"Error: {e}"''',
        "list_directory": '''
    try:
        items = list(Path(kwargs.get("path", ".")).iterdir())
        return "\\n".join(f"{'dir' if i.is_dir() else 'file'}: {i.name}" for i in items)
    except Exception as e:
        return f"Error: {e}"''',
    }

    for tool in filesystem_tools:
        code = generate_tool_stub(tool["name"], tool["description"], tool["schema"], "filesystem")
        if tool["name"] in implementations:
            code = code.replace(f'return f"Tool {tool["name"]} executed with {{kwargs}}"', implementations[tool["name"]].strip())

        (OUTPUT_DIR / f"{tool['name']}.py").write_text(code)
        tools_generated.append(tool["name"])
        print(f"Generated: {tool['name']}.py")

    return tools_generated

async def transpile_all_servers():
    return await transpile_filesystem_tools()

if __name__ == "__main__":
    tools = asyncio.run(transpile_all_servers())
    print(f"Transpiled {len(tools)} tools: {tools}")
