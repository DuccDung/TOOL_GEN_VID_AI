import { useCallback, useEffect, useRef } from 'react';
import { postToHost, subscribeToHost } from '../../bridge';

export function useShortVideoHost(organizationId: string, projectId?: string) {
  const requests = useRef(new Map<string, { resolve: (value: unknown) => void; reject: (error: Error) => void }>());
  useEffect(() => {
    const pending = requests.current;
    const unsubscribe = subscribeToHost(message => {
      if (!message.requestId) return;
      const request = pending.get(message.requestId);
      if (!request || !(message.type.startsWith('outfit.') || message.type === 'short-library.result' || message.type === 'operation.error')) return;
      pending.delete(message.requestId);
      if (message.type === 'operation.error') request.reject(new Error(message.error?.message ?? 'Không hoàn tất thao tác. Hãy thử lại.'));
      else request.resolve(message.payload);
    });
    return () => {
      unsubscribe();
      for (const request of pending.values()) request.reject(new DOMException('Phiên làm việc đã đổi.', 'AbortError'));
      pending.clear();
    };
  }, [organizationId, projectId]);
  return useCallback(<T,>(type: string, payload: object = {}) => new Promise<T>((resolve, reject) => {
    const id = postToHost(type, { ...payload, organizationId, ...(projectId ? { projectId } : {}) });
    requests.current.set(id, { resolve: value => resolve(value as T), reject });
  }), [organizationId, projectId]);
}
