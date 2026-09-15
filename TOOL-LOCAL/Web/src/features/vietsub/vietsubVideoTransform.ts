import type { VietsubVideoTransformSettings } from './types';

export const defaultVietsubVideoTransformSettings: VietsubVideoTransformSettings = {
  flipHorizontal: false,
  flipVertical: false
};

export function cloneVideoTransformSettings(
  settings: VietsubVideoTransformSettings
): VietsubVideoTransformSettings {
  return { ...settings };
}
