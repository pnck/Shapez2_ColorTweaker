# Agent Instructions for ColorTweaker

When starting any work on this project, **immediately load and read** `LLM-skills/colortweaker-dev/SKILL.md` before taking any action. It contains:

- The full game color rendering architecture (analyzed via dnspy)
- ColorTweaker mod implementation details and hook strategy
- dnspy MCP tool reference (parameter names, available tools)
- shapez2 modding framework key knowledge

The dnspy MCP is available and connected. Use `mcp_dnspy_*` tools to inspect game assemblies at any time.

## Language Policy

- Communicate with the user in whatever language they use.
- **Any modifications to skill files (SKILL.md or files under `LLM-skills/`) must be written in English**, regardless of the conversation language.
