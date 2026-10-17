import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { isHosted, postToHost } from '../../bridge';
import type { HostMessage, LicenseOffer } from '../../types';

export type LicenseOffersStatus = 'idle' | 'loading' | 'available' | 'unavailable' | 'error';

export function useLicenseOffers(userId: string) {
  const [offers, setOffers] = useState<LicenseOffer[]>([]);
  const [status, setStatus] = useState<LicenseOffersStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const requests = useRef(new Set<string>());
  const activeRequest = useRef<string | null>(null);
  const timer = useRef<number | undefined>(undefined);

  const stopWaiting = () => {
    window.clearTimeout(timer.current);
    timer.current = undefined;
    activeRequest.current = null;
  };

  const reset = () => {
    stopWaiting();
    setOffers([]);
    setStatus('idle');
    setError(null);
  };

  const markUnavailable = () => {
    stopWaiting();
    setOffers([]);
    setStatus('unavailable');
    setError(null);
  };

  useLayoutEffect(reset, [userId]);
  useEffect(() => () => { stopWaiting(); requests.current.clear(); }, []);

  const loadOffers = () => {
    if (activeRequest.current) return false;
    setOffers([]);
    setError(null);
    if (!isHosted) {
      setStatus('error');
      setError('Mở ứng dụng desktop và đăng nhập để xem các gói đang được cung cấp.');
      return false;
    }
    setStatus('loading');
    try {
      const requestId = postToHost('license.offers.get');
      requests.current.add(requestId);
      activeRequest.current = requestId;
      timer.current = window.setTimeout(() => {
        if (activeRequest.current !== requestId) return;
        stopWaiting();
        setStatus('error');
        setError('Máy chủ chưa phản hồi. Vui lòng thử tải lại danh sách gói.');
      }, 30000);
      return true;
    } catch {
      stopWaiting();
      setStatus('error');
      setError('Không thể gửi yêu cầu tải danh sách gói. Vui lòng thử lại.');
      return false;
    }
  };

  // Consume old responses too: a timeout, closed dialog or account change must
  // not turn a late catalog error into a global payment error or toast.
  const handleMessage = (message: HostMessage) => {
    if (!message.requestId || !requests.current.has(message.requestId)
      || (message.type !== 'license.offers' && message.type !== 'operation.error')) return false;
    if (message.requestId !== activeRequest.current) return true;
    stopWaiting();
    if (message.type === 'operation.error') {
      if (message.error?.code === 'payments_unavailable') markUnavailable();
      else {
        setStatus('error');
        setError(message.error?.message || 'Không thể tải danh sách gói. Vui lòng thử lại.');
      }
    } else if (Array.isArray(message.payload)) {
      setOffers(message.payload as LicenseOffer[]);
      setStatus(message.payload.length ? 'available' : 'unavailable');
      setError(null);
    } else {
      setStatus('error');
      setError('Danh sách gói chưa hợp lệ. Vui lòng thử lại.');
    }
    return true;
  };

  return { offers, status, error, loading: status === 'loading',
    isWaiting: () => activeRequest.current !== null,
    loadOffers, handleMessage, reset, markUnavailable };
}
