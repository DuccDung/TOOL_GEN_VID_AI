import { describe, expect, it } from 'vitest';
import { acceptSetupSnapshot, isSystemSetupReady, type SetupSnapshot } from './types';

const ready = (): SetupSnapshot => ({
  organizationId: 'org', contextGeneration: 'context', revision: 1, startupRequired: true,
  components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({ id, name: id, version: '1',
    state: 'READY', message: 'Sẵn sàng', canInstall: true, canVerify: true, canRepair: true }))
});

describe('readiness requires the complete native component contract', () => {
  it.each([
    ['empty', (value: SetupSnapshot) => ({ ...value, components: [] })],
    ['missing Piper', (value: SetupSnapshot) => ({ ...value, components: value.components.slice(0, 3) })],
    ['duplicate OCR', (value: SetupSnapshot) => ({ ...value,
      components: [...value.components.slice(0, 3), value.components[1]] })],
    ['missing context', (value: SetupSnapshot) => ({ ...value, contextGeneration: '' })]
  ] as const)('does not unlock on %s even when every supplied state is READY', (_name, change) => {
    const incomplete = change(ready());
    expect(isSystemSetupReady(incomplete)).toBe(false);
    const current = ready();
    expect(acceptSetupSnapshot(current, incomplete, 'org', true)).toBe(current);
  });

  it('keeps explicitly disabled components valid for existing delivery modes', () => {
    const value = ready();
    value.components = value.components.map(component => component.id === 'qwen' || component.id === 'piper'
      ? { ...component, state: 'DISABLED' } : component);
    expect(isSystemSetupReady(value)).toBe(true);
  });
});
