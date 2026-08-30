using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TOOL_SERVER.Generation;

internal sealed record ValidatedGeneratedImage(
    byte[] Bytes,
    string MimeType,
    string Sha256,
    int Width,
    int Height);

internal static class GeneratedImageValidator
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static ValidatedGeneratedImage ValidatePng(byte[] bytes, int maximumBytes)
    {
        if (bytes.Length is <= 0 || bytes.Length > maximumBytes)
        {
            throw Invalid("openai_image_size_invalid", "Ảnh OpenAI trả về vượt quá giới hạn dung lượng cho phép.");
        }

        var (mimeType, width, height) = ReadImageInfo(bytes);
        if (mimeType != "image/png")
        {
            throw Invalid("openai_image_unexpected_format", "OpenAI không trả về ảnh PNG như cấu hình yêu cầu.");
        }
        if (width != 1024 || height != 1024)
        {
            throw Invalid("openai_image_dimensions_invalid", "OpenAI không trả về ảnh đúng kích thước 1024x1024.");
        }

        return new ValidatedGeneratedImage(
            bytes,
            mimeType,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            width,
            height);
    }

    internal static (string MimeType, int Width, int Height) ReadImageInfo(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 24 && bytes[..8].SequenceEqual(PngSignature))
        {
            var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
            if (width <= 0 || height <= 0)
            {
                throw Invalid("openai_image_dimensions_invalid", "Ảnh OpenAI trả về có kích thước không hợp lệ.");
            }
            return ("image/png", width, height);
        }

        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
        {
            return ReadJpegInfo(bytes);
        }

        throw Invalid("openai_image_signature_invalid", "OpenAI trả về dữ liệu không có chữ ký PNG/JPEG hợp lệ.");
    }

    private static (string MimeType, int Width, int Height) ReadJpegInfo(ReadOnlySpan<byte> bytes)
    {
        var offset = 2;
        while (offset + 4 <= bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            while (offset < bytes.Length && bytes[offset] == 0xFF)
            {
                offset++;
            }
            if (offset >= bytes.Length)
            {
                break;
            }

            var marker = bytes[offset++];
            if (marker is 0xD8 or 0xD9)
            {
                continue;
            }
            if (offset + 2 > bytes.Length)
            {
                break;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
            {
                break;
            }
            if (IsStartOfFrame(marker) && segmentLength >= 7)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 3, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2));
                if (width > 0 && height > 0)
                {
                    return ("image/jpeg", width, height);
                }
            }
            offset += segmentLength;
        }

        throw Invalid("openai_image_dimensions_invalid", "Không đọc được kích thước ảnh JPEG do OpenAI trả về.");
    }

    private static bool IsStartOfFrame(byte marker) => marker is
        0xC0 or 0xC1 or 0xC2 or 0xC3 or 0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;

    private static ProviderHttpException Invalid(string code, string message) =>
        new(ProviderCodes.OpenAi, code, message);
}
