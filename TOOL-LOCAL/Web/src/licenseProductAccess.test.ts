import { describe, expect, it } from 'vitest';
import { licenseProductScope, videoToolsAllowed } from './licenseProductAccess';
import type { CurrentLicense } from './types';

describe('license product scope', () => {
  it.each([undefined, null, '{}', '{"maxConcurrentSessions":1}'])('preserves legacy rights for %s', value => {
    expect(videoToolsAllowed({ featureFlagsJson: value } as CurrentLicense)).toBe(true);
  });
  it.each(['{"productScope":"vietsub"}', '{"productScope":"unknown"}', 'broken', 'null', '[]'])('blocks unrelated tools for %s', value => {
    expect(videoToolsAllowed({ featureFlagsJson: value } as CurrentLicense)).toBe(false);
  });
  it('recognizes the translation module separately', () => {
    expect(licenseProductScope({ featureFlagsJson:'{"productScope":"vietsub","vietsubCloudEnabled":true}' } as CurrentLicense)).toBe('vietsub');
  });
});
