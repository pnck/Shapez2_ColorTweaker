# HUD / Menu System Analysis (Complete)

> Fully analyzed via dnspy deep decompilation + ShapezShifter source review.

## Core HUD Class Hierarchy

```
HUDComponent  (MonoBehaviour base)
  ├── HUDPart        ← independent HUD module; has Events/Player/Raycaster
  │     └── HUDPauseMenu   ← pause menu
  └── HUDDialog      ← modal dialog base, implements IPoolable
        ├── HUDDialogSimpleConfirm
        ├── HUDDialogSimpleInfo
        ├── HUDDialogSimpleInput
        └── ... (14 dialog types total)
  └── HUDSettingsRenderer  ← settings panel with tabs (General/Graphics/Sound/Keybindings)
```

## HUDPauseMenu — Full Analysis

### Fields (from get_type_info)
| Field | Type |
|-------|------|
| `UIBackground` | `UnityEngine.CanvasGroup` |
| `UIStats` / `UIStats2` / `UIBackBtnTransform` / `UIButtons` | `UnityEngine.GameObject` |
| `UIBackgroundLinesTransform` | `UnityEngine.RectTransform` |
| `UIBackBtn` | `HUDMenuBackButton` |
| `UIContinueBtn` / `UIMenuBtn` / `UISaveBtn` / `UISettingsBtn` / `UIExitBtn` | **`HUDMenuButton`** |
| `UIPlaytimeStatText` and other stat labels | `HUDLocalizedText` |

### `Construct(...)` key behavior
```csharp
base.gameObject.SetActive(false);
base.Events.ShowPauseMenu.Register(new Action(this.Show));
this.UIContinueBtn.OnClick.AddListener(new UnityAction(this.Hide));
this.UISettingsBtn.OnClick.AddListener(new UnityAction(base.Events.ShowPauseMenuSettings.Invoke));
// other buttons follow the same pattern
```

### `Show()` key behavior
```csharp
this.Visible = true;
base.gameObject.SetActive(true);
this.SimulationSpeedManager.IsPaused = true;
this.CurrentAnimation = DOTween.Sequence();
this.CurrentAnimation.Join(this.UIBackground.DOFade(1f, 0.2f, ...));
this.CurrentAnimation.Join(HUDTheme.AnimateSideUITopIn(this.UIButtons.transform, ...));
```
`UIButtons.transform` is the button container; the DOTween animation slides all children in from the top.

### Inferred HUD hierarchy
```
HUD Canvas
  └── HUDPauseMenu.gameObject (= self.gameObject)
       ├── UIBackground (CanvasGroup)
       ├── UIButtons (GameObject)  ← button container, DOTween target
       │    ├── UIContinueBtn (HUDMenuButton)
       │    ├── UISettingsBtn (HUDMenuButton)
       │    ├── UISaveBtn (HUDMenuButton)
       │    ├── UIMenuBtn (HUDMenuButton)
       │    └── UIExitBtn (HUDMenuButton)
       ├── UIStats (GameObject)
       └── ...
```

**HUDPart static token fields** (used for UI region identification):
- `TokenRightUpperScreenArea`
- `TokenToolbarContextActions`
- `TokenRender3D`
- `TokenFullscreenOverlay`

## HUDEvents (Global HUD Event Bus)

Key events (`Core.Events.MultiRegisterEvent`):
- `ShowPauseMenu` — triggers pause menu display
- `ShowPauseMenuSettings` — triggers settings panel display
- `HUDInitialized` — HUD fully initialized

Others: `ShowResearch`, `ShowStatistics`, `ShowWiki`, `ShowShapeViewer`, ~30 total.

## HUDMenuButton — Full API

```csharp
// Properties
UnityEvent OnClick { get; }          // public
void set_Text(IText value)           // public; IText lives in Core.Localization

// Methods (all public; accessible via Publicizer)
void Construct(IUISoundPlayer uiSoundPlayer)   // [Construct] DI entry point
void Select()
void SetHighlighted(bool highlighted, bool forceUpdate)
```

### Key fields
| Field | Type |
|-------|------|
| `Translation` | `SerializedTranslationId` |
| `UIButton` | `UnityEngine.UI.Button` |
| `UIMainTransform` | `UnityEngine.RectTransform` |
| `UIText` | `HUDLocalizedText` |
| `UIActiveIndicatorGroup` / `UIHoverIndicatorGroup` | `UnityEngine.CanvasGroup` |

### Clone + manual injection pattern
```csharp
// Inside a HUDPauseMenu.Construct postfix hook
var newGO = Object.Instantiate(self.UISettingsBtn.gameObject, self.UIButtons.transform);
var btn = newGO.GetComponent<HUDMenuButton>();
btn.Construct(uiSoundPlayer);          // manually call DI entry point (clone bypasses Zenject)
btn.Text = new RawText("Color Tweaker");
btn.OnClick.AddListener(() => _colorTweakerPanel.Open());
```

> `new RawText(string)` is in the `Core.Localization` namespace (assembly `Core.Localization.dll`).

## HUDSettingsRenderer — Restricted API

```csharp
void CreateContentGroup<T>(IText headerTitle, PrefabViewReference<T> contentGroupPrefab)
    where T : HUDComponent, IHUDSettingsContentGroup
```
**Requires `PrefabViewReference<T>` (AssetBundle) — not usable in mods without an AssetBundle.**

## ShapezShifter UI Findings

After full source review, ShapezShifter contains **no HUD/UI helper layer**:
- No HUD hook utilities
- No UI creation helpers
- Only Toolbar support (via `ToolbarData` data object with prefix/postfix hooks on `ToolbarBuilder.BuildToolbar()`)

---

## Decided Implementation Strategy: Programmatic UGUI (No AssetBundle)

### Core Rationale

All native UI extension APIs require an AssetBundle:
- `IHUDDialogStack.Show<T>()` needs `PrefabReference<T>`
- `HUDSettingsRenderer.CreateContentGroup<T>()` needs `PrefabViewReference<T>`
- ShapezShifter provides no UI helpers

Solution: **build the panel entirely in code using Unity UGUI APIs**.

### Step 1: Inject Button into Pause Menu

Hook `HUDPauseMenu.Construct()` **postfix**:

```csharp
// Clone an existing button to inherit full styling
var newGO = Object.Instantiate(self.UISettingsBtn.gameObject, self.UIButtons.transform);
var btn = newGO.GetComponent<HUDMenuButton>();
btn.Construct(uiSoundPlayer);          // manual DI injection (clone does not go through Zenject)
btn.Text = new RawText("Color Tweaker");
btn.OnClick.AddListener(() => _panel.Toggle());
```

- `uiSoundPlayer` comes from `HUDPauseMenu.Construct` hook parameters
- As a sibling clone, layout components (VerticalLayoutGroup, etc.) arrange it automatically
- No DOTween handling needed — the `UIButtons` container animation covers all children

### Step 2: Build the Custom Color Settings Panel

Construct once (lazy init) in the hook postfix:

```csharp
// Parent = HUDPauseMenu's parent (HUD Canvas or direct container)
var panelGO = new GameObject("ColorTweakerPanel");
panelGO.transform.SetParent(self.transform.parent, worldPositionStays: false);

// RectTransform — fullscreen
var rect = panelGO.AddComponent<RectTransform>();
rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
rect.offsetMin = rect.offsetMax = Vector2.zero;

// Background
var bg = panelGO.AddComponent<Image>();
bg.color = new Color(0.08f, 0.08f, 0.08f, 0.93f);

// Block clicks from passing through to the game scene
panelGO.AddComponent<GraphicRaycaster>();

// Content layout (VerticalLayoutGroup)
var content = new GameObject("Content");
content.transform.SetParent(panelGO.transform, false);
// ... add Slider × 3 (H/S/V or R/G/B) × 6 color codes

panelGO.SetActive(false);
_panel = panelGO;
```

### Step 3: Color Edit UI Layout

Per color row (e.g. red `r`):
```
[Label "Red"]  [Color Swatch]  [R Slider] [G Slider] [B Slider]  [Hex Input]
```
- `UnityEngine.UI.Slider` (min=0, max=1)
- `Slider.onValueChanged` → call `ColorOverrides.Set(code, newColor)` + `_hook.ClearCache(code)` live
- `UnityEngine.UI.InputField` for hex entry
- `UnityEngine.UI.Image` swatch; background = current color

### Step 4: Config Persistence (JSON)

Path: `Application.persistentDataPath + "/mods/ColorTweaker/config.json"`

```json
{
  "overrides": {
    "r": [0.82, 0.07, 0.08],
    "g": [0.13, 0.65, 0.08],
    "b": [0.08, 0.16, 0.85]
  }
}
```

- **Load**: in `ModEntry.Construct()`, read JSON and call `ColorOverrides.Set` to restore settings
- **Save**: on panel close (or live), write with `System.IO.File.WriteAllText`
- Call `Directory.CreateDirectory` to ensure the directory exists

### Key Implementation Notes

1. **Canvas hierarchy**: `panelGO.transform.SetParent(self.transform.parent)` assumes `HUDPauseMenu` is a direct child of the HUD Canvas; if not, traverse up with `.root` or find the `Canvas` component
2. **CanvasGroup interactability**: `SetActive(false)` is sufficient to hide; on show ensure `CanvasGroup.alpha=1, interactable=true`
3. **DI timing**: `HUDPauseMenu.Construct()` is called during HUD init while the Unity scene is fully loaded — safe to create UGUI objects
4. **Thread safety**: all UGUI operations must be on the main thread (Unity requirement)
5. **Slider precision**: color values are 0–1 float; Slider step = 0 (continuous)

### Font & Styling

The game uses a custom TextMeshPro font. Simplest options:
- Use legacy `Text` component with `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
- Or grab the TMP font reference from an existing game text object via `GameObject.Find` / `FindObjectOfType`

### Persistence Scope: Global vs Per-Savegame

ColorTweaker color settings are **global user preferences** (not bound to a savegame) — use a flat JSON file, not ShapezShifter's `AttachSaveData<T>` (which ties data to a specific save).
