import type { ProjectAssetLibrary } from './types';

export function getSceneFirstFrameAssetBlocker(
  sceneId: string,
  assetLibrary: ProjectAssetLibrary | null
): string | null {
  if (!assetLibrary) return null;

  const assignment = assetLibrary.sceneAssignments.find((item) => item.sceneId === sceneId);
  if (!assignment) return null;

  if (!assignment.isValid) {
    const details = assignment.blockers
      ?.map((blocker) => blocker.trim())
      .filter((blocker) => blocker.length > 0)
      .join(' ');
    return details || 'Lựa chọn tài sản của cảnh chưa hợp lệ. Hãy sửa lựa chọn trước khi tạo first-frame.';
  }

  if (assignment.projectAssetIds.length === 0) return null;

  const assetById = new Map(assetLibrary.assets.map((asset) => [asset.projectAssetId, asset]));
  const missingAsset = assignment.projectAssetIds.some((assetId) => !assetById.has(assetId));
  if (missingAsset) {
    return 'Dữ liệu tài sản của cảnh chưa đồng bộ. Hãy tải lại dự án trước khi tạo first-frame.';
  }

  const unlockedAssets = assignment.projectAssetIds
    .map((assetId) => assetById.get(assetId)!)
    .filter((asset) => asset.status !== 'Locked' || asset.currentVersion <= 0);
  if (assignment.hasUnlockedAssets || unlockedAssets.length > 0) {
    const assetNames = unlockedAssets.map((asset) => `“${asset.name}”`).join(', ');
    return assetNames.length > 0
      ? `Tài sản ${assetNames} chưa được xác nhận. Hãy bấm “Xác nhận tài sản cảnh” trước khi tạo first-frame.`
      : 'Tài sản của cảnh chưa được xác nhận. Hãy bấm “Xác nhận tài sản cảnh” trước khi tạo first-frame.';
  }

  return null;
}
