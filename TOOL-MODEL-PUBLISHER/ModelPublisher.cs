using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TOOL_SHARED.Contracts.ModelDistribution;
using TOOL_SHARED.Distribution.ModelDistribution;

namespace TOOL_MODEL_PUBLISHER;

public sealed record ModelPublishRequest(string HandoverRoot, string OutputRoot, string RepositoryId, string Channel,
    long Sequence, string DesktopVersion, int DesktopBuildNumber, string KeyId, string PrivateKeyPath,
    string InventorySha256, int ValidDays = 7, string? QualificationFile = null);

public sealed record ModelPublishResult(string CandidateId, string CatalogEnvelopeSha256, long ArtifactBytes,
    bool RuntimeQualified, bool DistributionApproved, bool Published);

public sealed class ModelPublishException(string code) : Exception("Không thể chuẩn bị kho model; hãy kiểm tra đầu vào theo hướng dẫn.")
{
    public string Code { get; } = code;
}

/// <summary>Offline operator tool. It has no HTTP, database, deployment, or key-generation capabilities.</summary>
public static partial class ModelPublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static async Task<ModelPublishResult> StageAsync(ModelPublishRequest request, CancellationToken token = default)
    {
        ValidateRequest(request);
        var handover = Path.GetFullPath(request.HandoverRoot);
        var output = Path.GetFullPath(request.OutputRoot);
        var inventoryPath = Path.Combine(handover, "inventory.json");
        var inventoryBytes = await ReadSmallFileAsync(inventoryPath, 16 * 1024 * 1024, token);
        if (!Matches(SHA256.HashData(inventoryBytes), request.InventorySha256)) throw Failure("inventory_hash_mismatch");
        using var inventory = JsonDocument.Parse(inventoryBytes);
        var root = inventory.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("state").GetString() != "ArtifactVerified")
            throw Failure("handover_not_verified");
        var candidateId = root.GetProperty("candidateId").GetString();
        if (!Identifier(candidateId)) throw Failure("candidate_invalid");
        var entries = root.GetProperty("files").EnumerateArray().ToArray();
        foreach (var artifact in ApprovedModelArtifactPins.All)
        {
            var matches = entries.Where(entry => entry.GetProperty("artifactId").GetString() == artifact.ArtifactId).ToArray();
            if (matches.Length != 1 || matches[0].GetProperty("path").GetString() != artifact.RelativePath ||
                matches[0].GetProperty("sizeBytes").GetInt64() != artifact.SizeBytes ||
                matches[0].GetProperty("sha256").GetString() != artifact.Sha256 ||
                matches[0].GetProperty("componentId").GetString() != artifact.ComponentId ||
                matches[0].GetProperty("componentVersion").GetString() != artifact.ComponentVersion)
                throw Failure("inventory_artifact_mismatch");
        }

        var compatibilityEntries = entries.Where(entry => entry.GetProperty("path").GetString() == "compatibility.json").ToArray();
        if (compatibilityEntries.Length != 1) throw Failure("compatibility_missing");
        var compatibilityBytes = await ReadSmallFileAsync(Path.Combine(handover, "compatibility.json"), 1024 * 1024, token);
        if (compatibilityEntries[0].GetProperty("sizeBytes").GetInt64() != compatibilityBytes.LongLength ||
            !Hash(compatibilityEntries[0].GetProperty("sha256").GetString()) ||
            !Matches(SHA256.HashData(compatibilityBytes), compatibilityEntries[0].GetProperty("sha256").GetString()!))
            throw Failure("compatibility_hash_mismatch");
        using var compatibility = JsonDocument.Parse(compatibilityBytes);
        var desktop = compatibility.RootElement.GetProperty("desktop");
        if (compatibility.RootElement.GetProperty("candidateId").GetString() != candidateId ||
            desktop.GetProperty("version").GetString() != request.DesktopVersion ||
            desktop.GetProperty("buildNumber").GetInt64() != request.DesktopBuildNumber ||
            desktop.GetProperty("platform").GetString() != "win-x64")
            throw Failure("desktop_compatibility_mismatch");

        var qualification = await ReadQualificationAsync(request, candidateId!, token);
        using var key = LoadExternalPrivateKey(request.PrivateKeyPath, handover, output);
        var publicKey = key.ExportSubjectPublicKeyInfoPem();
        // Recheck immediately before creating output. The tool never overwrites a prior candidate.
        if (Directory.Exists(output) || File.Exists(output)) throw Failure("output_exists");
        RejectLinks(output, allowMissing: true);
        Directory.CreateDirectory(output);
        await WriteNewAsync(Path.Combine(output, ".incomplete"), Encoding.UTF8.GetBytes("Not published. Offline staging in progress."), token);

        foreach (var artifact in ApprovedModelArtifactPins.All)
        {
            var source = ResolveUnder(handover, artifact.RelativePath);
            var destination = ResolveUnder(output, artifact.RelativePath, allowMissing: true);
            await CopyVerifiedAsync(source, destination, artifact.SizeBytes, artifact.Sha256, token);
        }

        var records = Path.Combine(output, "operation-records", candidateId!);
        Directory.CreateDirectory(records);
        await WriteNewAsync(Path.Combine(records, "inventory.json"), inventoryBytes, token);
        await WriteNewAsync(Path.Combine(records, "compatibility.json"), compatibilityBytes, token);
        foreach (var entry in entries.Where(entry => entry.GetProperty("path").GetString() is { } path &&
                     (path.StartsWith("licenses/", StringComparison.Ordinal) || path.StartsWith("provenance/", StringComparison.Ordinal))))
        {
            var relative = entry.GetProperty("path").GetString()!;
            var size = entry.GetProperty("sizeBytes").GetInt64();
            var hash = entry.GetProperty("sha256").GetString();
            if (size is <= 0 or > 64 * 1024 * 1024 || !Hash(hash)) throw Failure("handover_evidence_invalid");
            await CopyVerifiedAsync(ResolveUnder(handover, relative), ResolveUnder(records, relative, allowMissing: true), size, hash!, token);
        }
        if (qualification is not null)
        {
            await WriteNewAsync(Path.Combine(records, "qualification.json"), qualification.ReportBytes, token);
            foreach (var evidence in qualification.Evidence)
                await CopyVerifiedAsync(evidence.Path, Path.Combine(records, "evidence", evidence.Kind + Path.GetExtension(evidence.Path)),
                    new FileInfo(evidence.Path).Length, evidence.Sha256, token);
        }

        var now = DateTimeOffset.UtcNow;
        var artifacts = ApprovedModelArtifactPins.All.Select(artifact => artifact with
        {
            MinimumDesktopVersion = request.DesktopVersion,
            MinimumDesktopBuildNumber = request.DesktopBuildNumber
        }).ToArray();
        var catalog = new ModelDistributionCatalog(1, request.RepositoryId, request.Channel, "win-x64", request.Sequence,
            now, now.AddDays(request.ValidDays), artifacts);
        var envelope = ModelCatalogSigner.CreateEnvelope(JsonSerializer.SerializeToUtf8Bytes(catalog, Json), request.KeyId, key);
        ModelCatalogVerifier.Verify(envelope, new(request.RepositoryId, request.Channel, "win-x64",
            new Dictionary<string, string> { [request.KeyId] = publicKey }), null, now);
        var envelopeHash = Convert.ToHexString(SHA256.HashData(envelope)).ToLowerInvariant();
        var publicKeys = new Dictionary<string, string> { [request.KeyId] = publicKey };
        await WriteNewAsync(Path.Combine(output, "public-trust-root.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, repositoryId = request.RepositoryId, publicKeys
        }, Json), token);
        await WriteNewAsync(Path.Combine(output, "server-options.example.json"), JsonSerializer.SerializeToUtf8Bytes(new
        {
            ModelDistribution = new
            {
                Enabled = false, StorageRoot = "<absolute-model-repository-root>", RepositoryId = request.RepositoryId,
                Platform = "win-x64", AllowedChannels = new[] { request.Channel }, PublicKeysPem = publicKeys,
                TrustedCatalogSha256ByChannel = new Dictionary<string, string> { [request.Channel] = envelopeHash },
                MinimumSequence = request.Sequence
            }
        }, new JsonSerializerOptions { WriteIndented = true }), token);
        var result = new ModelPublishResult(candidateId!, envelopeHash, artifacts.Sum(artifact => artifact.SizeBytes),
            qualification is not null, qualification is not null, Published: false);
        await WriteNewAsync(Path.Combine(records, "stage-result.json"), JsonSerializer.SerializeToUtf8Bytes(result, Json), token);
        // Publish metadata only after all payloads and evidence have been checked. No live server is modified.
        var catalogPath = ResolveUnder(output, $"metadata/{request.Channel}/catalog.json", allowMissing: true);
        await WriteNewAsync(catalogPath, envelope, token);
        File.Delete(Path.Combine(output, ".incomplete"));
        return result;
    }

    public static void ValidateRequest(ModelPublishRequest request)
    {
        if (!Identifier(request.RepositoryId) || !Identifier(request.KeyId) || request.Sequence <= 0 ||
            request.DesktopBuildNumber <= 0 || request.ValidDays is < 1 or > 30 || !Hash(request.InventorySha256) ||
            request.Channel is not ("Stable" or "Beta" or "Development" or "Staging") ||
            !System.Text.RegularExpressions.Regex.IsMatch(request.DesktopVersion ?? "",
                @"^\d{1,5}\.\d{1,5}\.\d{1,5}(?:\.\d{1,5})?(?:-[a-zA-Z0-9.-]{1,40})?$"))
            throw Failure("arguments_invalid");
        if (!Path.IsPathFullyQualified(request.HandoverRoot) || !Path.IsPathFullyQualified(request.OutputRoot) ||
            !Path.IsPathFullyQualified(request.PrivateKeyPath)) throw Failure("absolute_paths_required");
        var input = Path.GetFullPath(request.HandoverRoot);
        var output = Path.GetFullPath(request.OutputRoot);
        if (!Directory.Exists(input) || Directory.Exists(output) || File.Exists(output) ||
            Contains(input, output) || Contains(output, input)) throw Failure("output_exists_or_overlaps");
        RejectLinks(input, allowMissing: false);
        RejectLinks(output, allowMissing: true);
        ValidateKeyLocation(request.PrivateKeyPath, input, output);
        if (request.Channel is "Stable" or "Beta" && string.IsNullOrWhiteSpace(request.QualificationFile))
            throw Failure("production_qualification_required");
    }

    public static async Task ExportPublicKeyAsync(string privateKeyPath, string outputPath, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(outputPath) || File.Exists(outputPath) || Directory.Exists(outputPath))
            throw Failure("output_exists_or_invalid");
        using var key = LoadExternalPrivateKey(privateKeyPath, Path.GetFullPath(outputPath), Path.GetFullPath(outputPath));
        RejectLinks(Path.GetFullPath(outputPath), allowMissing: true);
        await WriteNewAsync(outputPath, Encoding.UTF8.GetBytes(key.ExportSubjectPublicKeyInfoPem()), token);
    }

    private static async Task<Qualification?> ReadQualificationAsync(ModelPublishRequest request, string candidateId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.QualificationFile)) return null;
        var path = request.QualificationFile;
        if (!Path.IsPathFullyQualified(path) || Contains(request.HandoverRoot, path) || Contains(request.OutputRoot, path) ||
            FindRepositoryRoots().Any(repo => Contains(repo, path))) throw Failure("qualification_must_be_external");
        var bytes = await ReadSmallFileAsync(path, 1024 * 1024, token);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 || root.GetProperty("candidateId").GetString() != candidateId ||
            root.GetProperty("inventorySha256").GetString() != request.InventorySha256 ||
            root.GetProperty("desktopVersion").GetString() != request.DesktopVersion ||
            root.GetProperty("desktopBuildNumber").GetInt32() != request.DesktopBuildNumber ||
            !root.GetProperty("runtimeQualified").GetBoolean() || !root.GetProperty("distributionApproved").GetBoolean())
            throw Failure("qualification_invalid");
        var evidence = new List<Evidence>();
        foreach (var entry in root.GetProperty("evidence").EnumerateArray())
        {
            var kind = entry.GetProperty("kind").GetString();
            var relative = entry.GetProperty("path").GetString();
            var hash = entry.GetProperty("sha256").GetString();
            if (kind is not ("QwenRuntime" or "PiperRuntime" or "OcrRuntime" or "LicenseReview") ||
                entry.GetProperty("status").GetString() != "Passed" || relative is null || !Hash(hash) || evidence.Any(item => item.Kind == kind))
                throw Failure("qualification_evidence_invalid");
            var evidencePath = ResolveUnder(Path.GetDirectoryName(path)!, relative);
            await using var stream = OpenRead(evidencePath);
            if (stream.Length is <= 0 or > 64 * 1024 * 1024 || !Matches(await SHA256.HashDataAsync(stream, token), hash!))
                throw Failure("qualification_evidence_hash_mismatch");
            evidence.Add(new(kind, evidencePath, hash!));
        }
        if (evidence.Count != 4) throw Failure("qualification_evidence_missing");
        return new(bytes, evidence);
    }

    private static ECDsa LoadExternalPrivateKey(string keyPath, string handover, string output)
    {
        ValidateKeyLocation(keyPath, handover, output);
        var info = new FileInfo(keyPath);
        if (!info.Exists || info.Length is <= 0 or > 16384) throw Failure("private_key_invalid");
        var key = ECDsa.Create();
        try
        {
            key.ImportFromPem(File.ReadAllText(keyPath));
            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7" ||
                key.ExportParameters(true).D is null) throw Failure("private_key_invalid");
            return key;
        }
        catch
        {
            key.Dispose();
            throw Failure("private_key_invalid");
        }
    }

    private static void ValidateKeyLocation(string keyPath, string handover, string output)
    {
        if (!Path.IsPathFullyQualified(keyPath)) throw Failure("private_key_must_be_external");
        var full = Path.GetFullPath(keyPath);
        if (Contains(handover, full) || Contains(output, full) || FindRepositoryRoots().Any(repo => Contains(repo, full)))
            throw Failure("private_key_must_be_external");
        RejectLinks(full, allowMissing: false);
    }

    private static IEnumerable<string> FindRepositoryRoots()
    {
        foreach (var startingPoint in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(startingPoint); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "TOOL_GEN_POST_VIDEO.slnx")))
                {
                    yield return directory.FullName;
                    break;
                }
    }

    private static async Task CopyVerifiedAsync(string source, string destination, long size, string hash, CancellationToken token)
    {
        await using var input = OpenRead(source);
        if (input.Length != size || !Matches(await SHA256.HashDataAsync(input, token), hash)) throw Failure("source_artifact_invalid");
        input.Position = 0;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                         128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(target, 128 * 1024, token);
            await target.FlushAsync(token);
        }
        await using var verify = OpenRead(destination);
        if (verify.Length != size || !Matches(await SHA256.HashDataAsync(verify, token), hash)) throw Failure("copied_artifact_invalid");
    }

    private static async Task<byte[]> ReadSmallFileAsync(string path, int maxBytes, CancellationToken token)
    {
        RejectLinks(Path.GetFullPath(path), allowMissing: false);
        await using var stream = OpenRead(path);
        if (stream.Length is <= 0 || stream.Length > maxBytes) throw Failure("metadata_size_invalid");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }

    private static async Task WriteNewAsync(string path, byte[] bytes, CancellationToken token)
    {
        RejectLinks(Path.GetFullPath(path), allowMissing: true);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static string ResolveUnder(string root, string relative, bool allowMissing = false)
    {
        var segments = relative.Split('/');
        if (segments.Length == 0 || segments.Any(segment => !Identifier(segment) || segment is "." or ".." || segment[^1] == '.'))
            throw Failure("path_invalid");
        var path = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!Contains(root, path)) throw Failure("path_invalid");
        RejectLinks(path, allowMissing);
        return path;
    }

    private static void RejectLinks(string path, bool allowMissing)
    {
        var root = Path.GetPathRoot(path) ?? throw Failure("path_invalid");
        var current = root;
        foreach (var segment in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Failure("reparse_point_rejected");
            }
            catch (FileNotFoundException) when (allowMissing) { break; }
            catch (DirectoryNotFoundException) when (allowMissing) { break; }
        }
    }

    private static bool Contains(string root, string path)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        return fullPath.Equals(fullRoot, PathComparison) || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static bool Identifier(string? text) => text is { Length: > 0 and <= 180 } && text is not "." and not ".." &&
        text[^1] != '.' && !ReservedName(text) && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool ReservedName(string text)
    {
        var stem = text.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
               stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
               (stem.Length == 4 && stem[3] is >= '1' and <= '9' && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                   stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
    }
    private static bool Hash(string? value) => value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool Matches(byte[] actual, string expected) => CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected));
    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
        128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
    private static ModelPublishException Failure(string code) => new(code);
    private sealed record Evidence(string Kind, string Path, string Sha256);
    private sealed record Qualification(byte[] ReportBytes, IReadOnlyList<Evidence> Evidence);
}

public static class ModelPublisherCommand
{
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken token = default)
    {
        if (args.Length == 0 || args[0] is "--help" or "help")
        {
            await output.WriteLineAsync("stage --handover <absolute> --output <new-absolute-root> --repository <id> --channel <Staging|Development|Beta|Stable> --sequence <n> --desktop-version <version> --desktop-build <n> --key-id <id> --private-key <external-absolute-pem> --inventory-sha256 <sha256> [--valid-days 7] [--qualification-file <external-report>]\nrevoke --catalog <absolute> --catalog-sha256 <envelope-pin> --trust-root <absolute-public-root> --output <new-absolute-root> --sequence <higher> --revoke-sha256 <comma-separated-hashes> --key-id <trusted-id> --private-key <external-absolute-pem> [--valid-days 7]\npublic-key --private-key <external-absolute-pem> --output <new-public-pem>");
            return 0;
        }
        try
        {
            var values = Parse(args);
            string Required(string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new ModelPublishException("arguments_invalid");
            if (args[0] == "public-key")
            {
                if (values.Keys.Any(key => key is not ("--private-key" or "--output"))) throw new ModelPublishException("arguments_invalid");
                await ModelPublisher.ExportPublicKeyAsync(Required("--private-key"), Required("--output"), token);
                await output.WriteLineAsync("Đã xuất khóa công khai. Không xuất hoặc tạo khóa bí mật.");
                return 0;
            }
            if (args[0] == "revoke")
            {
                if (values.Keys.Any(key => key is not ("--catalog" or "--catalog-sha256" or "--trust-root" or "--output" or "--sequence" or
                        "--revoke-sha256" or "--key-id" or "--private-key" or "--valid-days"))) throw new ModelPublishException("arguments_invalid");
                var revocation = new ModelRevokeRequest(Required("--catalog"), Required("--catalog-sha256"), Required("--trust-root"),
                    Required("--output"), long.Parse(Required("--sequence"), CultureInfo.InvariantCulture),
                    Required("--revoke-sha256").Split(','), Required("--key-id"), Required("--private-key"),
                    values.TryGetValue("--valid-days", out var validity) ? int.Parse(validity, CultureInfo.InvariantCulture) : 7);
                var revocationResult = await ModelPublisher.RevokeAsync(revocation, token);
                await output.WriteLineAsync(JsonSerializer.Serialize(revocationResult, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
                return 0;
            }
            if (args[0] != "stage" || values.Keys.Any(key => key is not ("--handover" or "--output" or "--repository" or "--channel" or
                    "--sequence" or "--desktop-version" or "--desktop-build" or "--key-id" or "--private-key" or "--inventory-sha256" or
                    "--valid-days" or "--qualification-file"))) throw new ModelPublishException("arguments_invalid");
            var request = new ModelPublishRequest(Required("--handover"), Required("--output"), Required("--repository"), Required("--channel"),
                long.Parse(Required("--sequence"), CultureInfo.InvariantCulture), Required("--desktop-version"),
                int.Parse(Required("--desktop-build"), CultureInfo.InvariantCulture), Required("--key-id"), Required("--private-key"),
                Required("--inventory-sha256"), values.TryGetValue("--valid-days", out var days) ? int.Parse(days, CultureInfo.InvariantCulture) : 7,
                values.GetValueOrDefault("--qualification-file"));
            var result = await ModelPublisher.StageAsync(request, token);
            await output.WriteLineAsync(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return 0;
        }
        catch (OperationCanceledException)
        {
            await error.WriteLineAsync("model_publish_cancelled");
            return 2;
        }
        catch (ModelPublishException exception)
        {
            await error.WriteLineAsync(exception.Code);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or CryptographicException or
                                             ArgumentException or FormatException or OverflowException or JsonException or
                                             InvalidOperationException or KeyNotFoundException)
        {
            // Never print key material, input paths, raw metadata, or exception messages.
            await error.WriteLineAsync("model_publish_failed");
            return 1;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length % 2 != 1) throw new ModelPublishException("arguments_invalid");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1]))
                throw new ModelPublishException("arguments_invalid");
        return values;
    }
}
