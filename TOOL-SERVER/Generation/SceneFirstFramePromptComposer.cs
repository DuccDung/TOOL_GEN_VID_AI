using System.Text;

namespace TOOL_SERVER.Generation;

internal sealed record SceneFirstFrameCharacterPrompt(
    string Name,
    string? Role,
    string? VisualIdentity,
    string ProfileJson,
    string? WardrobeJson,
    string? ForbiddenChangesJson);

internal sealed record SceneFirstFrameAssetPrompt(
    string AssetType,
    string Name,
    string CanonicalDescription);

internal sealed record SceneFirstFramePromptInput(
    string AspectRatio,
    string VisualDescription,
    string? CameraDirection,
    string? Lighting,
    string? Motion,
    string? Emotion,
    SceneFirstFrameCharacterPrompt? Character,
    IReadOnlyList<SceneFirstFrameAssetPrompt> Assets,
    string? Narration = null,
    string? Dialogue = null,
    string? ApprovedScenePrompt = null,
    string? NegativePrompt = null);

internal static class SceneFirstFramePromptComposer
{
    public const string TemplateVersion = "scene-first-frame-v1";

    public static string Compose(SceneFirstFramePromptInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.AspectRatio is not ("16:9" or "9:16") ||
            string.IsNullOrWhiteSpace(input.VisualDescription))
        {
            throw new ArgumentException("Dữ liệu dựng prompt first-frame không hợp lệ.", nameof(input));
        }

        var prompt = new StringBuilder(2048)
            .AppendLine("Tạo đúng một ảnh điện ảnh làm khung hình đầu tiên cho một cảnh video Veo.")
            .Append("Tỷ lệ khung hình bắt buộc: ").Append(input.AspectRatio).AppendLine("; giữ chủ thể trong vùng bố cục an toàn, không cắt mặt hoặc vật thể chính.")
            .Append("Mô tả cảnh: ").AppendLine(input.VisualDescription.Trim());

        AppendOptional(prompt, "Máy quay", input.CameraDirection);
        AppendOptional(prompt, "Ánh sáng", input.Lighting);
        AppendOptional(prompt, "Chuyển động dự kiến sau khung hình đầu", input.Motion);
        AppendOptional(prompt, "Cảm xúc", input.Emotion);

        AppendOptional(prompt, "Prompt cảnh đã duyệt", input.ApprovedScenePrompt);
        AppendOptional(prompt, "Ràng buộc loại trừ của cảnh", input.NegativePrompt);
        AppendOptional(prompt, "Narration của cảnh", input.Narration);
        AppendOptional(prompt, "Dialogue của cảnh", input.Dialogue);

        if (input.Character is { } character)
        {
            prompt.AppendLine("Đây là cảnh on-camera có đúng một nhân vật. Dùng ảnh đầu vào làm nguồn nhận diện duy nhất; bảo toàn tuyệt đối khuôn mặt, độ tuổi, tóc, vóc dáng và trang phục.")
                .Append("Nhân vật: ").Append(character.Name.Trim());
            AppendInline(prompt, character.Role);
            prompt.AppendLine();
            AppendOptional(prompt, "Nhận diện bất biến", character.VisualIdentity);
            AppendOptional(prompt, "Hồ sơ hình thể", character.ProfileJson);
            AppendOptional(prompt, "Trang phục và phụ kiện", character.WardrobeJson);
            AppendOptional(prompt, "Thay đổi bị cấm", character.ForbiddenChangesJson);
            prompt.AppendLine("Giữ rõ toàn bộ mặt, môi và hàm; không che khuất; không thêm bất kỳ người nào khác.");
        }
        else
        {
            prompt.AppendLine("Đây là cảnh B-roll không có nhân vật. Không thêm người, khuôn mặt, bóng người hoặc nhân vật nền.");
        }

        if (input.Assets.Count > 0)
        {
            prompt.AppendLine("Các tài sản bối cảnh đã khóa, phải thể hiện nhất quán:");
            foreach (var asset in input.Assets)
            {
                prompt.Append("- ").Append(asset.AssetType).Append(": ")
                    .Append(asset.Name.Trim()).Append(" — ")
                    .AppendLine(asset.CanonicalDescription.Trim());
            }
        }

        prompt.AppendLine("Bố cục là một khung hình liên tục, tự nhiên và có chiều sâu; đặt chủ thể chính trong vùng an toàn cho chuyển động kế tiếp.")
            .Append("Cấm tuyệt đối: subtitle, caption, chữ, logo, watermark, giao diện, viền, split panel, collage, người thừa và vật thể thừa.");

        var result = prompt.ToString().Trim();
        var modeGuard = input.Character is null
            ? "This is B-roll: do not add any person, face, silhouette, or background character."
            : "This is a single-character shot: use the supplied image as the only identity source, preserve identity and wardrobe, and add no other person.";
        var mandatorySuffix = $"\n\n{modeGuard} Never add text, subtitles, captions, logos, watermarks, UI, borders, split panels, or collages.";
        var maximumBodyLength = 8_000 - mandatorySuffix.Length;
        if (result.Length > maximumBodyLength)
        {
            var length = maximumBodyLength;
            if (length > 0 && char.IsHighSurrogate(result[length - 1]))
            {
                length--;
            }
            result = result[..length].TrimEnd();
        }
        return result + mandatorySuffix;
    }

    private static void AppendOptional(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(label).Append(": ").AppendLine(value.Trim());
        }
    }

    private static void AppendInline(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.Append(" — ").Append(value.Trim());
        }
    }
}
