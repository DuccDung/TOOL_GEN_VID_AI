import type { HostMessage } from './types';

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage(message: string): void;
        addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
        removeEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
      };
    };
  }
}

const webview = window.chrome?.webview;

export const isHosted = Boolean(webview);

export function postToHost<T>(type: string, payload?: T): string {
  const requestId = crypto.randomUUID();
  const message: HostMessage<T> = { type, requestId, payload };
  webview?.postMessage(JSON.stringify(message));
  return requestId;
}

export function subscribeToHost(listener: (message: HostMessage) => void): () => void {
  if (!webview) {
    return () => undefined;
  }

  const handler = (event: MessageEvent) => listener(event.data as HostMessage);
  webview.addEventListener('message', handler);
  return () => webview.removeEventListener('message', handler);
}
