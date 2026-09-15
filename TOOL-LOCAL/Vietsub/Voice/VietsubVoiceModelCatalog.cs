namespace TOOL_LOCAL.Vietsub.Voice;

internal sealed record VietsubVoiceModelFile(string RelativePath, long Size, string Sha256);

internal sealed record VietsubVoiceModelDefinition(
    string VoiceId,
    string DisplayName,
    VietsubVoiceModelFile VoicePack);

internal static class VietsubVoiceModelCatalog
{
    // Model-card license metadata is not a substitute for a release review of the voicepack training data.
    public const string Repository = "contextboxai/Kokoro-Vietnamese";
    public const string Revision = "9f210d622209fcc216fe2ac6159fed2ff381cb8a";
    public const string ModelId = "kokoro-vietnamese-onnx";
    public const string License = "Apache-2.0 (upstream model card; voicepack rights require release review)";

    public static readonly VietsubVoiceModelFile CoreModel = new(
        "kokoro_vi.onnx", 325_731_953,
        "da191277f58633649a9c0d2ae8012e80ef57ea8e2a56e30323c0f7df1ca29087");

    public static readonly VietsubVoiceModelFile Config = new(
        "config.json", 2_351,
        "5abb01e2403b072bf03d04fde160443e209d7a0dad49a423be15196b9b43c17f");

    public static readonly IReadOnlyList<VietsubVoiceModelDefinition> Voices =
    [
        Voice("diem_trinh", "Diễm Trinh", 523_838, "19ac1ed49f46256a8d04e434541d4836818216fe07a3a00dc0db9f3adec01fa3"),
        Voice("duc_an", "Đức An", 523_746, "2618daa25e964ad903419cddbe9f510054e0e480b58413e026e888cbe00f076f"),
        Voice("duc_duy", "Đức Duy", 523_817, "25573ca0caaecb9389ce63c4e2396d499ca6333a74eb75a1adbd05e1645fcdb1"),
        Voice("hung_thinh", "Hưng Thịnh", 523_838, "875025a725e4ae5835add735c08b2b9729bcf414ea67f327f0343bb5dbf81f6d"),
        Voice("mai_linh", "Mai Linh", 523_824, "d8f12f7931618f884705486663951f486001e65aa4ee48633ab086995b57292d"),
        Voice("mai_loan", "Mai Loan", 523_824, "1c355dcea9c5018afda12e671710deb083b4e832f5a0ce04f96c54f474b65c11"),
        Voice("manh_dung", "Mạnh Dũng", 523_831, "f08d32366e78455171667112ec8f84d139f86233b5819e26fd68b35b1cf9f3f0"),
        Voice("my_yen", "Mỹ Yến", 523_746, "3edb2f25286b94de9053e123dc45278834925c60b5d2779dde32424a836fd1d7"),
        Voice("ngoc_huyen", "Ngọc Huyền", 523_838, "2ae069207dedfd62700957d84d9dec12268a0f115adca63129faf58a6196812a"),
        Voice("phat_tai", "Phát Tài", 523_824, "64c9e5dcb74d841b61f1f482494dadeef92cec0114fd2fa12aa174ffc6b8369a"),
        Voice("storyvert", "Storyvert", 523_831, "abb72c3ab157fc31131a338e14ab7b7df32a03f1e5bddd3ad7496423abfff746"),
        Voice("thanh_dat", "Thành Đạt", 523_831, "4d30926a819d95a19c7677feee5164314a96c8635ec6ff806398faf0395d6418"),
        Voice("thuc_trinh", "Thục Trinh", 523_838, "f46502d421b61c091a4f6e99aa9eb4e4e13e5e72e4082fdfab6630f401b63f14"),
        Voice("tuan_ngoc", "Tuấn Ngọc", 523_831, "febd1b6e1e159dfe08a5f40fbd317c93f323c7f1ed12792126e7522ddec4aea5")
    ];

    public static VietsubVoiceModelDefinition? Find(string voiceId) =>
        Voices.FirstOrDefault(voice => string.Equals(voice.VoiceId, voiceId, StringComparison.Ordinal));

    public static Uri DownloadUri(VietsubVoiceModelFile file) =>
        new($"https://huggingface.co/{Repository}/resolve/{Revision}/{file.RelativePath}");

    private static VietsubVoiceModelDefinition Voice(string id, string label, long size, string sha256) =>
        new($"kokoro-vi:{id}", label, new($"voicepacks/{id}.pt", size, sha256));
}
