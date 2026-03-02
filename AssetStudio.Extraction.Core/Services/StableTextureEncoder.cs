using AssetStudio.Extraction.Core.Models;
using System;

namespace AssetStudio.Extraction.Core.Services
{
    public static class StableTextureEncoder
    {
        public static bool TryEncodePngDataUrl(Texture2D? texture, out EncodedTextureData? encoded)
        {
            encoded = null;

            if (texture == null || texture.m_Width <= 0 || texture.m_Height <= 0)
                return false;

            try
            {
                using var stream = texture.ConvertToStream(ImageFormat.Png, true);
                if (stream == null || stream.Length <= 0)
                    return false;

                var bytes = stream.ToArray();
                encoded = new EncodedTextureData
                {
                    Name = texture.Name ?? "Texture",
                    Width = texture.m_Width,
                    Height = texture.m_Height,
                    Format = "image/png",
                    DataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}",
                    EncodedSize = bytes.Length
                };

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
