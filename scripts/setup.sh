#!/bin/bash
echo -e "\033[36mSetting up LocLM...\033[0m"

# Restore .NET
echo -e "\n\033[33mRestoring .NET packages...\033[0m"
dotnet restore

# Setup Python
echo -e "\n\033[33mSetting up Python environment...\033[0m"
cd src/LocLM/backend
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt

# Generate tools
echo -e "\n\033[33mGenerating MCP tool stubs...\033[0m"
python mcp_transpiler.py
cd ../../..

echo -e "\n\033[32mSetup complete!\033[0m"
echo "Run 'dotnet run --project src/LocLM' to start"
