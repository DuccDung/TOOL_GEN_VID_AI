import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { isHosted, postToHost } from '../../bridge';
import type { HostMessage, LicenseOffer } from '../../types';

export type LicenseInformationView = 'current' | 'offers';

// Browsing has its own requests so dashboard refreshes and the license payment
// gate cannot clear its loading state or consume a late response from a closed dialog.
export function useLicenseInformation(userId: string) {
  const [view, setView] = useState<LicenseInformationView | null>(null);
  const [offers, setOffers] = useState<LicenseOffer[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const pending = useRef(new Set<string>());
  const activeRequest = useRef<string | null>(null);
  const timer = useRef<number | undefined>(undefined);

  const stopWaiting = () => {
    window.clearTimeout(timer.current);
    timer.current = undefined;
    activeRequest.current = null;
  };

  const close = () => {
    stopWaiting();
    setView(null);
    setOffers([]);
    setLoading(false);
    setError(null);
  };

  useLayoutEffect(() => { close(); }, [userId]);
  useEffect(() => () => { stopWaiting(); pending.current.clear(); }, []);

  const loadOffers = () => {
    if (activeRequest.current) return;
    setOffers([]);
    setError(null);
    if (!isHosted) {
      setLoading(false);
      setError('Mở ứng dụng desktop và đăng nhập để xem các gói đang được cung cấp.');
      return;
    }
    setLoading(true);
    try {
      const requestId = postToHost('license.offers.get');
      pending.current.add(requestId);
      activeRequest.current = requestId;
      timer.current = window.setTimeout(() => {
        if (activeRequest.current !== requestId) return;
        stopWaiting();
        setLoading(false);
        setError('Máy chủ chưa phản hồi. Vui lòng thử tải lại danh sách gói.');
      }, 30000);
    } catch {
      stopWaiting();
      setLoading(false);
      setError('Không thể gửi yêu cầu tải danh sách gói. Vui lòng thử lại.');
    }
  };

  const open = (nextView: LicenseInformationView) => {
    setView(nextView);
    if (nextView === 'offers') loadOffers();
    else {
      stopWaiting();
      setLoading(false);
      setError(null);
    }
  };

  // Called before the App's general bridge handler. Consume only our own IDs,
  // including cancelled/timed-out requests, without changing other operations.
  const handleMessage = (message: HostMessage): boolean => {
    if (!message.requestId || !pending.current.has(message.requestId)
      || (message.type !== 'license.offers' && message.type !== 'operation.error')) return false;
    pending.current.delete(message.requestId);
    if (activeRequest.current !== message.requestId) return true;
    stopWaiting();
    setLoading(false);
    if (message.type === 'operation.error') {
      setError(message.error?.message || 'Không thể tải danh sách gói. Vui lòng thử lại.');
    } else if (Array.isArray(message.payload)) {
      setOffers(message.payload as LicenseOffer[]);
      setError(null);
    } else {
      setError('Danh sách gói chưa hợp lệ. Vui lòng thử lại.');
    }
    return true;
  };

  return { view, offers, loading, error, open, close, loadOffers, handleMessage };
}
