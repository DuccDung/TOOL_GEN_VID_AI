import { useState } from 'react';
import type { ReactNode } from 'react';
import { X } from 'lucide-react';

/** Dismissal belongs to this event in this mounted context, never to the underlying error/job. */
export function VietsubNotice({ eventId, children, icon, actions, className = '', role = 'status' }: {
  eventId: string | number;
  children: ReactNode;
  icon?: ReactNode;
  actions?: ReactNode;
  className?: string;
  role?: 'status' | 'alert';
}) {
  const [dismissed, setDismissed] = useState<string | number | null>(null);
  if (dismissed === eventId) return null;
  return <div className={`vietsub-notice ${className}`} role={role}>
    {icon && <span className="vietsub-notice-icon" aria-hidden="true">{icon}</span>}
    <div className="vietsub-notice-content">{children}</div>
    {actions && <div className="vietsub-notice-actions">{actions}</div>}
    <button type="button" className="vietsub-notice-close" aria-label="Đóng thông báo" title="Đóng thông báo"
      onClick={() => setDismissed(eventId)}><X size={16} /></button>
  </div>;
}
