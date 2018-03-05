namespace Utilities
{
    using System;
    using System.Diagnostics;

    public static class ColorExtensions
    {
        public static System.Windows.Media.Color AdjustAlpha(this System.Windows.Media.Color color, byte alpha)
        {
            Debug.Assert(color.A == 0xff);
            return System.Windows.Media.Color.FromArgb(alpha, color.R, color.G, color.B);
        }

        public static System.Windows.Media.Color MultiplyLightness(this System.Windows.Media.Color color, float lightnessFactor)
        {
            Debug.Assert(color.A == 0xff);
            color.GetHSL(out float hue, out float saturation, out float lightness);
            lightness = Clamp(lightness * lightnessFactor, 0, 1);
            return FromHSL(hue, saturation, lightness).Convert();
        }

        public static System.Windows.Media.Color MultiplySaturation(this System.Windows.Media.Color color, float saturationFactor)
        {
            Debug.Assert(color.A == 0xff);
            color.GetHSL(out float hue, out float saturation, out float lightness);
            saturation = Clamp(saturation * saturationFactor, 0, 1);
            return FromHSL(hue, saturation, lightness).Convert();
        }

        public static System.Windows.Media.Color Extreme(this System.Windows.Media.Color color, float factor)
        {
            Debug.Assert(color.A == 0xff);

            color.GetHSL(out float hue, out float saturation, out float lightness);
            if (lightness < 0.5)
            {
                lightness = Clamp((float)Math.Pow(lightness, factor), 0, 1);
            }
            else if (lightness > 0.5)
            {
                lightness = Clamp(1f - (float)Math.Pow(1 - lightness, factor), 0, 1);
            }
            return FromHSL(hue, saturation, lightness).Convert();
        }

        private static float Extreme(float value, float factor)
        {
            if (value < 0.5)
            {
                return Clamp((float)Math.Pow(value, factor), 0, 1);
            }

            if (value > 0.5)
            {
                return Clamp(1f - (float)Math.Pow(1 - value, factor), 0, 1);
            }

            return value;
        }

        private static float Clamp(float value, float min, float max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        public static System.Drawing.Color Convert(this System.Windows.Media.Color color)
        {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public static System.Windows.Media.Color Convert(this System.Drawing.Color color)
        {
            return System.Windows.Media.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        public static float GetSaturation(this System.Windows.Media.Color color)
        {
            return Convert(color).GetSaturation();
        }

        public static float GetHue(this System.Windows.Media.Color color)
        {
            return Convert(color).GetHue();
        }

        public static float GetLightness(this System.Windows.Media.Color color)
        {
            return Convert(color).GetBrightness();
        }

        public static void GetHSL(this System.Windows.Media.Color color, out float hue, out float saturation, out float lightness)
        {
            hue = color.GetHue();
            saturation = color.GetSaturation();
            lightness = color.GetLightness();
        }

        public static void GetHSL(this System.Drawing.Color color, out float hue, out float saturation, out float lightness)
        {
            color.Convert().GetHSL(out hue, out saturation, out lightness);
        }
        public static System.Drawing.Color GetOposite(this System.Drawing.Color color)
        {
            color.GetHSL(out float hue, out float saturation, out float lightness);
            hue = (hue + 180) % 360;

            Debug.Assert(lightness != 0.5);
            float newLightness = 1 - lightness;
            while (Math.Abs(newLightness - lightness) < 0.4)
            {
                newLightness = Extreme(newLightness, 1.5f);
            }

            float newSaturation = 1 - saturation;
            while (Math.Abs(newSaturation - saturation) < 0.4)
            {
                newSaturation = Extreme(newSaturation, 1.5f);
            }

            Debug.WriteLine("Opposite of " + color.ToHexString() + " is " + FromHSL(hue, newSaturation, newLightness).ToHexString());
            return FromHSL(hue, newSaturation, newLightness);
        }

        public static string ToHexString(this System.Drawing.Color color)
        {
            return "#" + color.R.ToString("x2") + color.G.ToString("x2") + color.B.ToString("x2");
        }

        public static System.Windows.Media.Color GetOposite(this System.Windows.Media.Color color)
        {
            return color.Convert().GetOposite().Convert();
        }

        public static System.Drawing.Color FromHSL(float hue, float saturation, float lightness)
        {
            float c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            float subX = ((hue / 60) % 2) - 1;
            float x = c * (1 - Math.Abs(subX));
            float m = lightness - (c / 2);

            float tempR, tempG, tempB;
            Debug.Assert(0 <= hue);
            Debug.Assert(hue < 360);
            if (hue < 60)
            {
                tempR = c;
                tempG = x;
                tempB = 0;
            }
            else if (hue < 120)
            {
                tempR = x;
                tempG = c;
                tempB = 0;
            }
            else if (hue < 180)
            {
                tempR = 0;
                tempG = c;
                tempB = x;
            }
            else if (hue < 240)
            {
                tempR = 0;
                tempG = x;
                tempB = c;
            }
            else if (hue < 300)
            {
                tempR = x;
                tempG = 0;
                tempB = c;
            }
            else
            {
                Debug.Assert(hue < 360);
                tempR = c;
                tempG = 0;
                tempB = x;
            }

            byte r = (byte)Math.Round((tempR + m) * 255);
            byte g = (byte)Math.Round((tempG + m) * 255);
            byte b = (byte)Math.Round((tempB + m) * 255);

            return System.Drawing.Color.FromArgb(r, g, b);
        }
    }
}
