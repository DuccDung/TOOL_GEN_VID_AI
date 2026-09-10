import type { DashboardState, GenerationProviderStatus } from '../../types';

export type PendingProjectCreation = { requestId: string; organizationId: string; previousProjectId?: string };

export function createdProjectPage(pending: PendingProjectCreation | null, requestId: string | null | undefined, dashboard: DashboardState): 'shortVideo' | 'longVideo' | null {
  const project = dashboard.selectedProject;
  if (!pending || pending.requestId !== requestId || dashboard.selectedOrganizationId !== pending.organizationId ||
      !project || project.project.organizationId !== pending.organizationId || project.project.projectId === pending.previousProjectId) return null;
  return project.workflowStructureType === 'DirectShortVideo' ? 'shortVideo' : 'longVideo';
}

// Short video uses the same configured Veo variant as LongForm; the server pins each project.
export function shortVideoProviderStatus(status: GenerationProviderStatus): GenerationProviderStatus {
  const veo = status.videoProviderCode?.toLowerCase() === 'fal';
  return { ...status, videoReady: status.videoReady && veo,
    videoUnavailableMessage: veo ? status.videoUnavailableMessage : 'Cấu hình Fal/Veo cho tổ chức trước khi tạo video ngắn.' };
}
