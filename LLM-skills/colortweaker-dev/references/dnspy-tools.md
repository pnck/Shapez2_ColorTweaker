# dnspy MCP Tool Reference

## MCP Server Setup

- **Server name**: `dnSpy` (HTTP streamable, not stdio)
- **Tool prefix**: `mcp_dnspy_*`
- **Connection config**: managed by the MCP configuration of the host environment (VS Code, Cursor, Claude Desktop, etc.); `.vscode/mcp.json` in the project root is one example config file
- **Verify availability**: call `mcp_dnspy_list_assemblies()`
  - Returns assembly list → available, proceed with reverse engineering
  - Error / empty result → unavailable; stop and ask the user to configure the dnSpy MCP server in their current host environment before retrying

## Tool List

| Tool | Required params | Description |
|------|----------------|-------------|
| `mcp_dnspy_list_assemblies` | — | List all loaded assemblies |
| `mcp_dnspy_search_types` | `query` | Search types by name (across all assemblies) |
| `mcp_dnspy_get_type_info` | `assembly_name`, `type_full_name` | Full type details (fields, methods, properties, base type, interfaces) |
| `mcp_dnspy_get_type_fields` | `assembly_name`, `type_full_name` | Fields only |
| `mcp_dnspy_get_type_property` | `assembly_name`, `type_full_name` | Properties only |
| `mcp_dnspy_list_methods` | `assembly_name`, `type_full_name` | All methods with tokens |
| `mcp_dnspy_decompile_method` | `assembly_name`, `type_full_name`, `method_name` | Decompile method to C# |
| `mcp_dnspy_get_method_il` | `assembly_name`, `type_full_name`, `method_name` | Raw IL bytecode |
| `mcp_dnspy_find_path_to_type` | `query` | Locate the assembly path containing a type |
| `mcp_dnspy_get_assembly_info` | `assembly_name` | Assembly metadata |

**Overloaded methods**: pass `method_token` (integer). Obtain the token first via `get_type_info` or `list_methods`, then supply it to `decompile_method`.

**Nested types**: use `+` as separator, e.g. `OuterClass+InnerClass` (not `/`).

## Assembly Index

| Assembly | Key types |
|----------|-----------|
| `Game.Content.Features` | `IShapeColor`, `IShapeColorScheme`, `IFluid`, `ColorFluid` |
| `SPZGameAssembly` | `MetaShapeColor`, `MetaShapeColorRenderData`, `MetaShapeColorVisualizationScheme`, `Game.Core.Shape.Colors.ShapeColorVisualizationScheme`, `Game.Core.Shape.Colors.ColorRenderData`, `ShapeColorScheme`, `MaterialPropertyHelpers` |
| `Game.Core` | Base game types, Modding (`IMod`) |
| `Game.Core.Rendering` | `InstancingIdManager`, `LOD6Material`, rendering pipeline |
| `Game.Core.Effects` | Effects |
| `Game.Content` | Content assets |
| `Game.Orchestration` | Top-level game orchestration (initialization, scene management) |
| `Core` | Utility library (`Core.Logging.ILogger`, etc.) |
| `ShapezShifter` | `DetourHelper`, SharpDetour hook API |
| `MonoMod.RuntimeDetour` | Low-level hook engine (`Hook` class) |
| `UnityEngine.CoreModule` | Unity basics (`Color`, `MaterialPropertyBlock`, `ScriptableObject`, ...) |

## Common Usage Examples

```
# Find a type
mcp_dnspy_search_types query="ShapeColorVisualizationScheme"

# Get type structure (including method tokens)
mcp_dnspy_get_type_info assembly_name="SPZGameAssembly"
                        type_full_name="Game.Core.Shape.Colors.ShapeColorVisualizationScheme"

# Decompile an overloaded method (use token to disambiguate)
mcp_dnspy_decompile_method assembly_name="SPZGameAssembly"
                           type_full_name="Game.Core.Shape.Colors.ShapeColorVisualizationScheme"
                           method_name="GetData"
                           method_token=100677694
```
                           method_token=100677694
```
