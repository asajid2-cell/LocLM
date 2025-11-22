"""
Agent Planner - Generates execution plans before running tools.

The planner asks the LLM to create a structured plan of steps
before executing any tools. This provides:
1. Better visibility for users on what will happen
2. Opportunity to approve/reject dangerous operations
3. More structured, predictable agent behavior
"""
import json
from typing import Optional
from .models import AgentPlan, PlanStep


PLANNING_PROMPT = """Based on the user's request, create a plan of steps to accomplish the task.

Format your response as a JSON object:
```plan
{
    "goal": "Brief description of what we're trying to achieve",
    "steps": [
        {
            "description": "What this step does",
            "tool": "tool_name or null if no tool needed",
            "reasoning": "Why this step is needed"
        }
    ],
    "requires_confirmation": true/false (set true for destructive operations like delete, overwrite)
}
```

Guidelines:
- Break complex tasks into simple, atomic steps
- Each step should use at most one tool
- Order steps logically (e.g., read before edit)
- For file modifications, always read first
- Set requires_confirmation=true for: file deletion, overwriting existing files, running potentially dangerous commands

User request: {user_request}

Current workspace context:
{workspace_context}
"""


class AgentPlanner:
    """Generates execution plans for agent tasks"""

    def __init__(self, workspace_context: str = ""):
        self.workspace_context = workspace_context

    def set_context(self, context: str):
        """Update workspace context for planning"""
        self.workspace_context = context

    def build_planning_prompt(self, user_request: str) -> str:
        """Build the prompt for plan generation"""
        return PLANNING_PROMPT.format(
            user_request=user_request,
            workspace_context=self.workspace_context or "No workspace context available"
        )

    def parse_plan(self, llm_response: str) -> Optional[AgentPlan]:
        """Parse a plan from LLM response"""
        try:
            # Try to extract JSON from ```plan block
            if "```plan" in llm_response:
                start = llm_response.find("```plan") + len("```plan")
                end = llm_response.find("```", start)
                if end > start:
                    json_str = llm_response[start:end].strip()
                    return self._parse_json_plan(json_str)

            # Try ```json block
            if "```json" in llm_response:
                start = llm_response.find("```json") + len("```json")
                end = llm_response.find("```", start)
                if end > start:
                    json_str = llm_response[start:end].strip()
                    return self._parse_json_plan(json_str)

            # Try to find raw JSON
            if "{" in llm_response and "goal" in llm_response:
                start = llm_response.find("{")
                # Find matching closing brace
                depth = 0
                for i, char in enumerate(llm_response[start:], start):
                    if char == "{":
                        depth += 1
                    elif char == "}":
                        depth -= 1
                        if depth == 0:
                            json_str = llm_response[start:i+1]
                            return self._parse_json_plan(json_str)

        except Exception as e:
            print(f"[Planner] Error parsing plan: {e}")

        return None

    def _parse_json_plan(self, json_str: str) -> AgentPlan:
        """Parse JSON string into AgentPlan"""
        data = json.loads(json_str)

        steps = []
        for step_data in data.get("steps", []):
            steps.append(PlanStep(
                description=step_data.get("description", ""),
                tool=step_data.get("tool"),
                reasoning=step_data.get("reasoning"),
                completed=False
            ))

        return AgentPlan(
            goal=data.get("goal", ""),
            steps=steps,
            requires_confirmation=data.get("requires_confirmation", False)
        )

    def is_plan_needed(self, user_request: str) -> bool:
        """Determine if a request needs planning vs direct response"""
        # Keywords that suggest complex multi-step operations
        planning_keywords = [
            "create", "build", "implement", "add", "write",
            "fix", "refactor", "update", "modify", "change",
            "delete", "remove", "move", "rename",
            "install", "setup", "configure",
            "analyze", "review", "find all", "search and replace"
        ]

        request_lower = user_request.lower()

        # Check for planning keywords
        for keyword in planning_keywords:
            if keyword in request_lower:
                return True

        # Check for multiple actions
        action_words = ["and", "then", "also", "after that"]
        for word in action_words:
            if word in request_lower:
                return True

        return False

    def format_plan_for_display(self, plan: AgentPlan) -> str:
        """Format a plan for display to the user"""
        lines = [f"**Plan: {plan.goal}**", ""]

        for i, step in enumerate(plan.steps, 1):
            status = "✓" if step.completed else "○"
            tool_info = f" (using `{step.tool}`)" if step.tool else ""
            lines.append(f"{status} {i}. {step.description}{tool_info}")

        if plan.requires_confirmation:
            lines.append("")
            lines.append("⚠️ This plan includes potentially destructive operations. Please confirm to proceed.")

        return "\n".join(lines)
