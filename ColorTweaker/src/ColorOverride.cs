using System.Collections.Generic;
using UnityEngine;

namespace ColorTweaker;

public static class ColorOverrides
{
    private static readonly Dictionary<char, Color> _overrides = new();

    public static void Set(char code, Color color) => _overrides[code] = color;
    public static void Clear(char code) => _overrides.Remove(code);
    public static bool TryGet(char code, out Color color) => _overrides.TryGetValue(code, out color);
    public static bool ContainsCode(char code) => _overrides.ContainsKey(code);
}