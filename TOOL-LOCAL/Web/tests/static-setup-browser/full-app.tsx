import { createRoot } from 'react-dom/client';
import App from '../../src/App';
import { postToHost } from '../../src/bridge';
import '../../src/styles.css';

// Production App, useSystemSetup and chrome.webview transport are used unchanged.
// Only the C# test host supplies accounts, component adapters and fault scheduling.
createRoot(document.getElementById('root')!).render(<App />);

Object.assign(window, {
  inspectFullSetup() {
    const dialog = document.querySelector<HTMLElement>('.startup-setup-dialog');
    return {
      appPresent: !!document.querySelector('.app-main'),
      mainInert: document.querySelector('.app-main')?.hasAttribute('inert') === true,
      sidebarInert: document.querySelector('.sidebar')?.hasAttribute('inert') === true,
      setupVisible: !!dialog,
      dialogCount: document.querySelectorAll('[role="dialog"], [role="alertdialog"], dialog[open]').length,
      alerts: [...document.querySelectorAll('[role="alert"]')].map(node => node.textContent).join('\n'),
      setupText: dialog?.textContent ?? '',
      updateVisible: !!document.querySelector('.update-overlay'),
      serviceErrorVisible: !!document.querySelector('.service-error-card'),
      primaryEnabled: !!dialog?.querySelector<HTMLButtonElement>('.startup-setup-primary:not(:disabled)'),
      focusInsideSetup: !!dialog?.contains(document.activeElement)
    };
  },
  clickFullSetupButton(text: string) {
    const button = [...document.querySelectorAll<HTMLButtonElement>('button')]
      .find(node => !node.disabled && node.textContent?.trim() === text);
    if (!button) throw new Error(`Enabled button not found: ${text}`);
    button.click();
  },
  clickFullSetupPrimary() {
    const button = document.querySelector<HTMLButtonElement>('.startup-setup-primary');
    if (!button || button.disabled) throw new Error('Setup primary is unavailable');
    button.click();
  },
  probeBlockedHostCommand() {
    return postToHost('generation.content', { fixtureOnly: true });
  }
});
