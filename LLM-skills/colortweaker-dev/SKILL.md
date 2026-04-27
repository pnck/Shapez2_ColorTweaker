---
name: colortweaker-dev
description: "LOAD IMMEDIATELY when working on ColorTweaker mod. Provides full dev context: current implementation status, game color rendering architecture (ShapeColorVisualizationScheme, ColorRenderData, MaterialPropertyBlock), hook strategy via ShapezShifter SharpDetours, dnspy MCP tool usage. Keywords: shapez2 mod, ColorTweaker, color rendering, hook, dnspy, FluidColorGenerator, MonoMod, MaterialReference, LOD6Material"
---

# ColorTweaker Dev Context

## On Load: Required Steps

1. Read the source files to understand current state: four files under `ColorTweaker/src/` — `ModEntry.cs`, `Hook.cs`, `ColorOverride.cs`, `Calc.cs`
2. If the task involves reverse engineering (decompiling game assemblies, inspecting fields/methods, identifying hook targets):
   - Use `tool_search("dnspy mcp decompile")` to load deferred tools
   - Call `mcp_dnspy_list_assemblies()` to verify availability
   - **If the call fails or returns empty**: stop all reverse-engineering work and tell the user:
     > "The dnSpy MCP tool is unavailable. Please ensure dnSpy is running with its MCP server enabled, configure the MCP connection in your current AI host environment, and retry."
   - **If successful**: proceed; see [./references/dnspy-tools.md](./references/dnspy-tools.md) for tool reference
3. Consult [./references/game-color-arch.md](./references/game-color-arch.md) for the existing game color system analysis — avoid repeating work already done
4. If the task involves building the project, use the Visual Studio MCP (see "Build" section below)

---

## Build via Visual Studio MCP

The build machine runs Visual Studio on Windows (separate from the agent host). A Visual Studio MCP server is available for triggering builds remotely.

**Tool prefix**: `mcp_visualstudio_*` (deferred — load with `tool_search("visual studio build project")` first)

**Workflow**:
```
1. mcp_visualstudio_project_list          → get the full .csproj path
2. mcp_visualstudio_build_project         → pass the full path; build starts asynchronously
3. mcp_visualstudio_build_status          → poll until State == "Done"; check FailedProjects
```

**Availability check**: same pattern as dnSpy — if `project_list` fails, stop and ask the user to ensure the Visual Studio MCP server is running and configured in their host environment.

## Current Development State

### Completed
- `ModEntry.cs`: mod entry point; registers 6 color overrides (r/g/b/y/c/m), instantiates `ColorRenderHook`
- `ColorOverride.cs`: global static dictionary with `Set/Clear/TryGet` interface
- `Calc.cs`: `FluidColorGenerator.Generate()` — derives Base/Highlight1/Highlight2/Minimal/FinalTint from a target color
- `Hook.cs`: 3 postfix hooks intercepting `ShapeColorVisualizationScheme` constructor and `GetData()` overloads; caches original material colors, supports override and restore

### Known Design Tensions (unresolved)
- **InstancingId discontinuity**: overriding creates a new `ColorRenderData` with `InstancingIdManager.AcquirePropertyBlockHash("color-render-data::" + targetColor)` — different key from original → possible GPU instancing batch splits; performance impact unknown
- **Globally shared materials**: `SetMaterialColorProperty()` mutates the `Material` referenced by `MaterialReference`, affecting all objects using it — intentional, but multi-scheme behavior needs verification
- **LOD6Material vs MaterialReference layering**: hook modifies via `MetaShapeColorRenderData` (holds `MaterialReference`), while runtime `ColorRenderData` wraps them as `LOD6Material`; both share the same underlying `Material`, so changes should propagate
- **FluidColorGenerator parameters** (energy=0.6, purity=0.1, lift=0.2) are hardcoded at call site — visual tuning not yet done

### Next Goal: In-Game Color Tweaker Menu

Three sub-goals in priority order:
1. **Inject a custom entry into the pause menu** — hook pause menu UI, add a button to open the ColorTweaker panel
2. **Persist user config** — save color settings to a local JSON file, restore on game start
3. **(Optional) Real-time in-menu preview** — reflect live color changes without leaving the menu

**Strategy decided** (see hud-menu-arch.md):
- No AssetBundle needed: clone `UISettingsBtn`, manually call `btn.Construct(uiSoundPlayer)`, append to `UIButtons` container
- Panel: build UGUI entirely in code (`new GameObject` + `Image` + `Slider` + `InputField`), parent to HUD Canvas
- Persistence: JSON at `Application.persistentDataPath + "/mods/ColorTweaker/config.json"`

See [./references/hud-menu-arch.md](./references/hud-menu-arch.md) for the full implementation plan with code scaffolding.

---

## Project Structure

```
ColorTweaker/src/
├── ModEntry.cs        ← IMod entry; sets up ColorOverrides, creates ColorRenderHook
├── Hook.cs            ← ColorRenderHook: 3 postfix hooks + override/restore logic
├── ColorOverride.cs   ← Global override dictionary (char → UnityEngine.Color)
└── Calc.cs            ← FluidColorGenerator: target color → multi-layer shader colors
```

**Build**: `dotnet build` → output to `$(SPZ2_PERSISTENT)/mods/ColorTweaker/`  
**Log**: `%USERPROFILE%\AppData\LocalLow\tobspr Games\shapez 2\Player.log`

---

## Hook Strategy Summary

```
PostfixCtor           → ShapeColorVisualizationScheme..ctor
                         caches SchemeRenderData (meta + original material colors)

PostfixGetDataFluid   → ShapeColorVisualizationScheme.GetData(IFluid)
PostfixGetDataShape   → ShapeColorVisualizationScheme.GetData(IShapeColor)
                         → GetOverrided(code, self, curRenderData)
                            ├── no override → return as-is (restore materials first if previously overridden)
                            └── has override → generate colors, mutate materials,
                                               construct new ColorRenderData, cache result
```

`DetourHelper.CreatePostfixHook<TInstance, TArg, TReturn>((self, arg) => self.Method(arg), handler)` — ShapezShifter SharpDetour postfix pattern.

---

## Shader Property Reference

| Property | Shader | Description |
|----------|--------|-------------|
| `_FluidBase` | FluidAsteroidGlass | Fluid base color (dark/shadow) |
| `_FluidHighlight1/2` | FluidAsteroidGlass | Fluid highlight layers |
| `_FinalTint` | FluidAsteroidGlass | Final multiplicative tint |
| `_ColorBase` | SpaceFluidGenericShader | Shape base color |
| `_ColorHighlight1/2` | SpaceFluidGenericShader | Shape highlight layers |
| `_Color` | Minimal shaders | Far-distance/LOD simplified color |
| `_BaseColor` (Shader ID) | GPU Instancing PropertyBlock | `MaterialPropertyHelpers.SHADER_ID_BaseColor` |
