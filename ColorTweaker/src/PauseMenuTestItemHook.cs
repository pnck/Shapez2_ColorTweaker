using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Core.Localization;
using MonoMod.RuntimeDetour;
using UnityEngine;

namespace ColorTweaker;

using ILogger = Core.Logging.ILogger;

public class PauseMenuTestItemHook : IDisposable
{
    private readonly List<Hook> _hooks = new();
    private readonly ILogger _logger;

    public PauseMenuTestItemHook(ILogger logger)
    {
        _logger = logger;

        var show = typeof(HUDPauseMenu).GetMethod(
            "Show",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null
        );

        if (show == null)
        {
            _logger.Info?.Log("ColorTweaker: failed to resolve HUDPauseMenu.Show hook target.");
            return;
        }

        _hooks.Add(new Hook(show, HookShow));
        _logger.Info?.Log("PauseMenuTestItemHook registered.");
    }

    private delegate void ShowDelegate(HUDPauseMenu self);

    private void HookShow(ShowDelegate orig, HUDPauseMenu self)
    {
        orig(self);

        try
        {
            InjectButtonIfNeeded(self);
        }
        catch (Exception ex)
        {
            _logger.Info?.Log($"ColorTweaker: error injecting button: {ex}");
        }
    }

    private void InjectButtonIfNeeded(HUDPauseMenu self)
    {
        if (self?.UIButtons == null || self.UISettingsBtn == null)
        {
            _logger.Info?.Log("ColorTweaker: UIButtons or UISettingsBtn is null, skipping injection.");
            return;
        }

        if (self.UIButtons.transform.Find("ColorTweaker_TestItem") != null)
        {
            return;
        }

        var instance = UnityEngine.Object.Instantiate(self.UISettingsBtn.gameObject, self.UIButtons.transform);
        instance.name = "ColorTweaker_TestItem";

        var button = instance.GetComponent<HUDMenuButton>();
        if (button == null)
        {
            _logger.Info?.Log("ColorTweaker: HUDMenuButton component not found on clone.");
            return;
        }

        // After Instantiate, non-Unity-serialized fields (interfaces like Resolver) are null on the clone.
        // Copy them from the original button so HUDLocalizedText.UpdateView() doesn't throw.
        InitClonedLocalizedText(self.UISettingsBtn.UIText, button.UIText);
        button.Construct(self.UISoundPlayer);
        button.Text = new RawText("ColorTweaker Panel");
        button.OnClick.RemoveAllListeners();
        button.OnClick.AddListener(() => OpenColorInputDialog(self.DialogStack));
        _logger.Info?.Log("ColorTweaker panel button injected into pause menu.");
    }

    // Copy non-serialized localization infrastructure from original to clone
    // so that HUDLocalizedText.UpdateView() (called inside Construct) does not throw.
    private static void InitClonedLocalizedText(HUDLocalizedText original, HUDLocalizedText clone)
    {
        clone.Resolver = original.Resolver;
        clone._TextStyleProvider = original._TextStyleProvider;
        clone.Builder = new StringBuilder();
    }

    private void OpenColorInputDialog(IHUDDialogStack dialogStack)
    {
        if (dialogStack == null)
        {
            _logger.Info?.Log("Dialog stack unavailable when opening ColorTweaker panel.");
            return;
        }

        var defaultColor = ColorOverrides.TryGet('r', out var current)
            ? $"#{ColorUtility.ToHtmlStringRGB(current)}"
            : "#D2122E";

        var dialog = dialogStack.Show<HUDDialogSimpleInput>(Globals.Resources.UIDialogSimpleInputPrefab);
        dialog.Init(
            new RawText("ColorTweaker - Input Color"),
            new RawText("Target code: r\nFormats: #RRGGBB / #RRGGBBAA / r,g,b / r,g,b,a"),
            new RawText("Apply"),
            new RawText(defaultColor),
            CorrectColorInput
        );
        dialog.OnConfirmed.Register(input => ApplyParsedColorInput(dialogStack, input));
    }

    private string CorrectColorInput(string input)
    {
        return input?.Trim() ?? string.Empty;
    }

    private void ApplyParsedColorInput(IHUDDialogStack dialogStack, string input)
    {
        if (TryParseColorInput(input, out var color, out var normalized))
        {
            ColorOverrides.Set('r', color);
            _logger.Info?.Log($"ColorTweaker parsed 'r' = {normalized} from input '{input}'.");
            ShowInfoDialog(dialogStack, $"Applied to 'r': {normalized}");
            return;
        }

        ShowInfoDialog(dialogStack, $"Invalid color input: '{input}'");
    }

    private static void ShowInfoDialog(IHUDDialogStack dialogStack, string message)
    {
        if (dialogStack == null)
        {
            return;
        }

        var infoDialog = dialogStack.Show<HUDDialogSimpleInfo>(Globals.Resources.UIDialogSimpleInfoPrefab);
        infoDialog.Init(
            new RawText("ColorTweaker"),
            new RawText(message),
            new RawText("OK")
        );
    }

    private static bool TryParseColorInput(string raw, out Color color, out string normalized)
    {
        color = default;
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var input = raw.Trim();
        if (TryParseHexColor(input, out color))
        {
            normalized = $"#{ColorUtility.ToHtmlStringRGBA(color)}";
            return true;
        }

        if (TryParseCsvColor(input, out color))
        {
            normalized = $"#{ColorUtility.ToHtmlStringRGBA(color)}";
            return true;
        }

        return false;
    }

    private static bool TryParseHexColor(string input, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        var candidate = trimmed;
        if (candidate[0] != '#')
        {
            if (candidate.Length != 6 && candidate.Length != 8)
            {
                return false;
            }

            candidate = "#" + candidate;
        }

        return ColorUtility.TryParseHtmlString(candidate, out color);
    }

    private static bool TryParseCsvColor(string input, out Color color)
    {
        color = default;
        var parts = input.Split(',');
        if (parts.Length != 3 && parts.Length != 4)
        {
            return false;
        }

        var values = new float[4] { 0f, 0f, 0f, 1f };
        for (var i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }

            values[i] = parsed;
        }

        var useByteRange = values[0] > 1f || values[1] > 1f || values[2] > 1f || values[3] > 1f;
        if (useByteRange)
        {
            values[0] /= 255f;
            values[1] /= 255f;
            values[2] /= 255f;
            values[3] /= 255f;
        }

        color = new Color(
            Mathf.Clamp01(values[0]),
            Mathf.Clamp01(values[1]),
            Mathf.Clamp01(values[2]),
            Mathf.Clamp01(values[3])
        );
        return true;
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            hook.Dispose();
        }

        _hooks.Clear();
    }
}
