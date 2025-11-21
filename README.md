# LocLM

A desktop AI coding agent with a modern dark UI, built with C# Avalonia and Python FastAPI.

## Features

- **Modern Dark UI**: Clean, minimal interface with three-layer depth system
- **Chat History**: SQLite-based persistent chat sessions
- **Dual Modes**: Chat mode (simple responses) and Agent mode (with tool calling)
- **File Explorer**: Browse and edit files directly in the app
- **Code Editor**: Built-in editor with syntax awareness
- **Vim Controls**: Optional vim-style keyboard navigation
- **Model Support**: Works with Groq, Ollama, and OpenAI
- **Tool Calling**: Execute local Python tools and commands

## Setup

### Prerequisites

- .NET 8.0 SDK
- Python 3.11+
- (Optional) Ollama for local models

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/asajid2-cell/LocLM.git
   cd LocLM
   ```

2. **Set up environment variables**
   ```bash
   cp .env.example .env
   ```

   Edit `.env` and add your API key:
   ```
   GROQ_API_KEY=your_api_key_here
   ```

3. **Install Python dependencies**
   ```bash
   cd src/LocLM/backend
   pip install -r requirements.txt
   ```

4. **Build and run**
   ```bash
   dotnet build
   dotnet run --project src/LocLM/LocLM.csproj
   ```

## Configuration

### Using Groq (Recommended)

1. Get a free API key from [console.groq.com](https://console.groq.com)
2. Set the environment variable:
   ```bash
   export GROQ_API_KEY=your_key_here  # Linux/Mac
   set GROQ_API_KEY=your_key_here     # Windows
   ```

### Using Ollama (Local)

1. Install [Ollama](https://ollama.ai)
2. Pull a model:
   ```bash
   ollama pull llama3.2:3b
   ```
3. Set the provider:
   ```bash
   export LLM_PROVIDER=ollama
   ```

## Usage

- **New Chat**: Click the 💬 button in the bottom-left footer
- **Chat History**: View and load past sessions from the left sidebar
- **Switch Modes**: Use the model selector dropdown to toggle between Chat and Agent modes
- **File Explorer**: Browse files in the current directory
- **Editor**: Click any file to open it in the built-in editor
- **Settings**: Click ⚙ to configure keyboard shortcuts and preferences

## Architecture

- **Frontend**: C# with Avalonia UI 11.1.0 (MVVM pattern)
- **Backend**: Python FastAPI with async LLM calls
- **Database**: SQLite for chat history
- **Styling**: Custom dark theme with three-layer depth (#0A0A0A sidebars, #000000 center)

## Chat History

All conversations are automatically saved to a local SQLite database at:
- Windows: `%LocalAppData%\LocLM\chat_history.db`
- Linux/Mac: `~/.local/share/LocLM/chat_history.db`

## License

MIT
