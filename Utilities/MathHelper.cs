using System;

namespace Utilities
{
    public static class MathHelper
    {
        public static double Clamp(double value, double min, double max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        public static float Clamp(float value, float min, float max)
        {
            return Math.Min(Math.Max(value, min), max);
        }

        public static int Clamp(int value, int min, int max)
        {
            return Math.Min(Math.Max(value, min), max);
        }
    }
}
