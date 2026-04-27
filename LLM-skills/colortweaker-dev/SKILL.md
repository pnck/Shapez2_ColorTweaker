---
name: colortweaker-dev
description: "LOAD IMMEDIATELY when working on ColorTweaker mod. Provides full dev context: current implementation status, game color rendering architecture (ShapeColorVisualizationScheme, ColorRenderData, MaterialPropertyBlock), hook strategy via pure MonoMod.RuntimeDetour, HUD/menu injection, dnspy MCP tool usage. Keywords: shapez2 mod, ColorTweaker, color rendering, hook, dnspy, FluidColorGenerator, MonoMod, MaterialReference, LOD6Material, HUDPauseMenu, HUDMenuButton"
---

# ColorTweaker Dev Context

## On Load: Required Steps

1. Read the source files to understand current state: five files under `ColorTweaker/src/` — `ModEntry.cs`, `ColorRenderHook.cs`, `ColorOverride.cs`, `Calc.cs`, `PauseMenuTestItemHook.cs`
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
- `ModEntry.cs`: mod entry point; registers 6 color overrides (r/g/b/y/c/m), instantiates `ColorRenderHook` then `PauseMenuTestItemHook` independently
- `ColorOverride.cs`: global static dictionary with `Set/Clear/TryGet` + `static event Action<char> OnChanged` (fires on Set/Clear)
- `Calc.cs`: `FluidColorGenerator.Generate()` — derives Base/Highlight1/Highlight2/Minimal/FinalTint from a target color
- `ColorRenderHook.cs`: 3 MonoMod hooks on `ShapeColorVisualizationScheme` (ctor + 2 GetData overloads); subscribes to `ColorOverrides.OnChanged` for cache invalidation; no coupling to menu code
- `PauseMenuTestItemHook.cs`: hooks `HUDPauseMenu.Show()` (not `Construct`!) to inject a cloned button; opens `HUDDialogSimpleInput`; no coupling to ColorRenderHook

### Known Design Tensions (unresolved)
- **InstancingId discontinuity**: overriding creates a new `ColorRenderData` with `InstancingIdManager.AcquirePropertyBlockHash("color-render-data::" + targetColor)` — different key from original → possible GPU instancing batch splits; performance impact unknown
- **Globally shared materials**: `SetMaterialColorProperty()` mutates the `Material` referenced by `MaterialReference`, affecting all objects using it — intentional, but multi-scheme behavior needs verification
- **LOD6Material vs MaterialReference layering**: hook modifies via `MetaShapeColorRenderData` (holds `MaterialReference`), while runtime `ColorRenderData` wraps them as `LOD6Material`; both share the same underlying `Material`, so changes should propagate
- **FluidColorGenerator parameters** (energy=0.6, purity=0.1, lift=0.2) are hardcoded at call site — visual tuning not yet done

### Next Goals
1. **Persist user config** — save color settings to a local JSON file, restore on game start (`Application.persistentDataPath + "/mods/ColorTweaker/config.json"`)
2. **Support all 6 color codes** — current dialog is hardcoded to `'r'`; generalize to allow user to select the code
3. **(Optional) Real-time UGUI panel** — Sliders + swatches built programmatically; see hud-menu-arch.md Step 2/3

See [./references/hud-menu-arch.md](./references/hud-menu-arch.md) for the full implementation plan.

---

## Project Structure

```
ColorTweaker/src/
├── ModEntry.cs              ← IMod entry; sets up ColorOverrides, creates both hooks
├── ColorRenderHook.cs       ← 3 MonoMod hooks on ShapeColorVisualizationScheme
├── ColorOverride.cs         ← Global override dictionary + OnChanged event
├── PauseMenuTestItemHook.cs ← Hooks HUDPauseMenu.Show; injects button + dialog flow
└── Calc.cs                  ← FluidColorGenerator: target color → multi-layer shader colors
```

**Build**: `dotnet build` → output to `$(SPZ2_PERSISTENT)/mods/ColorTweaker/`  
**Log**: `%USERPROFILE%\AppData\LocalLow\tobspr Games\shapez 2\Player.log`

---

## Hook Strategy Summary

**Dependency**: `MonoMod.RuntimeDetour` only — ShapezShifter has been removed.

All hooks use `new Hook(methodInfo, handler)` where the handler's first parameter is a typed delegate matching the original method's signature (including the `self` instance as first arg):

```csharp
// Pattern for instance methods:
private delegate ReturnType MyDelegate(TargetType self, Arg1Type arg1, ...);
private ReturnType HookMethod(MyDelegate orig, TargetType self, Arg1Type arg1, ...)
{
    var result = orig(self, arg1, ...);
    // postfix logic here
    return result;
}
// Register:
_hooks.Add(new Hook(methodInfo, HookMethod));
```

**Color rendering hooks** (in `ColorRenderHook.cs`):
```
HookCtor              → ShapeColorVisualizationScheme..ctor
                         caches SchemeRenderData (meta + original material colors)

HookGetDataFluid      → ShapeColorVisualizationScheme.GetData(IFluid)
HookGetDataShape      → ShapeColorVisualizationScheme.GetData(IShapeColor)
                         → GetOverrided(code, self, curRenderData)
                            ├── no override → return as-is (restore materials first if previously overridden)
                            └── has override → generate colors, mutate materials,
                                               construct new ColorRenderData, cache result
```

**Cache invalidation**: `ColorRenderHook` subscribes to `ColorOverrides.OnChanged` in its constructor. When `ColorOverrides.Set()` is called (e.g. from `PauseMenuTestItemHook`), the event fires and `InvalidateOverride(code)` clears `_overrideCache` for that code across all schemes. No direct coupling between the two hook classes.

**Menu injection hook** (in `PauseMenuTestItemHook.cs`):
```
HookShow              → HUDPauseMenu.Show()
                         Called every time the pause menu opens (safe to re-hook repeatedly)
                         Injects button on first call per HUDPauseMenu instance (guarded by name lookup)
```

**CRITICAL**: Hook `HUDPauseMenu.Show()`, NOT `Construct()`. `Construct` is a Zenject DI injection point called once at scene startup — before mod hooks are registered — so it will never fire for a mod hook.

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
