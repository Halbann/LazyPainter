using UnityEngine;

namespace LazyPainter
{
    public static class Colour
    {
        public static float[] ColortoHSV255(Color rgb)
        {
            Color.RGBToHSV(rgb, out float h, out float s, out float v);
            return new float[] { h * 255, s * 255, v * 255 };
        }

        public static int[] ColortoRGB255(Color color)
        {
            return new int[] {
                Mathf.RoundToInt(color.r * 255),
                Mathf.RoundToInt(color.g * 255),
                Mathf.RoundToInt(color.b * 255)
            };
        }

        public static Color HSV255toColor(float[] HSV)
        {
            return Color.HSVToRGB(HSV[0] / 255, HSV[1] / 255, HSV[2] / 255);
        }

        public static float[] HSV255toRGB255(float[] HSV)
        {
            Color rgb = Color.HSVToRGB(HSV[0] / 255, HSV[1] / 255, HSV[2] / 255);

            return new float[] { Mathf.Round(rgb.r * 255), Mathf.Round(rgb.g * 255), Mathf.Round(rgb.b * 255) };
        }

        public static Color HSV255toRGBA(float[] HSV, float alpha)
        {
            Color rgb = HSV255toColor(HSV);
            rgb.a = alpha;

            return rgb;
        }

        public static float[] RGB255toHSV255(float[] RGB)
        {
            Color rgb = new Color(RGB[0] / 255, RGB[1] / 255, RGB[2] / 255);
            Color.RGBToHSV(rgb, out float h, out float s, out float v);

            return new float[] { Mathf.Round(h * 255), Mathf.Round(s * 255), Mathf.Round(v * 255) };
        }
    }
}