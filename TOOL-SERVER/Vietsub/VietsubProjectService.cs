using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Organizations;
using TOOL_SERVER.Vietsub.Data;
using TOOL_SERVER.Vietsub.Domain;
using TOOL_SHARED.Contracts.Vietsub;

namespace TOOL_SERVER.Vietsub;

public sealed record VietsubProjectRequestContext(
    string UserId,
    Guid DeviceId,
    string? IpAddress,
    string? UserAgent,
    string CorrelationId);

public interface IVietsubProjectService
{
    Task<IReadOnlyList<VietsubProjectResponse>> ListAsync(
        Guid organizationId,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> GetAsync(
        Guid projectId,
        Guid organizationId,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> CreateAsync(
        CreateVietsubProjectRequest request,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> RenameAsync(
        Guid projectId,
        RenameVietsubProjectRequest request,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken);

    Task<VietsubProjectResponse> ArchiveAsync(
        Guid projectId,
        Guid organizationId,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken);
}

internal sealed class VietsubProjectService(
    VietsubDbContext db,
    IGenerationAccessService generationAccess,
    TimeProvider timeProvider) : IVietsubProjectService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> SourceLanguages =
        new HashSet<string>(["auto", "en", "zh"], StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<VietsubProjectResponse>> ListAsync(
        Guid organizationId,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(organizationId, context, requireWrite: false, cancellationToken);
        return await db.Projects
            .AsNoTracking()
            .Where(project => project.OrganizationId == organizationId
                              && project.CreatedByUserId == context.UserId
                              && !project.IsArchived)
            .OrderByDescending(project => project.UpdatedAtUtc)
            .ThenBy(project => project.Name)
            .Select(project => ToResponse(project))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<VietsubProjectResponse> GetAsync(
        Guid projectId,
        Guid organizationId,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(organizationId, context, requireWrite: false, cancellationToken);
        var project = await FindOwnedProjectAsync(projectId, organizationId, context.UserId, cancellationToken);
        return ToResponse(project);
    }

    public async Task<VietsubProjectResponse> CreateAsync(
        CreateVietsubProjectRequest request,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken)
    {
        if (request.ProjectId == Guid.Empty || request.OrganizationId == Guid.Empty)
        {
            throw new ArgumentException("Mã dự án hoặc tổ chức Vietsub không hợp lệ.");
        }

        var name = NormalizeName(request.Name);
        var sourceLanguageCode = NormalizeSourceLanguage(request.SourceLanguageCode);
        if (!string.Equals(request.TargetLanguageCode?.Trim(), "vi", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Phiên bản hiện tại chỉ hỗ trợ ngôn ngữ đích tiếng Việt.");
        }

        await RequireAccessAsync(request.OrganizationId, context, requireWrite: true, cancellationToken);
        var existing = await db.Projects.SingleOrDefaultAsync(
            project => project.ProjectId == request.ProjectId,
            cancellationToken);
        if (existing is not null)
        {
            if (existing.OrganizationId == request.OrganizationId
                && existing.CreatedByUserId == context.UserId
                && !existing.IsArchived
                && existing.Name == name
                && string.Equals(existing.SourceLanguageCode, sourceLanguageCode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.TargetLanguageCode, "vi", StringComparison.OrdinalIgnoreCase))
            {
                return ToResponse(existing);
            }

            throw new AccountApiException(
                StatusCodes.Status409Conflict,
                "vietsub_project_id_conflict",
                "Mã dự án Vietsub đã được sử dụng với dữ liệu khác.");
        }

        var now = UtcNow();
        var project = new VietsubProject
        {
            ProjectId = request.ProjectId,
            OrganizationId = request.OrganizationId,
            CreatedByUserId = context.UserId,
            Name = name,
            SourceLanguageCode = sourceLanguageCode,
            TargetLanguageCode = "vi",
            Status = VietsubProjectStatuses.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.Projects.Add(project);
        AddAudit(project.OrganizationId, context, "VietsubProjectCreated", new
        {
            project.ProjectId,
            project.Name,
            project.SourceLanguageCode,
            project.TargetLanguageCode
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(project);
    }

    public async Task<VietsubProjectResponse> RenameAsync(
        Guid projectId,
        RenameVietsubProjectRequest request,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken)
    {
        var name = NormalizeName(request.Name);
        await RequireAccessAsync(request.OrganizationId, context, requireWrite: true, cancellationToken);
        var project = await FindOwnedProjectAsync(
            projectId,
            request.OrganizationId,
            context.UserId,
            cancellationToken);
        if (project.Name == name)
        {
            return ToResponse(project);
        }

        project.Name = name;
        project.UpdatedAtUtc = UtcNow();
        AddAudit(project.OrganizationId, context, "VietsubProjectRenamed", new
        {
            project.ProjectId,
            project.Name
        });
        await db.SaveChangesAsync(cancellationToken);
        return ToResponse(project);
    }

    public async Task<VietsubProjectResponse> ArchiveAsync(
        Guid projectId,
        Guid organizationId,
        VietsubProjectRequestContext context,
        CancellationToken cancellationToken)
    {
        await RequireAccessAsync(organizationId, context, requireWrite: true, cancellationToken);
        var project = await db.Projects.SingleOrDefaultAsync(
            item => item.ProjectId == projectId
                    && item.OrganizationId == organizationId
                    && item.CreatedByUserId == context.UserId,
            cancellationToken)
            ?? throw ProjectNotFound();
        if (!project.IsArchived)
        {
            var now = UtcNow();
            project.IsArchived = true;
            project.ArchivedAtUtc = now;
            project.UpdatedAtUtc = now;
            AddAudit(project.OrganizationId, context, "VietsubProjectArchived", new
            {
                project.ProjectId,
                project.Name
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        return ToResponse(project);
    }

    private async Task<GenerationAccessContext> RequireAccessAsync(
        Guid organizationId,
        VietsubProjectRequestContext context,
        bool requireWrite,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty
            || string.IsNullOrWhiteSpace(context.UserId)
            || context.DeviceId == Guid.Empty)
        {
            throw new ArgumentException("Ngữ cảnh truy cập Vietsub không hợp lệ.");
        }

        var access = await generationAccess.RequireProjectAccessAsync(
            context.UserId,
            context.DeviceId,
            organizationId,
            projectId: null,
            cancellationToken);
        if (requireWrite && !OrganizationMemberRoles.CanGenerate(access.OrganizationRole))
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "vietsub_write_denied",
                "Vai trò Viewer chỉ được xem metadata dự án Vietsub.");
        }
        return access;
    }

    private async Task<VietsubProject> FindOwnedProjectAsync(
        Guid projectId,
        Guid organizationId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Mã dự án Vietsub không hợp lệ.");
        }

        return await db.Projects.SingleOrDefaultAsync(
            project => project.ProjectId == projectId
                       && project.OrganizationId == organizationId
                       && project.CreatedByUserId == userId
                       && !project.IsArchived,
            cancellationToken)
            ?? throw ProjectNotFound();
    }

    private void AddAudit(
        Guid organizationId,
        VietsubProjectRequestContext context,
        string eventType,
        object data) =>
        db.OrganizationAuditLogs.Add(new OrganizationAuditLog
        {
            OrganizationId = organizationId,
            ActorUserId = context.UserId,
            EventType = eventType,
            DataJson = JsonSerializer.Serialize(data, JsonOptions),
            IpAddress = context.IpAddress,
            UserAgent = context.UserAgent,
            CorrelationId = context.CorrelationId,
            OccurredAtUtc = UtcNow()
        });

    private static string NormalizeName(string? name)
    {
        var normalized = name?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 120 || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Tên dự án Vietsub phải có từ 1 đến 120 ký tự hợp lệ.");
        }
        return normalized;
    }

    private static string NormalizeSourceLanguage(string? sourceLanguageCode)
    {
        var normalized = sourceLanguageCode?.Trim().ToLowerInvariant() ?? "auto";
        if (!SourceLanguages.Contains(normalized))
        {
            throw new ArgumentException("Ngôn ngữ nguồn Vietsub phải là auto, en hoặc zh.");
        }
        return normalized;
    }

    private static VietsubProjectResponse ToResponse(VietsubProject project) =>
        new(
            project.ProjectId,
            project.OrganizationId,
            project.CreatedByUserId,
            project.Name,
            project.Status,
            project.SourceLanguageCode,
            project.TargetLanguageCode,
            project.IsArchived,
            project.CreatedAtUtc,
            project.UpdatedAtUtc,
            project.ArchivedAtUtc);

    private static AccountApiException ProjectNotFound() =>
        new(
            StatusCodes.Status404NotFound,
            "vietsub_project_not_found",
            "Không tìm thấy dự án Vietsub.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
