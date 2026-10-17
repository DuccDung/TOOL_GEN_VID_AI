import { useLayoutEffect, useState } from 'react';
import { useLicenseOffers } from './useLicenseOffers';

export type LicenseInformationView = 'current' | 'offers';

// Browsing owns separate catalog requests so the payment gate cannot consume
// a response from this dialog or clear its loading state.
export function useLicenseInformation(userId: string) {
  const [view, setView] = useState<LicenseInformationView | null>(null);
  const catalog = useLicenseOffers(userId);

  const close = () => {
    catalog.reset();
    setView(null);
  };

  useLayoutEffect(() => { close(); }, [userId]);

  const open = (nextView: LicenseInformationView) => {
    setView(nextView);
    if (nextView === 'offers') catalog.loadOffers();
    else catalog.reset();
  };

  return { view, ...catalog, open, close };
}
