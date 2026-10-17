import { useState } from 'react';
import { createRoot } from 'react-dom/client';
import { StartupSystemSetupModal } from '../../src/features/systemSetup/StartupSystemSetupModal';
import { isSystemSetupReady, type SetupSnapshot } from '../../src/features/systemSetup/types';
import type { SystemSetupController } from '../../src/features/systemSetup/useSystemSetup';
import '../../src/styles.css';

type Scenario = 'missing' | 'error' | 'warning' | 'ready';
const nextFrames = () => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
const longMessage = 'Không đủ dung lượng trên ổ chứa thành phần hệ thống. Cache và nhật ký được giữ để thử lại. '
  + 'Giải phóng dung lượng rồi chạy lại đúng bộ cài; model chưa được kiểm tra thành công.';
let setScenario: (scenario: Scenario) => void;
const originalOverflow = document.body.style.overflow;

function Fixture() {
  const [scenario, updateScenario] = useState<Scenario>('missing');
  setScenario = updateScenario;
  const snapshot: SetupSnapshot = {
    contextGeneration: 'browser-fixture', organizationId: 'test-org', startupRequired: true,
    components: ['media', 'ocr', 'qwen', 'piper'].map(id => ({
      id, name: ({ media: 'Xử lý video · FFmpeg', ocr: 'Nhận dạng phụ đề · PaddleOCR', qwen: 'Dịch Việt · Qwen 4B', piper: 'Giọng Việt · Piper' } as Record<string, string>)[id],
      version: '1.0.0', state: scenario === 'ready' || (scenario === 'warning' && id !== 'qwen') ? 'READY' : 'NEEDS_VERIFICATION',
      message: scenario === 'error' ? longMessage : 'Kiểm tra thành phần trước khi sử dụng trên máy này.',
      canInstall: true, canVerify: true, canRepair: true,
      errorCode: scenario === 'warning' && id === 'qwen' ? 'TRANSLATION_RESOURCE_CONFIRMATION_REQUIRED' : undefined
    })),
    operation: scenario === 'warning' ? { operationId: 'test-operation', mode: 'check', state: 'Failed',
      componentIds: ['qwen'], sequence: 1, allSelectedReady: false, allRequiredReady: false } : null
  };
  const ready = isSystemSetupReady(snapshot);
  const controller: SystemSetupController = {
    snapshot, busy: false, error: scenario === 'error' ? longMessage : '',
    connectionLost: false, repairProgress: null, repairError: '',
    run: () => {}, cancel: () => {}, refresh: () => {}, reconcile: () => {}, ownsResponse: () => false,
    repairApplication: () => {}, exitApplication: () => {}
  };
  return <><main inert={!ready}><button id="background-action">Nghiệp vụ nền</button></main>
    {!ready && <StartupSystemSetupModal setup={controller} />}</>;
}

createRoot(document.getElementById('root')!).render(<Fixture />);

Object.assign(window, {
  async runSetupCase(scenario: Scenario) {
    setScenario(scenario);
    await nextFrames();
    const errors: string[] = [];
    const dialog = document.querySelector<HTMLElement>('.startup-setup-dialog');
    if (scenario === 'ready') {
      if (dialog) errors.push('READY did not unmount the modal');
      if (document.querySelector('main')?.hasAttribute('inert')) errors.push('READY did not release the fixture background');
      if (document.body.style.overflow !== originalOverflow) errors.push('Body overflow was not restored');
      return { scenario, errors };
    }
    if (!dialog) return { scenario, errors: ['Modal missing'] };
    const bounds = dialog.getBoundingClientRect();
    if (bounds.left < -1 || bounds.top < -1 || bounds.right > innerWidth + 1 || bounds.bottom > innerHeight + 1)
      errors.push('Dialog exceeds viewport');
    if (dialog.getAttribute('aria-modal') !== 'true' || !dialog.getAttribute('aria-labelledby')) errors.push('Dialog semantics missing');
    for (const state of dialog.querySelectorAll<HTMLElement>('.startup-setup-state')) {
      const cell = state.closest('td')!.getBoundingClientRect();
      const label = state.getBoundingClientRect();
      if (label.right > cell.right + 1 || label.left < cell.left - 1)
        errors.push('Component state label overlaps an adjacent column');
    }
    document.querySelector<HTMLElement>('#background-action')!.focus();
    await nextFrames();
    if (!dialog.contains(document.activeElement)) errors.push('Focus escaped the dialog');
    const buttons = [...dialog.querySelectorAll<HTMLButtonElement>('button:not(:disabled)')];
    for (const button of buttons) {
      button.scrollIntoView({ block: 'nearest', inline: 'nearest' });
      await nextFrames();
      const rect = button.getBoundingClientRect();
      if (rect.top < -1 || rect.bottom > innerHeight + 1 || rect.left < -1 || rect.right > innerWidth + 1)
        errors.push(`Button is unreachable in viewport: ${button.textContent}`);
    }
    if (scenario === 'warning') {
      const checkbox = dialog.querySelector<HTMLInputElement>('input[type="checkbox"]');
      const consent = [...dialog.querySelectorAll<HTMLButtonElement>('button')].find(button => button.textContent?.includes('Vẫn thử dùng Qwen'));
      if (!checkbox || checkbox.checked || !consent?.disabled) errors.push('Resource consent is not opt-in');
    }
    return { scenario, errors, viewport: { width: innerWidth, height: innerHeight },
      dialog: { x: bounds.x, y: bounds.y, width: bounds.width, height: bounds.height }, buttonCount: buttons.length };
  }
});
