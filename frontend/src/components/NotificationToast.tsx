import { useEffect } from 'react';
import type { Notification } from '../hooks/useNotifications';

const typeStyles: Record<Notification['type'], { border: string; icon: string; bg: string }> = {
  info: { border: '#4a5c8a', icon: 'i', bg: '#4a5c8a18' },
  success: { border: '#10b981', icon: '\u2713', bg: '#10b98118' },
  warning: { border: '#fbbf24', icon: '!', bg: '#fbbf2418' },
  error: { border: '#b05a3a', icon: '\u2717', bg: '#b05a3a18' },
};

interface Props {
  notification: Notification;
  onDismiss: (id: string) => void;
}

export default function NotificationToast({ notification, onDismiss }: Props) {
  const style = typeStyles[notification.type];

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onDismiss(notification.id);
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [notification.id, onDismiss]);

  return (
    <div
      className="animate-fade-in-up flex items-start gap-3 px-4 py-3 rounded-lg cursor-pointer select-none pr-glass-surface"
      style={{
        backgroundColor: 'var(--pr-surface)',
        border: `1px solid ${style.border}70`,
        boxShadow: '0 8px 24px rgba(0,0,0,0.15)',
        minWidth: 320,
        maxWidth: 420,
      }}
      onClick={() => onDismiss(notification.id)}
    >
      <div
        className="flex items-center justify-center w-6 h-6 rounded text-[13px] font-mono font-bold shrink-0"
        style={{ backgroundColor: style.bg, color: style.border, border: `1px solid ${style.border}40` }}
      >
        {style.icon}
      </div>
      <div className="flex-1 min-w-0">
        <div className="text-[13px] font-mono font-bold tracking-wider uppercase" style={{ color: 'var(--pr-text)' }}>
          {notification.title}
        </div>
        <div className="text-[12px] font-mono mt-1" style={{ color: 'var(--pr-text-muted)' }}>
          {notification.message}
        </div>
      </div>
      <button
        className="text-[14px] font-mono shrink-0 mt-0.5 cursor-pointer"
        style={{ color: 'var(--pr-text-dim)', background: 'none', border: 'none' }}
        onClick={(e) => { e.stopPropagation(); onDismiss(notification.id); }}
      >
        ×
      </button>
    </div>
  );
}
