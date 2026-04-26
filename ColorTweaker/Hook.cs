using Game.Content.Features.Fluids;
using Game.Core.Shape.Colors;
using MonoMod.RuntimeDetour;
using ShapezShifter.SharpDetour;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ColorTweaker;

using ILogger = Core.Logging.ILogger;

public class ColorRenderHook : IDisposable
{
    private readonly List<Hook> _hooks = new();
    private readonly ILogger _logger;
    class SchemeRenderData
    {
        public Dictionary<char, Dictionary<(MaterialReference, string), Color>> _savedMaterialColors = new();
        public Dictionary<char, MetaShapeColorRenderData> _savedMetaRenderData = new();
        public Dictionary<char, ColorRenderData> _overrideCache = new();
        public readonly ILogger _logger;

        private static Color GetFluidMaterialColor(ShapeColorVisualizationScheme scheme, char code)
        {
            var field = typeof(ShapeColorVisualizationScheme)
                .GetField("FluidMaterials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? typeof(ShapeColorVisualizationScheme)
                    .GetField("_fluidMaterials", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field == null) return default;

            var dict = field.GetValue(scheme) as System.Collections.IDictionary;
            if (dict == null) return default;

            return dict.Contains(code) ? ((ColorRenderData)dict[code]).Color : default;
        }
        public SchemeRenderData(ShapeColorVisualizationScheme scheme, MetaShapeColorVisualizationScheme metaScheme, ILogger logger)
        {
            _logger = logger;
            _logger.Debug?.Log($"Caching color render data for scheme <{metaScheme.name}>.");
            foreach (var kv in metaScheme.RenderData)
            {
                char code = kv.Key.Code;
                _savedMetaRenderData[code] = kv.Value.RenderData;
                _logger.Debug?.Log($"ColorKey[{code}] = RGBA({GetFluidMaterialColor(scheme, code)})");
                var meta = kv.Value.RenderData;
                var colorProps = new Dictionary<(MaterialReference, string), Color>();
                foreach (var mr in new[] { meta.MapResourceMaterial,
                    meta.MapResourceMinimalMaterial,
                    meta.RegularBuildingMaterial,
                    meta.RegularBuildingMinimalMaterial,
                    meta.RegularIslandMaterial,
                    meta.RegularIslandMinimalMaterial })
                {
                    SaveColorProperties(mr, colorProps);
                    LogMaterialProperties(_logger, mr);
                }
                _savedMaterialColors[code] = colorProps;
            }
            _logger.Debug?.Log($"End of scheme <{metaScheme.name}> \n");
        }
        public bool HasOverride(char code) => _overrideCache.ContainsKey(code);

        private static void SaveColorProperties(MaterialReference mr, Dictionary<(MaterialReference, string), Color> dict)
        {
            var mat = mr?.GetMaterialInternal();
            if (mat == null) return;
            var shader = mat.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                {
                    var propName = shader.GetPropertyName(i);
                    dict[(mr, propName)] = mat.GetColor(propName);
                }
            }
        }
    }

    private readonly HashSet<string> _targetColorProperties = new()
    {
        "_Color",       // Minimal 系列 shader
        "_FluidBase",   // FluidAsteroidGlass
        "_ColorBase",   // SpaceFluidGenericShader
    };
    private readonly Dictionary<ShapeColorVisualizationScheme, SchemeRenderData> _Cache = new();


    public ColorRenderHook(ILogger logger)
    {
        _logger = logger;

        var ctor = typeof(ShapeColorVisualizationScheme).GetConstructor(
            new[] { typeof(MetaShapeColorVisualizationScheme) }
        );

        _hooks.Add(new Hook(ctor, PostfixCtor));

        _hooks.Add(DetourHelper.CreatePostfixHook<ShapeColorVisualizationScheme, IFluid, ColorRenderData>(
            (self, fluid) => self.GetData(fluid),
            PostfixGetDataFluid
        ));

        _hooks.Add(DetourHelper.CreatePostfixHook<ShapeColorVisualizationScheme, IShapeColor, ColorRenderData>(
            (self, color) => self.GetData(color),
            PostfixGetDataShape
        ));

        _logger.Info?.Log("ColorRenderHook registered.");

    }

    private void PostfixCtor(Action<ShapeColorVisualizationScheme, MetaShapeColorVisualizationScheme> ctor, ShapeColorVisualizationScheme self, MetaShapeColorVisualizationScheme metaScheme)
    {
        ctor(self, metaScheme);
        _Cache[self] = new SchemeRenderData(self, metaScheme, _logger);
    }

    private ColorRenderData PostfixGetDataFluid(
        ShapeColorVisualizationScheme self, IFluid fluid, ColorRenderData cur)
    {
        if (fluid is ColorFluid cf)
        {
            return GetOverrided(cf.Color.Code, self, cur);
        }
        return cur;
    }

    private ColorRenderData PostfixGetDataShape(
        ShapeColorVisualizationScheme self, IShapeColor color, ColorRenderData cur)
    {
        return GetOverrided(color.Code, self, cur);
    }

    private ColorRenderData GetOverrided(char code, ShapeColorVisualizationScheme self, ColorRenderData curRenderData)
    {
        if (!_Cache.TryGetValue(self, out var cache))
        {
            return curRenderData;
        }

        bool shouldOverride = ColorOverrides.ContainsCode(code);
        bool hasOverrided = cache.HasOverride(code);
        var _savedMetaRenderData = cache._savedMetaRenderData;
        var _savedMaterialColors = cache._savedMaterialColors;
        var _overrideCache = cache._overrideCache;
        if (!shouldOverride)
        {
            if (hasOverrided)
            {
                if (_savedMaterialColors.TryGetValue(code, out var colors))
                {
                    // Restore all saved colors for this code
                    foreach (var kv in colors)
                    {
                        SetMaterialColorProperty(kv.Key.Item1, kv.Key.Item2, kv.Value);
                    }
                }
                _overrideCache.Remove(code);
            }
            return curRenderData;

        }
        // should override
        if (_overrideCache.TryGetValue(code, out var cached))
        {
            return cached;
        }
        else
        {

            if (_savedMetaRenderData.TryGetValue(code, out var meta)
                && _savedMaterialColors.TryGetValue(code, out var colorProperties)
                && ColorOverrides.TryGet(code, out var targetColor))
            {
                var generatedColors = Calc.FluidColorGenerator.Generate(targetColor, .6f, .1f, 0.2f);

                SetMaterialColorProperty(meta.MapResourceMaterial, "_FluidBase", generatedColors.Base);
                SetMaterialColorProperty(meta.MapResourceMaterial, "_FluidHighlight1", generatedColors.Highlight1);
                SetMaterialColorProperty(meta.MapResourceMaterial, "_FluidHighlight2", generatedColors.Highlight2);
                SetMaterialColorProperty(meta.MapResourceMaterial, "_FinalTint", generatedColors.FinalTint);
                SetMaterialColorProperty(meta.MapResourceMinimalMaterial, "_Color", generatedColors.Minimal);
                SetMaterialColorProperty(meta.RegularBuildingMaterial, "_ColorBase", generatedColors.Base);
                SetMaterialColorProperty(meta.RegularBuildingMaterial, "_ColorHighlight1", generatedColors.Highlight1);
                SetMaterialColorProperty(meta.RegularBuildingMaterial, "_ColorHighlight2", generatedColors.Highlight2);
                SetMaterialColorProperty(meta.RegularBuildingMinimalMaterial, "_Color", generatedColors.Minimal);
                SetMaterialColorProperty(meta.RegularIslandMaterial, "_ColorBase", generatedColors.Base);
                SetMaterialColorProperty(meta.RegularIslandMaterial, "_ColorHighlight1", generatedColors.Highlight1);
                SetMaterialColorProperty(meta.RegularIslandMaterial, "_ColorHighlight2", generatedColors.Highlight2);
                SetMaterialColorProperty(meta.RegularIslandMinimalMaterial, "_Color", generatedColors.Minimal);


                var overrideData = new ColorRenderData(meta, targetColor);
                _overrideCache[code] = overrideData;
                return overrideData;
            }
        }
        return curRenderData;
    }

    private static void SetMaterialColorProperty(MaterialReference mr, string propertyName, Color color)
    {
        var mat = mr?.GetMaterialInternal();
        if (mat == null || !mat.HasProperty(propertyName))
        {
            return;
        }
        mat.SetColor(propertyName, color);
    }

    private static void LogMaterialProperties(ILogger logger, MaterialReference mr)
    {
        var mat = mr?.GetMaterialInternal();
        if (mat == null) return;
        var shader = mat.shader;
        int count = shader.GetPropertyCount();
        var props = new System.Text.StringBuilder();
        props.Append($"Material:'{mat.name}' shader:'{shader.name}' properties:\n");
        for (int i = 0; i < count; i++)
        {
            var propName = shader.GetPropertyName(i);
            var propType = shader.GetPropertyType(i);
            switch (propType)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    var color = mat?.GetColor(propName);
                    props.Append($"  {propType} {propName} = {color}\n");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    var vector = mat?.GetVector(propName);
                    props.Append($"  {propType} {propName} = {vector}\n");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                    var floatVal = mat?.GetFloat(propName);
                    props.Append($"  {propType} {propName} = {floatVal}\n");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    var rangeVal = mat?.GetFloat(propName);
                    var rangeLimits = shader.GetPropertyRangeLimits(i);
                    props.Append($"  {propType} {propName} = {rangeVal} Range({rangeLimits.x}, {rangeLimits.y})\n");
                    break;
                default:
                    props.Append($"  {propType} {propName}\n");
                    break;
            }
        }
        logger.Debug?.Log(props.ToString());
    }



    public void Dispose()
    {
        foreach (var hook in _hooks)
            hook.Dispose();
        _hooks.Clear();
    }
}