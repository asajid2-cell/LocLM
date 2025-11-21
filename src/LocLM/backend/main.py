"""
LocLM Backend - FastAPI server for the AI coding agent
"""
import sys
from pathlib import Path
from fastapi import FastAPI, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import uvicorn

sys.path.insert(0, str(Path(__file__).parent))

from agent.agent_loop import AgentLoop
from agent.platform_utils import get_platform_info
from dotenv import load_dotenv

# Load environment variables from project root .env if present
ROOT_DIR = Path(__file__).resolve().parents[3]
load_dotenv(ROOT_DIR / ".env")

app = FastAPI(title="LocLM Backend", version="0.1.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

agent = AgentLoop()

class PromptRequest(BaseModel):
    prompt: str

class ChatResponse(BaseModel):
    response: str
    tool_calls: list = []

class ModeRequest(BaseModel):
    mode: str

class ProviderRequest(BaseModel):
    provider: str
    model: str | None = None

@app.get("/health")
async def health_check():
    provider_health = await agent.check_provider_health()
    model_info = agent.get_model_info()
    return {
        "status": "ok",
        "platform": get_platform_info(),
        "provider": provider_health,
        "model": model_info
    }

@app.get("/model")
async def get_model():
    """Get current model configuration"""
    model_info = agent.get_model_info()
    provider_health = await agent.check_provider_health()
    return {
        "model": model_info,
        "available": provider_health["available"]
    }

@app.get("/mode")
async def get_mode():
    """Get current agent mode"""
    return {"mode": agent.get_mode()}

@app.post("/mode")
async def set_mode(request: ModeRequest):
    """Set agent mode: 'chat' or 'agent'"""
    if agent.set_mode(request.mode):
        return {"mode": agent.get_mode()}
    raise HTTPException(status_code=400, detail="Invalid mode. Use 'chat' or 'agent'")

@app.get("/provider")
async def get_provider():
    """Get current provider configuration"""
    return {
        "provider": agent.provider,
        "model": agent.get_model_info()["model"],
        "available_providers": ["groq", "ollama"]
    }

@app.post("/provider")
async def set_provider(request: ProviderRequest):
    """Set LLM provider: 'groq' or 'ollama'"""
    if request.provider not in ["groq", "ollama"]:
        raise HTTPException(status_code=400, detail="Invalid provider. Use 'groq' or 'ollama'")

    agent.provider = request.provider

    # Update model if specified
    if request.model:
        if request.provider == "groq":
            agent.groq_model = request.model
        elif request.provider == "ollama":
            agent.ollama_model = request.model

    return {
        "provider": agent.provider,
        "model": agent.get_model_info()["model"]
    }

@app.post("/chat", response_model=ChatResponse)
async def chat(request: PromptRequest):
    try:
        result = await agent.process(request.prompt)
        return ChatResponse(
            response=result.get("response", ""),
            tool_calls=result.get("tool_calls", [])
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/tools")
async def list_tools():
    tools_dir = Path(__file__).parent / "local_tools"
    tools = []
    if tools_dir.exists():
        for f in tools_dir.glob("*.py"):
            if f.name != "__init__.py":
                tools.append({"name": f.stem, "path": str(f)})
    return {"tools": tools}

@app.post("/transpile")
async def transpile_mcp():
    from mcp_transpiler import transpile_all_servers
    try:
        result = await transpile_all_servers()
        return {"status": "success", "tools_generated": result}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    print("Starting LocLM Backend on http://localhost:8000")
    uvicorn.run(app, host="127.0.0.1", port=8000, log_level="info")
