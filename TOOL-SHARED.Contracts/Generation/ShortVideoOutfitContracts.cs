namespace TOOL_SHARED.Contracts.Generation;

public static class ShortVideoModes
{
    public const string TextOnly = "TextOnly";
    public const string CharacterOutfit = "CharacterOutfit";
    public static bool IsSupported(string value) => value is TextOnly or CharacterOutfit;
}

public sealed record ShortVideoImageInfo(string Sha256, string MimeType, long SizeBytes, int Width, int Height);
public sealed record ShortVideoImageInput(ShortVideoImageInfo Info, string Base64Data);
public sealed record ShortVideoSettingsRequest(Guid ProjectId, Guid OrganizationId, int ExpectedRevision,
    ShortVideoImageInfo Character, ShortVideoImageInfo Outfit, string Background, string Motion);
public sealed record ShortVideoQuoteRequest(Guid ProjectId, Guid OrganizationId, int Revision, string Kind);
public sealed record ShortVideoQuote(Guid QuoteId, string Kind, decimal EstimatedCost, string CurrencyCode,
    string ModelCode, string Resolution, bool NativeAudio, DateTime ExpiresAtUtc, int Revision);
public sealed record ShortVideoComposeRequest(Guid ProjectId, Guid OrganizationId, Guid QuoteId,
    ShortVideoImageInput Character, ShortVideoImageInput Outfit);
public sealed record ShortVideoComposition(Guid CompositionId, string Status, string Sha256,
    string MimeType, long SizeBytes, int Width, int Height, string ContentUrl, int Revision);
public sealed record ShortVideoState(Guid ProjectId, int Revision, ShortVideoImageInfo? Character,
    ShortVideoImageInfo? Outfit, string Background, string Motion, ShortVideoComposition? Composition,
    bool Enabled, string? Message = null);
public sealed record ShortVideoApprovalRequest(Guid ProjectId, Guid OrganizationId, Guid CompositionId,
    int Revision, bool Approved);
public sealed record ShortVideoCompositionInput(Guid CompositionId, Guid QuoteId, int Revision,
    string MimeType, string Base64Data, string Sha256);
