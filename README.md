# Mud MCP

A Model Context Protocol (MCP) server that gives AI assistants access to MudBlazor component documentation, code examples, and API reference for the exact MudBlazor version your project uses.

[![CI](https://github.com/M0n7y5/MudMCP/actions/workflows/ci.yml/badge.svg)](https://github.com/M0n7y5/MudMCP/actions/workflows/ci.yml)
[![.NET 10](https://img.shields.io/badge/.NET-10.0-purple)](https://dotnet.microsoft.com/)
[![MCP Protocol](https://img.shields.io/badge/MCP-Protocol-blue)](https://modelcontextprotocol.io/)
[![License: GPL-2.0](https://img.shields.io/badge/License-GPL%202.0-green.svg)](LICENSE)

> **Disclaimer:** This project is not affiliated with, endorsed by, or officially supported by the MudBlazor team. It is an independent implementation that extracts and serves documentation from the official MudBlazor repository.

## How it works

The server clones the MudBlazor repository at the tag matching your `--version`, parses the source with Roslyn, and exposes the resulting index through MCP tools. Clients such as GitHub Copilot, Claude Code, and Cursor query it instead of guessing from training data.

- Documentation always matches the requested MudBlazor version, including enums and inherited members parsed from source
- Responses are Markdown, sized for LLM consumption
- Up to 3 versions are cached side by side with LRU eviction; after the first run a version starts instantly from its serialized index
- HTTP and stdio transports; readiness and liveness health endpoints report index state and parse failures

## Quick Start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Git](https://git-scm.com/).

1. Find the MudBlazor version in your project's `.csproj`:

   ```xml
   <PackageReference Include="MudBlazor" Version="9.0.0" />
   ```

2. Run the server with that version (the `--version` argument is required):

   ```bash
   dotnet run --project src/MudBlazor.Mcp/MudBlazor.Mcp.csproj -- --version 9.0.0
   ```

   The first run clones the MudBlazor repository (about 500 MB) and builds the index; later runs load the cached index and start instantly.

3. Verify:

   ```bash
   curl http://localhost:8000/health
   ```

4. Connect your client (VS Code / Cursor `mcp.json`, HTTP mode):

   ```json
   {
     "servers": {
       "mudblazor": {
         "type": "http",
         "url": "http://localhost:8000/mcp"
       }
     }
   }
   ```

## Local MCP (stdio)

Most MCP clients (Cursor, Claude Code, Claude Desktop) spawn the server as a child process and talk over stdin/stdout. No web server is started in this mode.

### Option A: dotnet run

Add to your project's `.mcp.json` (or `.cursor/mcp.json`, `claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "mudblazor": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "<path-to-MudMCP>/src/MudBlazor.Mcp/MudBlazor.Mcp.csproj",
        "--",
        "--stdio",
        "--version",
        "9.0.0"
      ]
    }
  }
}
```

Replace `<path-to-MudMCP>` with the absolute path to your clone and `9.0.0` with your project's MudBlazor version.

### Option B: self-contained executable

Publish a single-file executable that starts without the .NET SDK:

```powershell
dotnet publish src/MudBlazor.Mcp/MudBlazor.Mcp.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o publish/win-x64
```

```json
{
  "mcpServers": {
    "mudblazor": {
      "command": "<path-to-MudMCP>/publish/win-x64/MudBlazor.Mcp.exe",
      "args": ["--stdio", "--version", "9.0.0"]
    }
  }
}
```

### Option C: Docker (HTTP mode, persistent cache)

Compose runs the published image (built by CI for `linux/amd64` and `linux/arm64`) with a named volume and health checks preconfigured:

```bash
docker compose up -d
docker compose logs -f
curl http://localhost:8000/health
```

Compose always pulls and runs exactly `ghcr.io/m0n7y5/mudmcp:latest`, so every `docker compose up` uses the current published image.

Without Compose, the same setup as a plain `docker run`:

```bash
docker run -d --name mudblazor-mcp \
  -p 8000:8080 \
  -e MUDBLAZOR_VERSION=9.0.0 \
  -v mudblazor-data:/app/data \
  ghcr.io/m0n7y5/mudmcp:latest
```

The MCP endpoint is `http://localhost:8000/mcp`. Cached data lives in the `mudblazor-data` volume mounted at `/app/data`; each version gets its own subdirectory with the git clone and serialized index. Tagged commits are immutable, so existing clones are reused without fetching.

```bash
# Stop, keep the cache
docker compose down

# Stop and delete all cached data (full re-clone and re-index next start)
docker compose down -v
```

### Version caching

Up to 3 MudBlazor versions are cached at once, each with its own clone and index:

```
data/
  versions.json          # tracks cached versions + last-used timestamps
  v8.15.0/
    mudblazor-repo/      # git clone at tag v8.15.0
    index.json           # serialized component index
  v9.0.0/
    mudblazor-repo/
    index.json
```

Requesting a 4th version evicts the least recently used one. Projects on different MudBlazor versions can each point their `.mcp.json` at the right `--version` and share the cache.

### Transport comparison

| Mode | Command | Kestrel | Use case |
|------|---------|---------|----------|
| `--stdio` | `dotnet run -- --stdio --version X.Y.Z` or `.exe --stdio --version X.Y.Z` | No | Cursor, Claude Code, Claude Desktop, local clients |
| HTTP (default) | `dotnet run -- --version X.Y.Z` | Yes (`:8000`) | VS Code HTTP, MCP Inspector, remote |
| Docker | `docker compose up` | Yes (`:8000` to `:8080`) | Containerised / persistent cache |

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `list_components` | Lists all MudBlazor components with optional category filter |
| `list_categories` | Lists all component categories with descriptions |
| `get_component_detail` | Gets details about a specific component, optionally with inherited members |
| `get_component_parameters` | Gets all parameters for a component |
| `get_component_examples` | Gets code examples for a component |
| `get_example_by_name` | Gets a specific example by name |
| `list_component_examples` | Lists all example names for a component |
| `search_components` | Searches components by name, description, or parameters |
| `get_components_by_category` | Gets all components in a specific category |
| `get_related_components` | Gets components related through inheritance, category, or docs links |
| `get_api_reference` | Gets full API reference for a component, base class, or enum |
| `get_enum_values` | Gets all values for a MudBlazor enum, parsed from source |

Example prompts:

- "List all MudBlazor button components"
- "Show me how to use MudTextField with validation"
- "What parameters does MudDataGrid support?"
- "What are the available Color enum values?"

## GitHub Copilot agent file

`.github/agents/mudblazor-expert.agent.md` teaches Copilot when to call which MCP tool, to query before answering, and how to chain tools (search, then detail, then examples). Derived from [github/awesome-copilot](https://github.com/github/awesome-copilot).

## Documentation

| Document | Description |
|----------|-------------|
| [Overview](./docs/01-overview.md) | Architecture and design overview |
| [Getting Started](./docs/02-getting-started.md) | Installation and first run |
| [Architecture](./docs/03-architecture.md) | Technical architecture and component design |
| [Best Practices](./docs/04-best-practices.md) | Implemented patterns and practices |
| [Tools Reference](./docs/05-tools-reference.md) | Reference for all 12 MCP tools |
| [Configuration](./docs/06-configuration.md) | Configuration options and environment setup |
| [Testing](./docs/07-testing.md) | Unit testing strategy and examples |
| [MCP Inspector](./docs/08-mcp-inspector.md) | Testing with MCP Inspector |
| [IDE Integration](./docs/09-ide-integration.md) | VS Code, Visual Studio, and Claude Desktop setup |
| [Troubleshooting](./docs/10-troubleshooting.md) | Common issues and solutions |

## Project Structure

```
MudMCP/
├── .github/
│   ├── agents/                     # Copilot agent files
│   └── workflows/ci.yml            # Build, test, publish image to GHCR
├── src/
│   └── MudBlazor.Mcp/              # MCP server
│       ├── Configuration/          # Strongly-typed options
│       ├── Models/                 # Domain models (immutable records)
│       ├── Services/               # Indexing, git, version cache
│       │   └── Parsing/            # Roslyn-based parsers
│       └── Tools/                  # MCP tool implementations
├── tests/
│   └── MudBlazor.Mcp.Tests/        # Unit tests
└── docs/
```

## Contributing

Fork, branch, and open a pull request. Run `dotnet build` and `dotnet test` before submitting; warnings are treated as errors.

## License

Licensed under the [GNU General Public License v2.0](LICENSE), matching MudBlazor's license. Original copyright notices are retained.

## Acknowledgments

- [MudBlazor](https://mudblazor.com/), the Blazor component library this server documents
- [Model Context Protocol](https://modelcontextprotocol.io/)
- [Roslyn](https://github.com/dotnet/roslyn)
- [github/awesome-copilot](https://github.com/github/awesome-copilot) for the agent file base
