
namespace ColorTweaker
{
    namespace Calc
    {
        using UnityEngine;

        public static class FluidColorGenerator
        {
            public class FluidScheme
            {
                public Color Base;        // _FluidBase / _ColorBase
                public Color Highlight1;  // _FluidHighlight1 / _ColorHighlight1
                public Color Highlight2;  // _FluidHighlight2 / _ColorHighlight2
                public Color Minimal;     // _Color (Minimal)
                public Color FinalTint;   // _FinalTint
            }

            /// <summary>
            /// targetColor : 视觉目标色
            /// energy      : 层次分离强度。越大 => Base 更深、Highlight 更强
            /// purity      : 色纯度分配。>0 更饱和，<0 更偏灰/偏发光
            /// lift        : 整体提亮量。作用于目标锚点，不只是拉开对比
            /// </summary>
            public static FluidScheme Generate(
                Color targetColor,
                float energy = 1.0f,
                float purity = 0.0f,
                float lift = 0.0f)
            {
                FluidScheme scheme = new FluidScheme();

                Vector3 target = new Vector3(targetColor.r, targetColor.g, targetColor.b);

                float luminance = GetLuminance(target);
                Vector3 gray = Replicate(luminance);
                Vector3 chroma = target - gray;

                Color.RGBToHSV(targetColor, out _, out float saturation, out _);

                float chromaWeight = Smooth01(saturation);     // 颜色越饱和，色度操作越可信
                float energy01 = NormalizeEnergy(energy);      // 把 energy 压成平滑增长
                float purity01 = 0.5f * (Mathf.Clamp(purity, -1f, 1f) + 1f);
                float lift01 = Mathf.Clamp(lift, -1f, 1f);

                // ------------------------------------------------------------
                // 1) 提亮后的目标锚点
                // ------------------------------------------------------------
                // lift 的作用：
                // - 对 gray：整体亮度抬升
                // - 对 chroma：少量同步抬升，避免颜色一亮就发灰
                float liftedGrayScale =
                    1f + lift01 * Mathf.Lerp(Model.LiftGrayLowPurity, Model.LiftGrayHighPurity, purity01);

                float liftedChromaScale =
                    1f + lift01 * chromaWeight * Mathf.Lerp(Model.LiftChromaLowPurity, Model.LiftChromaHighPurity, purity01);

                Vector3 liftedTarget =
                    gray * liftedGrayScale +
                    chroma * liftedChromaScale;

                // ------------------------------------------------------------
                // 2) Minimal
                // ------------------------------------------------------------
                // Minimal 是远景锚点色：
                // - 应非常接近 target
                // - 只做很轻微的协调
                Vector3 minimalGray = Replicate(GetLuminance(liftedTarget));
                Vector3 minimalChroma = liftedTarget - minimalGray;

                float minimalGrayScale =
                    Mathf.Lerp(Model.MinimalGrayForNeutral, Model.MinimalGrayForChromatic, chromaWeight);

                float minimalChromaScale =
                    Mathf.Lerp(
                        1f - Model.MinimalChromaReduceAtLowPurity * (1f - purity01),
                        1f + Model.MinimalChromaBoostAtHighPurity * purity01,
                        chromaWeight
                    );

                Vector3 minimalRgb =
                    minimalGray * minimalGrayScale +
                    minimalChroma * minimalChromaScale;

                minimalRgb = Clamp01(minimalRgb);

                // 后续近景层围绕 Minimal 展开
                Vector3 anchor = minimalRgb;
                float anchorLuma = GetLuminance(anchor);
                Vector3 anchorGray = Replicate(anchorLuma);
                Vector3 anchorChroma = anchor - anchorGray;

                // ------------------------------------------------------------
                // 3) Base
                // ------------------------------------------------------------
                // Base = 厚度 / 吸收 / 暗部骨架
                // - gray 部分控制“暗多少”
                // - chroma 部分控制“颜色还留多少”
                float baseGrayScale =
                    Mathf.Lerp(Model.BaseGrayNeutral, Model.BaseGrayChromatic, chromaWeight) *
                    (1f - Model.BaseEnergyDarkening * energy01);

                float baseChromaScale =
                    Mathf.Lerp(Model.BaseChromaLowPurity, Model.BaseChromaHighPurity, purity01) *
                    (1f - Model.BaseChromaEnergyDarkening * energy01);

                Vector3 baseRgb =
                    anchorGray * baseGrayScale +
                    anchorChroma * baseChromaScale;

                // ------------------------------------------------------------
                // 4) Highlight1
                // ------------------------------------------------------------
                // Highlight1 = 主高光
                // - gray 增强：决定“发光感”
                // - chroma 增强：决定“发色感”
                // - 小角度绕灰轴旋转：制造层次，不按具体颜色分支
                float hueAngle =
                    (Model.HighlightHueRotateBase + Model.HighlightHueRotateByEnergy * energy01) *
                    chromaWeight *
                    Mathf.Lerp(Model.HighlightHueRotateLowPurity, Model.HighlightHueRotateHighPurity, purity01);

                float h1GrayScale =
                    1f + energy01 *
                    Mathf.Lerp(
                        Model.Highlight1GrayNeutralBoost * Mathf.Sqrt(Mathf.Max(anchorLuma, 0f)),
                        Model.Highlight1GrayChromaticBoost,
                        chromaWeight
                    );

                float h1ChromaScale =
                    1f + energy01 * chromaWeight *
                    Mathf.Lerp(Model.Highlight1ChromaLowPurity, Model.Highlight1ChromaHighPurity, purity01);

                Vector3 h1Rgb =
                    anchorGray * h1GrayScale +
                    RotateAroundGrayAxis(anchorChroma, +hueAngle) * h1ChromaScale;

                // ------------------------------------------------------------
                // 5) Highlight2
                // ------------------------------------------------------------
                // Highlight2 = 次级偏色 / 边缘色
                // 它是小修正项，不承担主能量。
                float h2GrayScale =
                    1f + Model.Highlight2GrayBoost * energy01;

                float h2ChromaScale =
                    1f + energy01 * chromaWeight *
                    Mathf.Lerp(Model.Highlight2ChromaLowPurity, Model.Highlight2ChromaHighPurity, purity01);

                Vector3 h2Raw =
                    anchorGray * h2GrayScale +
                    RotateAroundGrayAxis(anchorChroma, -Model.Highlight2HueRotateRatio * hueAngle) * h2ChromaScale;

                float h2Mix =
                    Model.Highlight2MixBase +
                    Model.Highlight2MixByEnergy * energy01 * chromaWeight;

                Vector3 h2Rgb = Vector3.Lerp(anchor, h2Raw, h2Mix);

                // ------------------------------------------------------------
                // 6) FinalTint
                // ------------------------------------------------------------
                // neutralTint   : 黑白灰主导时的整体能量
                // chromaticTint : 彩色主导时的整体能量
                float neutralTint =
                    Mathf.Lerp(
                        Model.FinalTintNeutralMin,
                        Model.FinalTintNeutralMax,
                        Mathf.Pow(Mathf.Clamp01(anchorLuma), Model.FinalTintNeutralGamma)
                    );

                float chromaticTint =
                    Mathf.Lerp(
                        Model.FinalTintChromaticMin,
                        Model.FinalTintChromaticMax,
                        Mathf.Clamp01(anchorLuma)
                    ) *
                    Mathf.Lerp(1f, Model.FinalTintEnergyBoost, energy01);

                float tint =
                    Mathf.Lerp(neutralTint, chromaticTint, chromaWeight);

                scheme.Minimal = ToColor(Clamp01(minimalRgb));
                scheme.Base = ToColor(Clamp01(baseRgb));
                scheme.Highlight1 = ToColor(Max0(h1Rgb));
                scheme.Highlight2 = ToColor(Max0(h2Rgb));
                scheme.FinalTint = new Color(tint, tint, tint, 0f);

                return scheme;
            }

            private static class Model
            {
                // ------------------------------------------------------------
                // Lift
                // ------------------------------------------------------------
                // lift 对 gray 的作用：
                // purity 低时更偏“提亮”，purity 高时更克制
                public const float LiftGrayLowPurity = 0.22f;
                public const float LiftGrayHighPurity = 0.10f;

                // lift 对 chroma 的作用：
                // purity 高时允许更明显的“提亮且不发灰”
                public const float LiftChromaLowPurity = 0.04f;
                public const float LiftChromaHighPurity = 0.18f;

                // ------------------------------------------------------------
                // Minimal
                // ------------------------------------------------------------
                // Minimal 应非常接近 target，只做轻协调
                public const float MinimalGrayForNeutral = 1.00f;
                public const float MinimalGrayForChromatic = 0.985f;

                public const float MinimalChromaReduceAtLowPurity = 0.05f;
                public const float MinimalChromaBoostAtHighPurity = 0.06f;

                // ------------------------------------------------------------
                // Base
                // ------------------------------------------------------------
                public const float BaseGrayNeutral = 0.28f;
                public const float BaseGrayChromatic = 0.18f;
                public const float BaseEnergyDarkening = 0.22f;

                public const float BaseChromaLowPurity = 0.22f;
                public const float BaseChromaHighPurity = 0.34f;
                public const float BaseChromaEnergyDarkening = 0.18f;

                // ------------------------------------------------------------
                // Highlight hue rotation
                // ------------------------------------------------------------
                public const float HighlightHueRotateBase = 0.04f;
                public const float HighlightHueRotateByEnergy = 0.08f;
                public const float HighlightHueRotateLowPurity = 0.70f;
                public const float HighlightHueRotateHighPurity = 1.00f;

                // ------------------------------------------------------------
                // Highlight1
                // ------------------------------------------------------------
                public const float Highlight1GrayNeutralBoost = 7.0f;
                public const float Highlight1GrayChromaticBoost = 1.2f;

                public const float Highlight1ChromaLowPurity = 1.00f;
                public const float Highlight1ChromaHighPurity = 2.40f;

                // ------------------------------------------------------------
                // Highlight2
                // ------------------------------------------------------------
                public const float Highlight2GrayBoost = 0.10f;

                public const float Highlight2ChromaLowPurity = 0.04f;
                public const float Highlight2ChromaHighPurity = 0.24f;

                // h2 的旋转角是 h1 的倍数
                public const float Highlight2HueRotateRatio = 1.6f;

                // h2 最终会往 anchor 拉回，保证它是“小修正项”
                public const float Highlight2MixBase = 0.08f;
                public const float Highlight2MixByEnergy = 0.10f;

                // ------------------------------------------------------------
                // FinalTint
                // ------------------------------------------------------------
                public const float FinalTintNeutralMin = 1.0f;
                public const float FinalTintNeutralMax = 12.0f;
                public const float FinalTintNeutralGamma = 0.72f;

                public const float FinalTintChromaticMin = 3.0f;
                public const float FinalTintChromaticMax = 8.0f;
                public const float FinalTintEnergyBoost = 1.22f;
            }

            /// <summary>
            /// 把无限增长的 energy 压成平滑增长的 0..1 量
            /// 这样高 energy 不会一下把颜色推爆
            /// </summary>
            private static float NormalizeEnergy(float energy)
            {
                float e = Mathf.Max(0f, energy);
                return e / (1f + e);
            }

            /// <summary>
            /// smoothstep(0,1,x)
            /// 把 saturation 转成更平滑的权重
            /// </summary>
            private static float Smooth01(float x)
            {
                x = Mathf.Clamp01(x);
                return x * x * (3f - 2f * x);
            }

            /// <summary>
            /// 感知亮度
            /// </summary>
            private static float GetLuminance(Vector3 c)
            {
                return 0.2126f * c.x + 0.7152f * c.y + 0.0722f * c.z;
            }

            /// <summary>
            /// 把一个标量复制成灰度向量
            /// </summary>
            private static Vector3 Replicate(float x)
            {
                return new Vector3(x, x, x);
            }

            /// <summary>
            /// 围绕灰轴 (1,1,1) 旋转向量
            /// 这是统一的小幅色相扰动方式，不依赖具体颜色分支
            /// </summary>
            private static Vector3 RotateAroundGrayAxis(Vector3 v, float angle)
            {
                Vector3 axis = new Vector3(
                    0.57735026919f,
                    0.57735026919f,
                    0.57735026919f
                );

                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                return
                    v * cos +
                    Vector3.Cross(axis, v) * sin +
                    axis * Vector3.Dot(axis, v) * (1f - cos);
            }

            private static Vector3 Clamp01(Vector3 v)
            {
                return new Vector3(
                    Mathf.Clamp01(v.x),
                    Mathf.Clamp01(v.y),
                    Mathf.Clamp01(v.z)
                );
            }

            private static Vector3 Max0(Vector3 v)
            {
                return new Vector3(
                    Mathf.Max(0f, v.x),
                    Mathf.Max(0f, v.y),
                    Mathf.Max(0f, v.z)
                );
            }

            private static Color ToColor(Vector3 v)
            {
                return new Color(v.x, v.y, v.z, 0f);
            }
        }
    }
}
