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
| `UISoundPlayer` | `IUISoundPlayer` |
| `DialogStack` | `IHUDDialogStack` |

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

Workaround for dialogs: `Globals.Resources.UIDialogSimpleInputPrefab` / `UIDialogSimpleInfoPrefab` are accessible at runtime despite `Globals.Resources` being marked `[Obsolete]`. This is the only way to access dialog prefabs without an AssetBundle.

### Step 1: Inject Button into Pause Menu (VERIFIED WORKING)

**Hook `HUDPauseMenu.Show()`, NOT `Construct()`.**

> `Construct` is a Zenject `[Construct]` injection point called once at scene startup — **before** mod hooks are registered. `Show()` is called every time the menu opens, and is guaranteed to fire after mod load.

```csharp
private delegate void ShowDelegate(HUDPauseMenu self);

private void HookShow(ShowDelegate orig, HUDPauseMenu self)
{
    orig(self);
    try { InjectButtonIfNeeded(self); }
    catch (Exception ex) { _logger.Info?.Log($"error: {ex}"); }
}

private void InjectButtonIfNeeded(HUDPauseMenu self)
{
    // Guard: inject only once per HUDPauseMenu instance
    if (self.UIButtons.transform.Find("ColorTweaker_TestItem") != null) return;

    var instance = UnityEngine.Object.Instantiate(self.UISettingsBtn.gameObject, self.UIButtons.transform);
    instance.name = "ColorTweaker_TestItem";
    var button = instance.GetComponent<HUDMenuButton>();

    // REQUIRED: After Instantiate, non-Unity-serialized fields (Resolver, etc.)
    // are null on the clone. Must be copied before Construct is called.
    InitClonedLocalizedText(self.UISettingsBtn.UIText, button.UIText);
    button.Construct(self.UISoundPlayer);
    button.Text = new RawText("ColorTweaker Panel");
    button.OnClick.RemoveAllListeners();
    button.OnClick.AddListener(() => OpenDialog(self.DialogStack));
}

// HUDLocalizedText.Resolver (ILocalizationResolver) is an interface injected by Zenject.
// Object.Instantiate does NOT copy it — UpdateView() throws "not constructed yet" if null.
// Solution: copy from the original button before calling Construct().
private static void InitClonedLocalizedText(HUDLocalizedText original, HUDLocalizedText clone)
{
    clone.Resolver = original.Resolver;
    clone._TextStyleProvider = original._TextStyleProvider;
    clone.Builder = new System.Text.StringBuilder();
}
```

**Key fields available on `self` inside `HookShow`** (populated by Construct, already valid by Show time):
- `self.UISoundPlayer` — pass to `button.Construct()`
- `self.DialogStack` — pass to dialog Show calls
- `self.UISettingsBtn.UIText` — source for `InitClonedLocalizedText`

### Step 2: Dialog for Color Input (VERIFIED WORKING)

```csharp
var dialog = dialogStack.Show<HUDDialogSimpleInput>(Globals.Resources.UIDialogSimpleInputPrefab);
dialog.Init(
    new RawText("Title"),
    new RawText("Description"),
    new RawText("Button label"),
    new RawText("default value"),
    correctorFunc   // Func<string, string>; return cleaned input
);
dialog.OnConfirmed.Register(input => { /* handle */ });
```

Info dialog:
```csharp
var info = dialogStack.Show<HUDDialogSimpleInfo>(Globals.Resources.UIDialogSimpleInfoPrefab);
info.Init(new RawText("Title"), new RawText("Message"), new RawText("OK"));
```

### Step 3: Cache Invalidation without Coupling

When the dialog applies a new color:
```csharp
ColorOverrides.Set('r', color);  // fires ColorOverrides.OnChanged
// ColorRenderHook receives OnChanged event and clears its own cache automatically
// PauseMenuTestItemHook does NOT know about ColorRenderHook
```

### Step 4 (Future): Full UGUI Color Panel

Build once, parent to `self.transform.parent` (HUD Canvas):
```csharp
var panelGO = new GameObject("ColorTweakerPanel");
panelGO.transform.SetParent(self.transform.parent, worldPositionStays: false);
var rect = panelGO.AddComponent<RectTransform>();
rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
rect.offsetMin = rect.offsetMax = Vector2.zero;
var bg = panelGO.AddComponent<Image>();
bg.color = new Color(0.08f, 0.08f, 0.08f, 0.93f);
panelGO.AddComponent<GraphicRaycaster>();
panelGO.SetActive(false);
```

Per color row: `[Label]  [Swatch]  [R Slider] [G Slider] [B Slider]  [Hex InputField]`
- `Slider.onValueChanged` → `ColorOverrides.Set(code, newColor)` (invalidation automatic via event)

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
