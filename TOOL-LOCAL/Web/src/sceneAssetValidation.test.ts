import { describe, expect, it } from 'vitest';
import { getSceneFirstFrameAssetBlocker } from './sceneAssetValidation';
import type { ProjectAssetLibrary, ProjectAssetSummary } from './types';

const sceneId = 'scene-2';

function asset(overrides: Partial<ProjectAssetSummary> = {}): ProjectAssetSummary {
  return {
    projectAssetId: 'asset-background',
    assetType: 'Background',
    name: 'Bàn chăm cây bên cửa sổ',
    canonicalDescription: 'Bàn chăm cây cạnh cửa sổ có ánh sáng tự nhiên.',
    status: 'Locked',
    currentVersion: 1,
    updatedAtUtc: '2026-09-05T00:00:00Z',
    concurrencyToken: 'row-version',
    sceneIds: [sceneId],
    assetKey: 'background-window-garden-table',
    sourceKind: 'AiGenerated',
    ...overrides
  };
}

function library(overrides: Partial<ProjectAssetLibrary> = {}): ProjectAssetLibrary {
  return {
    projectId: 'project-1',
    canEdit: true,
    assets: [asset()],
    sceneAssignments: [{
      sceneId,
      projectAssetIds: ['asset-background'],
      hasUnlockedAssets: false,
      isValid: true,
      backgroundCount: 1,
      promptCharacters: 120,
      promptLimit: 2500,
      requiredPromptCharacters: 80
    }],
    ...overrides
  };
}

describe('getSceneFirstFrameAssetBlocker', () => {
  it('allows scenes whose assigned assets are locked at a materialized version', () => {
    expect(getSceneFirstFrameAssetBlocker(sceneId, library())).toBeNull();
  });

  it('blocks a draft asset before the UI requests a first-frame quote', () => {
    const result = getSceneFirstFrameAssetBlocker(sceneId, library({
      assets: [asset({ status: 'Draft', currentVersion: 0 })],
      sceneAssignments: [{
        ...library().sceneAssignments[0],
        hasUnlockedAssets: true
      }]
    }));

    expect(result).toContain('Bàn chăm cây bên cửa sổ');
    expect(result).toContain('Xác nhận tài sản cảnh');
  });

  it('matches the server guard when a Locked asset has no current version', () => {
    expect(getSceneFirstFrameAssetBlocker(sceneId, library({
      assets: [asset({ status: 'Locked', currentVersion: 0 })]
    }))).toContain('chưa được xác nhận');
  });

  it('shows server-provided selection blockers for an invalid assignment', () => {
    expect(getSceneFirstFrameAssetBlocker(sceneId, library({
      sceneAssignments: [{
        ...library().sceneAssignments[0],
        isValid: false,
        blockers: ['Mỗi cảnh chỉ được chọn một bối cảnh.']
      }]
    }))).toBe('Mỗi cảnh chỉ được chọn một bối cảnh.');
  });

  it('asks for a refresh when an assigned asset is missing from the UI snapshot', () => {
    expect(getSceneFirstFrameAssetBlocker(sceneId, library({ assets: [] }))).toContain('chưa đồng bộ');
  });
});

