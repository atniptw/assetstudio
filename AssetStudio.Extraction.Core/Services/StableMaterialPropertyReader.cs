using System;
using System.Linq;

namespace AssetStudio.Extraction.Core.Services
{
    public static class StableMaterialPropertyReader
    {
        public static float[]? GetColor(Material? material, params string[] keys)
        {
            if (material?.m_SavedProperties?.m_Colors == null || keys == null || keys.Length == 0)
                return null;

            foreach (var key in keys)
            {
                var colorEntry = material.m_SavedProperties.m_Colors
                    .FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(colorEntry.Key))
                    continue;

                return new[]
                {
                    colorEntry.Value.R,
                    colorEntry.Value.G,
                    colorEntry.Value.B,
                    colorEntry.Value.A
                };
            }

            return null;
        }

        public static float GetFloat(Material? material, float fallback, params string[] keys)
        {
            if (material?.m_SavedProperties?.m_Floats == null || keys == null || keys.Length == 0)
                return fallback;

            foreach (var key in keys)
            {
                var floatEntry = material.m_SavedProperties.m_Floats
                    .FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(floatEntry.Key))
                    return floatEntry.Value;
            }

            return fallback;
        }

        public static bool TryGetFloat(Material? material, out float value, params string[] keys)
        {
            value = 0f;
            if (material?.m_SavedProperties?.m_Floats == null || keys == null || keys.Length == 0)
                return false;

            foreach (var key in keys)
            {
                var floatEntry = material.m_SavedProperties.m_Floats
                    .FirstOrDefault(pair => string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrWhiteSpace(floatEntry.Key))
                    continue;

                value = floatEntry.Value;
                return true;
            }

            return false;
        }
    }
}
