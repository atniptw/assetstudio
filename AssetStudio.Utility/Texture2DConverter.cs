using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using Texture2DDecoder;

namespace AssetStudio
{
    public class Texture2DConverter
    {
        private ResourceReader reader;
        private int m_Width;
        private int m_Height;
        private TextureFormat m_TextureFormat;
        private int[] version;
        private BuildTarget platform;
        private int outPutSize;

        public Texture2DConverter(Texture2D m_Texture2D)
        {
            reader = m_Texture2D.image_data;
            m_Width = m_Texture2D.m_Width;
            m_Height = m_Texture2D.m_Height;
            m_TextureFormat = m_Texture2D.m_TextureFormat;
            version = m_Texture2D.version;
            platform = m_Texture2D.platform;
            outPutSize = m_Width * m_Height * 4;
        }

        public bool DecodeTexture2D(byte[] bytes)
        {
            if (reader.Size == 0 || m_Width == 0 || m_Height == 0)
            {
                return false;
            }
            var flag = false;
            var rented = ArrayPool<byte>.Shared.Rent(reader.Size);
            var buff = rented;
            try
            {
                reader.GetData(buff);
                if (buff.Length != reader.Size)
                {
                    var exact = new byte[reader.Size];
                    Buffer.BlockCopy(buff, 0, exact, 0, reader.Size);
                    buff = exact;
                }
                switch (m_TextureFormat)
                {
                    case TextureFormat.Alpha8: //test pass
                        flag = DecodeAlpha8(buff, bytes);
                        break;
                    case TextureFormat.ARGB4444: //test pass
                        SwapBytesForXbox(buff);
                        flag = DecodeARGB4444(buff, bytes);
                        break;
                    case TextureFormat.RGB24: //test pass
                        flag = DecodeRGB24(buff, bytes);
                        break;
                    case TextureFormat.RGBA32: //test pass
                        flag = DecodeRGBA32(buff, bytes);
                        break;
                    case TextureFormat.ARGB32: //test pass
                        flag = DecodeARGB32(buff, bytes);
                        break;
                    case TextureFormat.RGB565: //test pass
                        SwapBytesForXbox(buff);
                        flag = DecodeRGB565(buff, bytes);
                        break;
                    case TextureFormat.R16: //test pass
                    case TextureFormat.R16_Alt: //test pass
                        flag = DecodeR16(buff, bytes);
                        break;
                    case TextureFormat.DXT1: //test pass
                        SwapBytesForXbox(buff);
                        flag = DecodeDXT1(buff, bytes);
                        break;
                    case TextureFormat.DXT3:
                        break;
                    case TextureFormat.DXT5: //test pass
                        SwapBytesForXbox(buff);
                        flag = DecodeDXT5(buff, bytes);
                        break;
                    case TextureFormat.RGBA4444: //test pass
                        flag = DecodeRGBA4444(buff, bytes);
                        break;
                    case TextureFormat.BGRA32: //test pass
                        flag = DecodeBGRA32(buff, bytes);
                        break;
                    case TextureFormat.RHalf:
                        flag = DecodeRHalf(buff, bytes);
                        break;
                    case TextureFormat.RGHalf:
                        flag = DecodeRGHalf(buff, bytes);
                        break;
                    case TextureFormat.RGBAHalf: //test pass
                        flag = DecodeRGBAHalf(buff, bytes);
                        break;
                    case TextureFormat.RFloat:
                        flag = DecodeRFloat(buff, bytes);
                        break;
                    case TextureFormat.RGFloat:
                        flag = DecodeRGFloat(buff, bytes);
                        break;
                    case TextureFormat.RGBAFloat:
                        flag = DecodeRGBAFloat(buff, bytes);
                        break;
                    case TextureFormat.YUY2: //test pass
                        flag = DecodeYUY2(buff, bytes);
                        break;
                    case TextureFormat.RGB9e5Float: //test pass
                        flag = DecodeRGB9e5Float(buff, bytes);
                        break;
                    case TextureFormat.BC6H: //test pass
                        flag = DecodeBC6H(buff, bytes);
                        break;
                    case TextureFormat.BC7: //test pass
                        flag = DecodeBC7(buff, bytes);
                        break;
                    case TextureFormat.BC4: //test pass
                        flag = DecodeBC4(buff, bytes);
                        break;
                    case TextureFormat.BC5: //test pass
                        flag = DecodeBC5(buff, bytes);
                        break;
                    case TextureFormat.DXT1Crunched: //test pass
                        flag = DecodeDXT1Crunched(buff, bytes);
                        break;
                    case TextureFormat.DXT5Crunched: //test pass
                        flag = DecodeDXT5Crunched(buff, bytes);
                        break;
                    case TextureFormat.PVRTC_RGB2: //test pass
                    case TextureFormat.PVRTC_RGBA2: //test pass
                        flag = DecodePVRTC(buff, bytes, true);
                        break;
                    case TextureFormat.PVRTC_RGB4: //test pass
                    case TextureFormat.PVRTC_RGBA4: //test pass
                        flag = DecodePVRTC(buff, bytes, false);
                        break;
                    case TextureFormat.ETC_RGB4: //test pass
                    case TextureFormat.ETC_RGB4_3DS:
                        flag = DecodeETC1(buff, bytes);
                        break;
                    case TextureFormat.ATC_RGB4: //test pass
                        flag = DecodeATCRGB4(buff, bytes);
                        break;
                    case TextureFormat.ATC_RGBA8: //test pass
                        flag = DecodeATCRGBA8(buff, bytes);
                        break;
                    case TextureFormat.EAC_R: //test pass
                        flag = DecodeEACR(buff, bytes);
                        break;
                    case TextureFormat.EAC_R_SIGNED:
                        flag = DecodeEACRSigned(buff, bytes);
                        break;
                    case TextureFormat.EAC_RG: //test pass
                        flag = DecodeEACRG(buff, bytes);
                        break;
                    case TextureFormat.EAC_RG_SIGNED:
                        flag = DecodeEACRGSigned(buff, bytes);
                        break;
                    case TextureFormat.ETC2_RGB: //test pass
                        flag = DecodeETC2(buff, bytes);
                        break;
                    case TextureFormat.ETC2_RGBA1: //test pass
                        flag = DecodeETC2A1(buff, bytes);
                        break;
                    case TextureFormat.ETC2_RGBA8: //test pass
                    case TextureFormat.ETC_RGBA8_3DS:
                        flag = DecodeETC2A8(buff, bytes);
                        break;
                    case TextureFormat.ASTC_RGB_4x4: //test pass
                    case TextureFormat.ASTC_RGBA_4x4: //test pass
                    case TextureFormat.ASTC_HDR_4x4: //test pass
                        flag = DecodeASTC(buff, bytes, 4);
                        break;
                    case TextureFormat.ASTC_RGB_5x5: //test pass
                    case TextureFormat.ASTC_RGBA_5x5: //test pass
                    case TextureFormat.ASTC_HDR_5x5: //test pass
                        flag = DecodeASTC(buff, bytes, 5);
                        break;
                    case TextureFormat.ASTC_RGB_6x6: //test pass
                    case TextureFormat.ASTC_RGBA_6x6: //test pass
                    case TextureFormat.ASTC_HDR_6x6: //test pass
                        flag = DecodeASTC(buff, bytes, 6);
                        break;
                    case TextureFormat.ASTC_RGB_8x8: //test pass
                    case TextureFormat.ASTC_RGBA_8x8: //test pass
                    case TextureFormat.ASTC_HDR_8x8: //test pass
                        flag = DecodeASTC(buff, bytes, 8);
                        break;
                    case TextureFormat.ASTC_RGB_10x10: //test pass
                    case TextureFormat.ASTC_RGBA_10x10: //test pass
                    case TextureFormat.ASTC_HDR_10x10: //test pass
                        flag = DecodeASTC(buff, bytes, 10);
                        break;
                    case TextureFormat.ASTC_RGB_12x12: //test pass
                    case TextureFormat.ASTC_RGBA_12x12: //test pass
                    case TextureFormat.ASTC_HDR_12x12: //test pass
                        flag = DecodeASTC(buff, bytes, 12);
                        break;
                    case TextureFormat.RG16: //test pass
                        flag = DecodeRG16(buff, bytes);
                        break;
                    case TextureFormat.R8: //test pass
                        flag = DecodeR8(buff, bytes);
                        break;
                    case TextureFormat.ETC_RGB4Crunched: //test pass
                        flag = DecodeETC1Crunched(buff, bytes);
                        break;
                    case TextureFormat.ETC2_RGBA8Crunched: //test pass
                        flag = DecodeETC2A8Crunched(buff, bytes);
                        break;
                    case TextureFormat.RG32: //test pass
                        flag = DecodeRG32(buff, bytes);
                        break;
                    case TextureFormat.RGB48: //test pass
                        flag = DecodeRGB48(buff, bytes);
                        break;
                    case TextureFormat.RGBA64: //test pass
                        flag = DecodeRGBA64(buff, bytes);
                        break;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented, true);
            }

            return flag;
        }

        private void SwapBytesForXbox(byte[] image_data)
        {
            if (platform == BuildTarget.XBOX360)
            {
                for (var i = 0; i < reader.Size / 2; i++)
                {
                    var b = image_data[i * 2];
                    image_data[i * 2] = image_data[i * 2 + 1];
                    image_data[i * 2 + 1] = b;
                }
            }
        }

        private bool DecodeAlpha8(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            var span = new Span<byte>(buff);
            span.Fill(0xFF);
            for (var i = 0; i < size; i++)
            {
                buff[i * 4 + 3] = image_data[i];
            }
            return true;
        }

        private bool DecodeARGB4444(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            var pixelNew = new byte[4];
            for (var i = 0; i < size; i++)
            {
                var pixelOldShort = BitConverter.ToUInt16(image_data, i * 2);
                pixelNew[0] = (byte)(pixelOldShort & 0x000f);
                pixelNew[1] = (byte)((pixelOldShort & 0x00f0) >> 4);
                pixelNew[2] = (byte)((pixelOldShort & 0x0f00) >> 8);
                pixelNew[3] = (byte)((pixelOldShort & 0xf000) >> 12);
                for (var j = 0; j < 4; j++)
                    pixelNew[j] = (byte)((pixelNew[j] << 4) | pixelNew[j]);
                pixelNew.CopyTo(buff, i * 4);
            }
            return true;
        }

        private bool DecodeRGB24(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = image_data[i * 3 + 2];
                buff[i * 4 + 1] = image_data[i * 3 + 1];
                buff[i * 4 + 2] = image_data[i * 3 + 0];
                buff[i * 4 + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGBA32(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = image_data[i + 2];
                buff[i + 1] = image_data[i + 1];
                buff[i + 2] = image_data[i + 0];
                buff[i + 3] = image_data[i + 3];
            }
            return true;
        }

        private bool DecodeARGB32(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = image_data[i + 3];
                buff[i + 1] = image_data[i + 2];
                buff[i + 2] = image_data[i + 1];
                buff[i + 3] = image_data[i + 0];
            }
            return true;
        }

        private bool DecodeRGB565(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                var p = BitConverter.ToUInt16(image_data, i * 2);
                buff[i * 4] = (byte)((p << 3) | (p >> 2 & 7));
                buff[i * 4 + 1] = (byte)((p >> 3 & 0xfc) | (p >> 9 & 3));
                buff[i * 4 + 2] = (byte)((p >> 8 & 0xf8) | (p >> 13));
                buff[i * 4 + 3] = 255;
            }
            return true;
        }

        private bool DecodeR16(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = 0; //b
                buff[i * 4 + 1] = 0; //g
                buff[i * 4 + 2] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 2)); //r
                buff[i * 4 + 3] = 255; //a
            }
            return true;
        }

        private bool DecodeDXT1(byte[] image_data, byte[] buff)
        {
            return TryDecodeDxtWithFallback(image_data, buff, isDxt1: true);
        }

        private bool DecodeDXT5(byte[] image_data, byte[] buff)
        {
            return TryDecodeDxtWithFallback(image_data, buff, isDxt1: false);
        }

        private bool TryDecodeDxtWithFallback(byte[] imageData, byte[] outBuffer, bool isDxt1)
        {
            if (imageData == null || imageData.Length == 0)
                return false;

            if (TryDecodeDxt(imageData, outBuffer, isDxt1))
                return true;

            if (TryDecodeDxtByteSwapped(imageData, outBuffer, isDxt1))
                return true;

            var blockBytes = isDxt1 ? 8 : 16;
            var expectedBaseMip = GetBlockCompressedBaseMipSize(m_Width, m_Height, blockBytes);
            if (expectedBaseMip <= 0 || imageData.Length < expectedBaseMip)
                return false;

            if (isDxt1 && TryDecodeDxt1Manual(imageData, 0, expectedBaseMip, outBuffer))
                return true;

            if (TryDecodeDxtSlice(imageData, 0, expectedBaseMip, outBuffer, isDxt1))
                return true;

            var maxOffset = Math.Min(256, imageData.Length - expectedBaseMip);
            for (var offset = blockBytes; offset <= maxOffset; offset += blockBytes)
            {
                if (isDxt1 && TryDecodeDxt1Manual(imageData, offset, expectedBaseMip, outBuffer))
                    return true;

                if (TryDecodeDxtSlice(imageData, offset, expectedBaseMip, outBuffer, isDxt1))
                    return true;
            }

            return false;
        }

        private bool TryDecodeDxt(byte[] source, byte[] outBuffer, bool isDxt1)
        {
            try
            {
                return isDxt1
                    ? TextureDecoder.DecodeDXT1(source, m_Width, m_Height, outBuffer)
                    : TextureDecoder.DecodeDXT5(source, m_Width, m_Height, outBuffer);
            }
            catch
            {
                return false;
            }
        }

        private bool TryDecodeDxtSlice(byte[] source, int offset, int length, byte[] outBuffer, bool isDxt1)
        {
            var slice = new byte[length];
            Buffer.BlockCopy(source, offset, slice, 0, length);
            return TryDecodeDxt(slice, outBuffer, isDxt1)
                || TryDecodeDxtByteSwapped(slice, outBuffer, isDxt1);
        }

        private bool TryDecodeDxtByteSwapped(byte[] source, byte[] outBuffer, bool isDxt1)
        {
            if (source.Length < 2)
                return false;

            var swapped = new byte[source.Length];
            Buffer.BlockCopy(source, 0, swapped, 0, source.Length);
            for (var i = 0; i + 1 < swapped.Length; i += 2)
            {
                var temp = swapped[i];
                swapped[i] = swapped[i + 1];
                swapped[i + 1] = temp;
            }

            return TryDecodeDxt(swapped, outBuffer, isDxt1);
        }

        private bool TryDecodeDxt1Manual(byte[] source, int offset, int length, byte[] outBuffer)
        {
            if (source == null || outBuffer == null || offset < 0 || length <= 0)
                return false;
            if (offset + length > source.Length)
                return false;

            var blockWidth = Math.Max(1, (m_Width + 3) / 4);
            var blockHeight = Math.Max(1, (m_Height + 3) / 4);
            var required = blockWidth * blockHeight * 8;
            if (length < required)
                return false;

            var src = offset;
            for (var blockY = 0; blockY < blockHeight; blockY++)
            {
                for (var blockX = 0; blockX < blockWidth; blockX++)
                {
                    var color0 = (ushort)(source[src] | (source[src + 1] << 8));
                    var color1 = (ushort)(source[src + 2] | (source[src + 3] << 8));
                    var indices = (uint)(source[src + 4]
                        | (source[src + 5] << 8)
                        | (source[src + 6] << 16)
                        | (source[src + 7] << 24));
                    src += 8;

                    var palette = new (byte R, byte G, byte B, byte A)[4];
                    DecodeRgb565(color0, out palette[0]);
                    DecodeRgb565(color1, out palette[1]);

                    if (color0 > color1)
                    {
                        palette[2] = (
                            (byte)((2 * palette[0].R + palette[1].R) / 3),
                            (byte)((2 * palette[0].G + palette[1].G) / 3),
                            (byte)((2 * palette[0].B + palette[1].B) / 3),
                            255);
                        palette[3] = (
                            (byte)((palette[0].R + 2 * palette[1].R) / 3),
                            (byte)((palette[0].G + 2 * palette[1].G) / 3),
                            (byte)((palette[0].B + 2 * palette[1].B) / 3),
                            255);
                    }
                    else
                    {
                        palette[2] = (
                            (byte)((palette[0].R + palette[1].R) / 2),
                            (byte)((palette[0].G + palette[1].G) / 2),
                            (byte)((palette[0].B + palette[1].B) / 2),
                            255);
                        palette[3] = (0, 0, 0, 0);
                    }

                    for (var py = 0; py < 4; py++)
                    {
                        var y = blockY * 4 + py;
                        if (y >= m_Height)
                            continue;

                        for (var px = 0; px < 4; px++)
                        {
                            var x = blockX * 4 + px;
                            if (x >= m_Width)
                                continue;

                            var paletteIndex = (int)((indices >> (2 * (py * 4 + px))) & 0x3);
                            var color = palette[paletteIndex];
                            var dst = (y * m_Width + x) * 4;
                            outBuffer[dst] = color.B;
                            outBuffer[dst + 1] = color.G;
                            outBuffer[dst + 2] = color.R;
                            outBuffer[dst + 3] = color.A;
                        }
                    }
                }
            }

            return true;
        }

        private static void DecodeRgb565(ushort value, out (byte R, byte G, byte B, byte A) color)
        {
            var r = (byte)((value >> 11) & 0x1F);
            var g = (byte)((value >> 5) & 0x3F);
            var b = (byte)(value & 0x1F);

            color = (
                (byte)((r << 3) | (r >> 2)),
                (byte)((g << 2) | (g >> 4)),
                (byte)((b << 3) | (b >> 2)),
                255);
        }

        private static int GetBlockCompressedBaseMipSize(int width, int height, int blockBytes)
        {
            var blockWidth = Math.Max(1, (width + 3) / 4);
            var blockHeight = Math.Max(1, (height + 3) / 4);
            return blockWidth * blockHeight * blockBytes;
        }

        private bool DecodeRGBA4444(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            var pixelNew = new byte[4];
            for (var i = 0; i < size; i++)
            {
                var pixelOldShort = BitConverter.ToUInt16(image_data, i * 2);
                pixelNew[0] = (byte)((pixelOldShort & 0x00f0) >> 4);
                pixelNew[1] = (byte)((pixelOldShort & 0x0f00) >> 8);
                pixelNew[2] = (byte)((pixelOldShort & 0xf000) >> 12);
                pixelNew[3] = (byte)(pixelOldShort & 0x000f);
                for (var j = 0; j < 4; j++)
                    pixelNew[j] = (byte)((pixelNew[j] << 4) | pixelNew[j]);
                pixelNew.CopyTo(buff, i * 4);
            }
            return true;
        }

        private bool DecodeBGRA32(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = image_data[i];
                buff[i + 1] = image_data[i + 1];
                buff[i + 2] = image_data[i + 2];
                buff[i + 3] = image_data[i + 3];
            }
            return true;
        }

        private bool DecodeRHalf(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = 0;
                buff[i + 2] = (byte)Math.Round(Half.ToHalf(image_data, i / 2) * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGHalf(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = (byte)Math.Round(Half.ToHalf(image_data, i + 2) * 255f);
                buff[i + 2] = (byte)Math.Round(Half.ToHalf(image_data, i) * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGBAHalf(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = (byte)Math.Round(Half.ToHalf(image_data, i * 2 + 4) * 255f);
                buff[i + 1] = (byte)Math.Round(Half.ToHalf(image_data, i * 2 + 2) * 255f);
                buff[i + 2] = (byte)Math.Round(Half.ToHalf(image_data, i * 2) * 255f);
                buff[i + 3] = (byte)Math.Round(Half.ToHalf(image_data, i * 2 + 6) * 255f);
            }
            return true;
        }

        private bool DecodeRFloat(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = 0;
                buff[i + 2] = (byte)Math.Round(BitConverter.ToSingle(image_data, i) * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGFloat(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = 0;
                buff[i + 1] = (byte)Math.Round(BitConverter.ToSingle(image_data, i * 2 + 4) * 255f);
                buff[i + 2] = (byte)Math.Round(BitConverter.ToSingle(image_data, i * 2) * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeRGBAFloat(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = (byte)Math.Round(BitConverter.ToSingle(image_data, i * 4 + 8) * 255f);
                buff[i + 1] = (byte)Math.Round(BitConverter.ToSingle(image_data, i * 4 + 4) * 255f);
                buff[i + 2] = (byte)Math.Round(BitConverter.ToSingle(image_data, i * 4) * 255f);
                buff[i + 3] = (byte)Math.Round(BitConverter.ToSingle(image_data, i * 4 + 12) * 255f);
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ClampByte(int x)
        {
            return (byte)(byte.MaxValue < x ? byte.MaxValue : (x > byte.MinValue ? x : byte.MinValue));
        }

        private bool DecodeYUY2(byte[] image_data, byte[] buff)
        {
            int p = 0;
            int o = 0;
            int halfWidth = m_Width / 2;
            for (int j = 0; j < m_Height; j++)
            {
                for (int i = 0; i < halfWidth; ++i)
                {
                    int y0 = image_data[p++];
                    int u0 = image_data[p++];
                    int y1 = image_data[p++];
                    int v0 = image_data[p++];
                    int c = y0 - 16;
                    int d = u0 - 128;
                    int e = v0 - 128;
                    buff[o++] = ClampByte((298 * c + 516 * d + 128) >> 8);            // b
                    buff[o++] = ClampByte((298 * c - 100 * d - 208 * e + 128) >> 8);  // g
                    buff[o++] = ClampByte((298 * c + 409 * e + 128) >> 8);            // r
                    buff[o++] = 255;
                    c = y1 - 16;
                    buff[o++] = ClampByte((298 * c + 516 * d + 128) >> 8);            // b
                    buff[o++] = ClampByte((298 * c - 100 * d - 208 * e + 128) >> 8);  // g
                    buff[o++] = ClampByte((298 * c + 409 * e + 128) >> 8);            // r
                    buff[o++] = 255;
                }
            }
            return true;
        }

        private bool DecodeRGB9e5Float(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                var n = BitConverter.ToInt32(image_data, i);
                var scale = n >> 27 & 0x1f;
                var scalef = Math.Pow(2, scale - 24);
                var b = n >> 18 & 0x1ff;
                var g = n >> 9 & 0x1ff;
                var r = n & 0x1ff;
                buff[i] = (byte)Math.Round(b * scalef * 255f);
                buff[i + 1] = (byte)Math.Round(g * scalef * 255f);
                buff[i + 2] = (byte)Math.Round(r * scalef * 255f);
                buff[i + 3] = 255;
            }
            return true;
        }

        private bool DecodeBC4(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeBC4(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeBC5(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeBC5(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeBC6H(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeBC6(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeBC7(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeBC7(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeDXT1Crunched(byte[] image_data, byte[] buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                if (DecodeDXT1(result, buff))
                {
                    return true;
                }
            }
            return false;
        }

        private bool DecodeDXT5Crunched(byte[] image_data, byte[] buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                if (DecodeDXT5(result, buff))
                {
                    return true;
                }
            }
            return false;
        }

        private bool DecodePVRTC(byte[] image_data, byte[] buff, bool is2bpp)
        {
            return TextureDecoder.DecodePVRTC(image_data, m_Width, m_Height, buff, is2bpp);
        }

        private bool DecodeETC1(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeETC1(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeATCRGB4(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeATCRGB4(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeATCRGBA8(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeATCRGBA8(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeEACR(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeEACR(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeEACRSigned(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeEACRSigned(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeEACRG(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeEACRG(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeEACRGSigned(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeEACRGSigned(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeETC2(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeETC2(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeETC2A1(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeETC2A1(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeETC2A8(byte[] image_data, byte[] buff)
        {
            return TextureDecoder.DecodeETC2A8(image_data, m_Width, m_Height, buff);
        }

        private bool DecodeASTC(byte[] image_data, byte[] buff, int blocksize)
        {
            return TextureDecoder.DecodeASTC(image_data, m_Width, m_Height, blocksize, blocksize, buff);
        }

        private bool DecodeRG16(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = 0; //B
                buff[i * 4 + 1] = image_data[i * 2 + 1];//G
                buff[i * 4 + 2] = image_data[i * 2];//R
                buff[i * 4 + 3] = 255;//A
            }
            return true;
        }

        private bool DecodeR8(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = 0; //B
                buff[i * 4 + 1] = 0; //G
                buff[i * 4 + 2] = image_data[i];//R
                buff[i * 4 + 3] = 255;//A
            }
            return true;
        }

        private bool DecodeETC1Crunched(byte[] image_data, byte[] buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                if (DecodeETC1(result, buff))
                {
                    return true;
                }
            }
            return false;
        }

        private bool DecodeETC2A8Crunched(byte[] image_data, byte[] buff)
        {
            if (UnpackCrunch(image_data, out var result))
            {
                if (DecodeETC2A8(result, buff))
                {
                    return true;
                }
            }
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static byte DownScaleFrom16BitTo8Bit(ushort component)
        {
            return (byte)(((component * 255) + 32895) >> 16);
        }

        private bool DecodeRG32(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = 0;                                                                          //b
                buff[i + 1] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i + 2));     //g
                buff[i + 2] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i));         //r
                buff[i + 3] = byte.MaxValue;                                                          //a
            }
            return true;
        }

        private bool DecodeRGB48(byte[] image_data, byte[] buff)
        {
            var size = m_Width * m_Height;
            for (var i = 0; i < size; i++)
            {
                buff[i * 4] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 6 + 4));     //b
                buff[i * 4 + 1] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 6 + 2)); //g
                buff[i * 4 + 2] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 6));     //r
                buff[i * 4 + 3] = byte.MaxValue;                                                          //a
            }
            return true;
        }

        private bool DecodeRGBA64(byte[] image_data, byte[] buff)
        {
            for (var i = 0; i < outPutSize; i += 4)
            {
                buff[i] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 2 + 4));     //b
                buff[i + 1] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 2 + 2)); //g
                buff[i + 2] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 2));     //r
                buff[i + 3] = DownScaleFrom16BitTo8Bit(BitConverter.ToUInt16(image_data, i * 2 + 6)); //a
            }
            return true;
        }

        private bool UnpackCrunch(byte[] image_data, out byte[] result)
        {
            if (version[0] > 2017 || (version[0] == 2017 && version[1] >= 3) //2017.3 and up
                || m_TextureFormat == TextureFormat.ETC_RGB4Crunched
                || m_TextureFormat == TextureFormat.ETC2_RGBA8Crunched)
            {
                result = TextureDecoder.UnpackUnityCrunch(image_data);
            }
            else
            {
                result = TextureDecoder.UnpackCrunch(image_data);
            }
            if (result != null)
            {
                return true;
            }
            return false;
        }
    }
}
