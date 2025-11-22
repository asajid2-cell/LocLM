"""
Pydantic models for structured agent I/O.

These models provide type safety and validation for:
- Tool calls extracted from LLM responses
- Tool execution results
- Agent process responses
"""
from pydantic import BaseModel, Field
from typing import Optional, Any
from enum import Enum


class ToolCallStatus(str, Enum):
    """Status of a tool execution"""
    SUCCESS = "success"
    ERROR = "error"
    SKIPPED = "skipped"


class ToolCall(BaseModel):
    """A tool call extracted from LLM response"""
    tool: str = Field(..., description="Name of the tool to execute")
    args: dict[str, Any] = Field(default_factory=dict, description="Arguments for the tool")

    class Config:
        extra = "ignore"


class ToolResult(BaseModel):
    """Result of a tool execution"""
    tool: str = Field(..., description="Name of the tool that was executed")
    args: dict[str, Any] = Field(default_factory=dict, description="Arguments that were passed")
    result: str = Field(..., description="Output from the tool")
    status: ToolCallStatus = Field(default=ToolCallStatus.SUCCESS)
    warnings: list[str] = Field(default_factory=list, description="Middleware warnings")

    @property
    def is_error(self) -> bool:
        return self.status == ToolCallStatus.ERROR or self.result.startswith("Error:")


class AgentResponse(BaseModel):
    """Complete response from the agent loop"""
    response: str = Field(..., description="Final text response to display")
    tool_calls: list[ToolResult] = Field(default_factory=list)
    iterations: int = Field(default=0, description="Number of agent loop iterations")
    mode: str = Field(default="chat", description="Agent mode used")

    @property
    def has_tool_calls(self) -> bool:
        return len(self.tool_calls) > 0

    @property
    def has_errors(self) -> bool:
        return any(tc.is_error for tc in self.tool_calls)


class FileChange(BaseModel):
    """Represents a file modification for diff view"""
    path: str = Field(..., description="File path relative to workspace")
    operation: str = Field(..., description="create, modify, delete")
    old_content: Optional[str] = Field(None, description="Previous content (for modify)")
    new_content: Optional[str] = Field(None, description="New content (for create/modify)")

    @property
    def is_new_file(self) -> bool:
        return self.operation == "create"

    @property
    def is_deletion(self) -> bool:
        return self.operation == "delete"


class PlanStep(BaseModel):
    """A step in the agent's execution plan (Phase 2)"""
    description: str = Field(..., description="What this step will do")
    tool: Optional[str] = Field(None, description="Tool to use, if any")
    reasoning: Optional[str] = Field(None, description="Why this step is needed")
    completed: bool = Field(default=False)


class AgentPlan(BaseModel):
    """Execution plan before running tools (Phase 2)"""
    goal: str = Field(..., description="What the user wants to achieve")
    steps: list[PlanStep] = Field(default_factory=list)
    requires_confirmation: bool = Field(default=False, description="If plan needs user approval")

    @property
    def is_complete(self) -> bool:
        return all(step.completed for step in self.steps)


# Request/Response models for API endpoints
class ChatRequest(BaseModel):
    """Request to the /chat endpoint"""
    prompt: str = Field(..., min_length=1, description="User message")
    mode: Optional[str] = Field(None, description="Override agent mode")


class ChatResponse(BaseModel):
    """Response from the /chat endpoint"""
    response: str
    tool_calls: list[dict[str, Any]] = Field(default_factory=list)
    model: dict[str, str] = Field(default_factory=dict)


class WorkspaceRequest(BaseModel):
    """Request to set workspace"""
    path: str = Field(..., description="Workspace directory path")


class HealthResponse(BaseModel):
    """Health check response"""
    status: str
    available: bool
    provider: str
