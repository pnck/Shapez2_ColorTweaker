# Game Color System Architecture

> Derived via dnspy MCP decompilation of SPZGameAssembly / Game.Content.Features

## Color Identity

- Each color is uniquely identified by a single `char` code: `'r'`, `'g'`, `'b'`, `'y'`, `'c'`, `'m'` etc.
- `IShapeColor` (Game.Content.Features): interface with a single `Code: char` property
- `MetaShapeColor` (SPZGameAssembly): ScriptableObject implementation; fields `_Code: char` + `Material: ShapeShaderMaterialType`

## Color Scheme Hierarchy (Editor Asset → Runtime)

```
MetaShapeColorVisualizationScheme  (ScriptableObject, editor asset)
  └── RenderData: EditorDict<MetaShapeColor, MetaShapeColorVisualizationScheme.ColorRenderData>
        └── [per color] .RenderData: MetaShapeColorRenderData  (ScriptableObject)
              ├── RegularBuildingMaterial:        MaterialReference
              ├── RegularBuildingMinimalMaterial:  MaterialReference
              ├── RegularIslandMaterial:           MaterialReference
              ├── RegularIslandMinimalMaterial:    MaterialReference
              ├── MapResourceMaterial:             MaterialReference
              ├── MapResourceMinimalMaterial:      MaterialReference
              └── ColorName: SerializedTranslationId
            .Color: UnityEngine.Color  ← editor-configured source color

↓ constructed via new ShapeColorVisualizationScheme(metaScheme):

ShapeColorVisualizationScheme  (runtime, Game.Core.Shape.Colors)
  └── FluidMaterials: IReadOnlyDictionary<char, ColorRenderData>  ← private field (exposed by Publicizer)
        └── [per char code] ColorRenderData  (runtime, Game.Core.Shape.Colors)
              ├── RegularBuildingMaterial:    LOD6Material   ← bundles 6 LOD levels
              ├── RegularIslandMaterial:      LOD6Material
              ├── MapResourceMaterial:        MaterialReference
              ├── MapResourceMinimalMaterial: MaterialReference
              ├── PropertyBlock:              MaterialPropertyBlock  ← SHADER_ID_BaseColor = color.linear
              ├── Color:                      UnityEngine.Color  ← color.linear.WithAlpha(1f)
              ├── InstancingId:               PropertyBlockHash  ← key = "color-render-data::" + color.ToString()
              └── ColorName:                  IText
```

## Key Constructor Code (decompiled)

### ShapeColorVisualizationScheme..ctor
```csharp
this.FluidMaterials = scheme.RenderData.ToDictionary(
    kv => kv.Key.Code,
    kv => new ColorRenderData(kv.Value.RenderData, kv.Value.Color)
);
this.HasColorBlindPattern = scheme.HasColorBlindPattern;
```

### ColorRenderData..ctor
```csharp
this.RegularBuildingMaterial = LOD6Material.From(new[] {
    data.RegularBuildingMaterial, data.RegularBuildingMaterial,
    data.RegularBuildingMinimalMaterial, data.RegularBuildingMinimalMaterial,
    data.RegularBuildingMinimalMaterial, data.RegularBuildingMinimalMaterial
});
this.RegularIslandMaterial = LOD6Material.From(...); // same pattern
this.MapResourceMinimalMaterial = data.MapResourceMinimalMaterial;
this.Color = color.linear.WithAlpha(1f);
this.PropertyBlock = new MaterialPropertyBlock();
this.PropertyBlock.SetColor(MaterialPropertyHelpers.SHADER_ID_BaseColor, this.Color);
this.InstancingId = InstancingIdManager.AcquirePropertyBlockHash("color-render-data::" + color);
```

### GetData methods
```csharp
// GetData(IFluid)
ColorFluid cf = fluid as ColorFluid;
if (cf == null) throw new Exception("Unsupported fluid type: " + fluid.GetType().Name);
return this.FluidMaterials[cf.Color.Code];

// GetData(IShapeColor)
return this.FluidMaterials[color.Code];
```

## Fluid Type

`ColorFluid` (Game.Content.Features):
- Implements `IFluid`, `IItem`
- `Color: IShapeColor`
- `ForColor(IShapeColor): ColorFluid` — static factory backed by a `ConcurrentDictionary` cache

## Color Scheme Interface (IShapeColorScheme)

- `Colors`, `PrimaryColors`, `SecondaryColors`, `TertiaryColors`, `PlayerObtainableColors`
- `GetMixResult(IShapeColor, IShapeColor): IShapeColor` — color mixing result
- `GetColorByCode(char): IShapeColor`

`ShapeColorScheme` implements `IShapeColorScheme` + `IShapeColorSchemeWithVisualizationProvider` + `IShapeColorSchemeVisualizationProvider`.

## GPU Instancing Key Classes

`InstancingIdManager` (Game.Core.Rendering):
- `AcquirePropertyBlockHash(string key): PropertyBlockHash` — cached; same key always returns the same hash
- `AcquireMeshId(): MeshInstanceId`
- `AcquireMaterialId(): MaterialInstanceId`

`MaterialPropertyHelpers` (SPZGameAssembly):
- `SHADER_ID_BaseColor: int` — integer shader property ID for `_BaseColor`
- `SHADER_ID_Alpha: int`
