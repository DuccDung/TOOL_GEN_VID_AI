import type { DashboardState, GenerationProviderStatus } from '../../types';

export type PendingProjectCreation = { requestId: string; organizationId: string; previousProjectId?: string };

export function createdProjectPage(pending: PendingProjectCreation | null, requestId: string | null | undefined, dashboard: DashboardState): 'shortVideo' | 'longVideo' | null {
  const project = dashboard.selectedProject;
  if (!pending || pending.requestId !== requestId || dashboard.selectedOrganizationId !== pending.organizationId ||
      !project || project.project.organizationId !== pending.organizationId || project.project.projectId === pending.previousProjectId) return null;
  return project.workflowStructureType === 'DirectShortVideo' ? 'shortVideo' : 'longVideo';
}

// LongForm readiness can refer to Fal; the direct short-video workflow uses Kling.
// Server generation still validates the project snapshot and organization policy.
export function shortVideoProviderStatus(status: GenerationProviderStatus): GenerationProviderStatus {
  return { ...status, videoReady: status.klingReady, videoProviderCode: 'kling', videoProviderName: 'Kling',
    videoModel: status.klingModel, videoResolution: '720p', videoUnavailableCode: status.klingUnavailableCode,
    videoUnavailableMessage: status.klingUnavailableMessage, estimatedVideoCostPerSecond: status.estimatedKlingCostPerSecond };
}
