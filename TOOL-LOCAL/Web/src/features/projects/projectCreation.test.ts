import { describe, expect, it } from 'vitest';
import type { DashboardState, GenerationProviderStatus } from '../../types';
import { createdProjectPage, shortVideoProviderStatus } from './projectCreation';

const pending = { requestId: 'create-1', organizationId: 'org-1', previousProjectId: 'old-project' };
function dashboard(workflowStructureType = 'OpenAiStructuredPlan', projectId = 'new-project', organizationId = 'org-1') {
  return { selectedOrganizationId: organizationId,
    selectedProject: { workflowStructureType, project: { projectId, organizationId } } } as DashboardState;
}

describe('new project completion', () => {
  it.each([['DirectShortVideo', 'shortVideo'], ['OpenAiStructuredPlan', 'longVideo']])('opens the saved %s project', (workflow, page) => {
    expect(createdProjectPage(pending, 'create-1', dashboard(workflow))).toBe(page);
  });
  it('keeps the popup open for unrelated refreshes or the previous project', () => {
    expect(createdProjectPage(pending, 'poll', dashboard())).toBeNull();
    expect(createdProjectPage(pending, 'create-1', dashboard('OpenAiStructuredPlan', 'old-project'))).toBeNull();
    expect(createdProjectPage(null, undefined, dashboard())).toBeNull();
  });
  it('does not navigate from a response for another organization', () => {
    expect(createdProjectPage(pending, 'create-1', dashboard('DirectShortVideo', 'new-project', 'org-2'))).toBeNull();
    const state = dashboard();
    state.selectedProject!.project.organizationId = 'org-2';
    expect(createdProjectPage(pending, 'create-1', state)).toBeNull();
  });
});

describe('short-video readiness', () => {
  it('uses Kling readiness and price when the organization uses Fal for long video', () => {
    const result = shortVideoProviderStatus({ videoReady: true, videoProviderCode: 'fal', videoModel: 'veo',
      estimatedVideoCostPerSecond: 0.9, klingReady: true, klingModel: 'kling-3.0', estimatedKlingCostPerSecond: 0.2 } as GenerationProviderStatus);
    expect(result).toMatchObject({ videoReady: true, videoProviderCode: 'kling', videoModel: 'kling-3.0', estimatedVideoCostPerSecond: 0.2 });
  });
  it('keeps short video unavailable if only Fal is ready', () => {
    expect(shortVideoProviderStatus({ videoReady: true, klingReady: false, klingUnavailableMessage: 'Thiếu cấu hình Kling' } as GenerationProviderStatus))
      .toMatchObject({ videoReady: false, videoUnavailableMessage: 'Thiếu cấu hình Kling' });
  });
});
