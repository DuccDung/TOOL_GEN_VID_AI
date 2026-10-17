import type { CurrentLicense } from './types';

export function licenseProductScope(license?: CurrentLicense | null): string {
  try {
    const flags = JSON.parse(license?.featureFlagsJson?.trim() || '{}');
    if (!flags || typeof flags !== 'object' || Array.isArray(flags)) return 'invalid';
    if (!Object.hasOwn(flags, 'productScope')) return 'all-tools';
    return flags.productScope === 'all-tools' || flags.productScope === 'vietsub' ? flags.productScope : 'invalid';
  } catch { return 'invalid'; }
}
export const localOnlyLicense = (license?: CurrentLicense | null) => licenseProductScope(license) === 'vietsub'
  && JSON.parse(license!.featureFlagsJson!).vietsubCloudEnabled !== true;

export const videoToolsAllowed = (license?: CurrentLicense | null) => licenseProductScope(license) === 'all-tools';
