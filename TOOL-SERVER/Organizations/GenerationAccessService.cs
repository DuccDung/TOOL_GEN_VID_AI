using Microsoft.EntityFrameworkCore;
using TOOL_SERVER.Authentication;
using TOOL_SERVER.Accounts;
using TOOL_SERVER.Data;
using TOOL_SERVER.Domain.Organizations;
using TOOL_SERVER.Models;

namespace TOOL_SERVER.Organizations;

public sealed record GenerationAccessContext(
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationRole,
    Project? Project);

public interface IGenerationAccessService
{
    Task<GenerationAccessContext> RequireAsync(
        string userId,
        Guid deviceId,
        Guid? requestedOrganizationId,
        Guid? projectId,
        CancellationToken cancellationToken);
}

internal sealed class GenerationAccessService(
    AccountDbContext accountDb,
    AiGovernanceDbContext governanceDb,
    VideoFactoryDbContext videoDb,
    TimeProvider timeProvider) : IGenerationAccessService
{
    public async Task<GenerationAccessContext> RequireAsync(
        string userId,
        Guid deviceId,
        Guid? requestedOrganizationId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        await RequireLicenseAsync(userId, deviceId, cancellationToken);

        Project? project = null;
        if (projectId is { } requestedProjectId)
        {
            if (requestedProjectId == Guid.Empty)
            {
                throw new ArgumentException("Project ID không hợp lệ.");
            }
            project = await videoDb.Projects.SingleOrDefaultAsync(
                x => x.ProjectId == requestedProjectId &&
                     x.RemoteUserId == userId &&
                     x.DeletedAtUtc == null,
                cancellationToken)
                ?? throw new AccountApiException(StatusCodes.Status404NotFound, "project_not_found", "Không tìm thấy dự án.");
            if (requestedOrganizationId is { } requested &&
                project.OrganizationId is { } assigned &&
                requested != assigned)
            {
                throw new AccountApiException(StatusCodes.Status404NotFound, "project_not_found", "Không tìm thấy dự án trong tổ chức đã chọn.");
            }
        }

        var organizationId = requestedOrganizationId ?? project?.OrganizationId;
        OrganizationMember membership;
        if (organizationId is { } selected)
        {
            membership = await FindMembershipAsync(selected, userId, cancellationToken)
                ?? throw AccessDenied();
        }
        else
        {
            var memberships = await governanceDb.OrganizationMembers
                .Include(x => x.Organization)
                .Where(x => x.UserId == userId &&
                            x.Status == OrganizationMemberStatuses.Active &&
                            x.Organization.Status == OrganizationStatuses.Active)
                .OrderBy(x => x.JoinedAtUtc)
                .Take(2)
                .ToListAsync(cancellationToken);
            if (memberships.Count == 0)
            {
                throw AccessDenied();
            }
            if (memberships.Count > 1)
            {
                throw new AccountApiException(
                    StatusCodes.Status409Conflict,
                    "organization_required",
                    "Tài khoản thuộc nhiều tổ chức; hãy chọn tổ chức trước khi dùng AI.");
            }
            membership = memberships[0];
            organizationId = membership.OrganizationId;
        }

        if (!OrganizationMemberRoles.CanGenerate(membership.Role))
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "organization_generation_denied",
                "Vai trò Viewer không có quyền sử dụng AI.");
        }

        if (project is not null)
        {
            if (project.OrganizationId is null)
            {
                project.OrganizationId = organizationId;
                project.CreatedByUserId ??= userId;
                project.UpdatedAtUtc = UtcNow();
                await videoDb.SaveChangesAsync(cancellationToken);
            }
            else if (project.OrganizationId != organizationId)
            {
                throw new AccountApiException(StatusCodes.Status404NotFound, "project_not_found", "Không tìm thấy dự án trong tổ chức đã chọn.");
            }
        }

        return new GenerationAccessContext(
            organizationId.Value,
            membership.Organization.Name,
            membership.Role,
            project);
    }

    private async Task RequireLicenseAsync(string userId, Guid deviceId, CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var valid = await accountDb.LicenseActivations
            .AsNoTracking()
            .AnyAsync(x => x.DeviceId == deviceId &&
                           x.Status == "Active" &&
                           x.RevokedAtUtc == null &&
                           x.LastVerifiedAtUtc >= now.Subtract(LicensePolicy.LeaseDuration) &&
                           x.UserLicense.UserId == userId &&
                           (x.UserLicense.Status == "Trial" || x.UserLicense.Status == "Active") &&
                           x.UserLicense.StartsAtUtc <= now &&
                           (x.UserLicense.ExpiresAtUtc == null || x.UserLicense.ExpiresAtUtc > now) &&
                           x.UserLicense.RevokedAtUtc == null,
                cancellationToken);
        if (!valid)
        {
            throw new AccountApiException(
                StatusCodes.Status403Forbidden,
                "license_unavailable",
                "License hoặc lease của thiết bị không còn hiệu lực; hãy heartbeat lại trước khi dùng AI.");
        }
    }

    private Task<OrganizationMember?> FindMembershipAsync(
        Guid organizationId,
        string userId,
        CancellationToken cancellationToken) =>
        governanceDb.OrganizationMembers
            .Include(x => x.Organization)
            .SingleOrDefaultAsync(x => x.OrganizationId == organizationId &&
                                       x.UserId == userId &&
                                       x.Status == OrganizationMemberStatuses.Active &&
                                       x.Organization.Status == OrganizationStatuses.Active,
                cancellationToken);

    private static AccountApiException AccessDenied() =>
        new(StatusCodes.Status403Forbidden, "organization_access_denied", "Tài khoản chưa được gán vào tổ chức đang hoạt động.");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
