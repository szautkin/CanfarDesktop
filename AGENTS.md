# Connecting an AI agent to Verbinal

This file is for AI assistants — Claude, Codex, Copilot, Cursor, Gemini or any other — that the person
using Verbinal has asked to connect to it. It is not about working on Verbinal's source code.

Verbinal is a Windows desktop app for the Canadian Astronomy Data Centre and the CANFAR science
platform: archive search, FITS image and data-cube viewers, notebooks, VOSpace storage and platform
sessions. Everything a person can do in it, an agent can do too: more than 170 tools, over the
Model Context Protocol (MCP).

## How it connects

- **A local stdio MCP server.** Your client launches a small bridge program with one argument, `mcp`.
  The bridge relays to the running Verbinal app over a named pipe that only this Windows user can open.
- **No network port, no URL, no API key, no environment variables.**
- **Server name:** use `verbinal-canfar`. It is the name Verbinal's own setup uses.
- **Verbinal has to be running**, with its MCP server turned on.

## 1. Is Verbinal installed?

```powershell
Get-AppxPackage -Name CodeBG.Verbinal | Select-Object Version, PackageFamilyName
```

If this prints nothing, Verbinal is not installed. It is free in the Microsoft Store:
https://apps.microsoft.com/detail/9p8jqvk4pjch

## 2. The person turns on the MCP server

The MCP server is off until the person turns it on, and turning it on is their decision. Ask them to:

1. Open Verbinal.
2. Go to **Settings** (the gear in the title bar) ▸ **AI agent**.
3. Turn on **Enable MCP server**.

If their assistant is **Claude Desktop** or **Claude Code**, the **AI Assistant** tile on Verbinal's
home screen runs a wizard that does steps 2 to 4 for them.

## 3. Find the bridge

The bridge is at the same place on every machine, under the person's own profile:

```text
%LOCALAPPDATA%\Packages\CodeBG.Verbinal_zjqkjyb5296v2\LocalCache\Verbinal\mcp-bridge\CanfarDesktop.McpBridge.exe
```

Verbinal puts it there whenever its MCP server is on, and keeps it current across app updates.
`CodeBG.Verbinal_zjqkjyb5296v2` is the app's package family name. It is the same for every version
and architecture, but look it up rather than trusting it. Most clients do not expand
`%LOCALAPPDATA%`, so register the full path. This command prints the path, and says whether it
exists yet:

```powershell
$family = (Get-AppxPackage -Name CodeBG.Verbinal).PackageFamilyName
$bridge = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalCache\Verbinal\mcp-bridge\CanfarDesktop.McpBridge.exe"
$bridge; Test-Path $bridge
```

If it does not exist, the MCP server has not been turned on yet (step 2). Once it is, the file
appears within a few seconds. **Settings ▸ AI agent** shows the exact command for this machine too.
It is also the place to check if this is a development build run from Visual Studio, whose bridge is
elsewhere.

## 4. Register it with your client

Every client needs the same three things:

- **Name:** `verbinal-canfar`
- **Command:** the full path from step 3
- **Arguments:** `["mcp"]`

Add it to the client's configuration **without removing the servers already there**. In the examples
below, replace `C:\...\CanfarDesktop.McpBridge.exe` with the full path. In JSON, every backslash is
doubled. Most clients need a restart or a reload before a new server appears.

**Claude Code**

```powershell
claude mcp add --transport stdio --scope user verbinal-canfar -- "C:\...\CanfarDesktop.McpBridge.exe" mcp
```

**Claude Desktop:** `claude_desktop_config.json`, or let Verbinal's wizard write it

```json
{ "mcpServers": { "verbinal-canfar": { "command": "C:\\...\\CanfarDesktop.McpBridge.exe", "args": ["mcp"] } } }
```

**OpenAI Codex:** `%USERPROFILE%\.codex\config.toml`. Single quotes keep the backslashes as they are.

```toml
[mcp_servers.verbinal-canfar]
command = 'C:\...\CanfarDesktop.McpBridge.exe'
args = ["mcp"]
```

**These clients take the same `mcpServers` entry as Claude Desktop:**

- **Cursor:** `%USERPROFILE%\.cursor\mcp.json`
- **Gemini CLI:** `%USERPROFILE%\.gemini\settings.json`
- **Windsurf:** `%USERPROFILE%\.codeium\windsurf\mcp_config.json`

**VS Code (GitHub Copilot agent mode):** the user or workspace `mcp.json`

```json
{ "servers": { "verbinal-canfar": { "type": "stdio", "command": "C:\\...\\CanfarDesktop.McpBridge.exe", "args": ["mcp"] } } }
```

**Any other client** that can launch a stdio MCP server works the same way.

## 5. Check it works

Call `describe_app`. It answers with the app's version and what it can do. If the call fails:

- **Is Verbinal running?** The bridge only relays to the app. It cannot start it.
- **Is Enable MCP server on?**
- **Is "Require approval for new clients" on?** Then the person approves your client under
  **Settings ▸ AI agent ▸ Connected clients**.
- **Still failing?** **Settings ▸ AI agent ▸ Diagnostics ▸ Run diagnostics** checks the whole chain
  and says which link is broken.

## Once you are connected

- **Get your bearings.**
  - `describe_app` gives an overview.
  - `search_tools` finds a tool for a task.
  - `man` gives any tool's full manual.
- **Follow the workflows.** `list_workflows` and `use_workflow` give step-by-step protocols, including:
  - a guided tour of the whole app
  - a detailed tour of each screen
  - built-in science workflows
- **Work where the person can see it.** `navigate_to` shows them the screen you are working on, and
  `point_at_ui` points at the control you mean. They see what you are doing as you do it.
- **The person reviews your changes.** Consequential changes are proposals.
  - With auto-apply on (the default), reversible ones apply at once.
  - Destructive ones, such as deleting data or stopping a session, always wait for the person to
    approve them in the app.
  - `list_pending_proposals` shows what is waiting.
- **Signing in is theirs to do.** Portal, Remote Compute and Storage are the person's CADC/CANFAR
  account, and stay locked until they sign in.
  - Their tools answer "sign in required" until then.
  - `navigate_to` on one of those screens asks them to sign in.
  - Never ask for their password. You cannot sign in for them.
- **Running code on CANFAR:** `run_code` runs Python or Bash in a session on their CANFAR account.
  It works only after they have set up Remote Compute, which is their decision and their allocation.
  The Remote Compute screen explains how.
