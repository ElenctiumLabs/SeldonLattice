import { useState } from 'react';
import type { Notification, NotificationType } from '../hooks/useNotifications';

const typeLabels: Record<NotificationType, { label: string; color: string }> = {
  info: { label: 'Info', color: '#4a5c8a' },
  success: { label: 'Success', color: '#6e8c74' },
  warning: { label: 'Warning', color: '#fbbf24' },
  error: { label: 'Error', color: '#b05a3a' },
};

function timeAgo(ts: number): string {
  const diff = Date.now() - ts;
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  return `${days}d ago`;
}

interface Props {
  notifications: Notification[];
  onMarkAsRead: (id: string) => void;
  onMarkAllRead: () => void;
  onClearAll: () => void;
  onClose: () => void;
}

export default function NotificationCenter({
  notifications,
  onMarkAsRead,
  onMarkAllRead,
  onClearAll,
  onClose,
}: Props) {
  const [filter, setFilter] = useState<NotificationType | 'all'>('all');

  const filtered = filter === 'all' ? notifications : notifications.filter((n) => n.type === filter);
  const unreadCount = notifications.filter((n) => !n.read).length;

  return (
    <div
      className="fixed inset-0 z-60 flex items-start justify-end"
      style={{ backgroundColor: 'rgba(15, 12, 8, 0.45)', backdropFilter: 'blur(4px)' }}
      onClick={onClose}
    >
      <div
        className="flex flex-col h-full w-full max-w-md animate-fade-in-up pr-glass-surface"
        style={{
          backgroundColor: 'var(--pr-surface)',
          borderLeft: '1px solid var(--pr-border)',
          boxShadow: '-8px 0 30px rgba(0,0,0,0.15)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div className="px-4 py-3 border-b flex items-center justify-between" style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)' }}>
          <div className="flex items-center gap-3">
            <span className="text-[14px] font-mono font-bold tracking-widest uppercase" style={{ color: 'var(--pr-text)' }}>
              Notifications
            </span>
            {unreadCount > 0 && (
              <span
                className="text-[11px] font-mono font-bold px-2 py-0.5 rounded"
                style={{ backgroundColor: 'rgba(120,89,26,0.15)', color: 'var(--pr-role-proposer)', border: '1px solid var(--pr-role-proposer)' }}
              >
                {unreadCount}
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            {unreadCount > 0 && (
              <button
                className="text-[11px] font-mono uppercase tracking-wider cursor-pointer px-2 py-1 rounded transition-colors"
                style={{ color: 'var(--pr-text-muted)', background: 'var(--pr-surface)', border: '1px solid var(--pr-border)' }}
                onClick={onMarkAllRead}
              >
                Mark all read
              </button>
            )}
            <button
              className="flex items-center justify-center w-8 h-8 rounded cursor-pointer text-lg font-mono"
              style={{ color: 'var(--pr-text-dim)', background: 'none', border: '1px solid var(--pr-border)' }}
              onClick={onClose}
            >
              ×
            </button>
          </div>
        </div>

        {/* Filters */}
        <div className="px-4 py-2 border-b flex gap-2 flex-wrap" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
          {(['all', 'info', 'success', 'warning', 'error'] as const).map((f) => (
            <button
              key={f}
              className="text-[11px] font-mono uppercase tracking-wider px-2.5 py-1 rounded cursor-pointer transition-all"
              style={{
                color: filter === f ? 'var(--pr-bg)' : 'var(--pr-text-muted)',
                backgroundColor: filter === f ? 'var(--pr-role-proposer)' : 'transparent',
                border: `1px solid ${filter === f ? 'var(--pr-role-proposer)' : 'var(--pr-border)'}`,
              }}
              onClick={() => setFilter(f)}
            >
              {f === 'all' ? 'All' : typeLabels[f].label}
            </button>
          ))}
        </div>

        {/* Notification list */}
        <div className="flex-1 overflow-y-auto p-4 space-y-2" style={{ backgroundColor: 'var(--pr-bg)' }}>
          {filtered.length === 0 ? (
            <div className="text-center py-12">
              <div className="text-[13px] font-mono" style={{ color: 'var(--pr-text-dim)' }}>
                No notifications
              </div>
            </div>
          ) : (
            filtered.map((n) => {
              const tl = typeLabels[n.type];
              return (
                <div
                  key={n.id}
                  className="px-3 py-2.5 rounded-lg cursor-pointer transition-colors"
                  style={{
                    backgroundColor: n.read ? 'var(--pr-surface)' : 'var(--pr-surface-raised)',
                    border: `1px solid ${n.read ? 'var(--pr-border)' : `${tl.color}70`}`,
                  }}
                  onClick={() => onMarkAsRead(n.id)}
                >
                  <div className="flex items-center gap-2 mb-1">
                    <span
                      className="text-[10px] font-mono font-bold uppercase px-1.5 py-0.5 rounded"
                      style={{ backgroundColor: `${tl.color}18`, color: tl.color, border: `1px solid ${tl.color}35` }}
                    >
                      {tl.label}
                    </span>
                    <span className="text-[10px] font-mono" style={{ color: 'var(--pr-text-dim)' }}>
                      {timeAgo(n.timestamp)}
                    </span>
                    {!n.read && (
                      <span
                        className="w-2 h-2 rounded-full ml-auto"
                        style={{ backgroundColor: 'var(--pr-role-proposer)' }}
                      />
                    )}
                  </div>
                  <div className="text-[13px] font-mono font-bold tracking-wider uppercase" style={{ color: 'var(--pr-text)' }}>
                    {n.title}
                  </div>
                  <div className="text-[12px] font-mono mt-1" style={{ color: 'var(--pr-text-muted)' }}>
                    {n.message}
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Footer */}
        {notifications.length > 0 && (
          <div className="px-4 py-3 border-t" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <button
              className="w-full text-[11px] font-mono uppercase tracking-wider px-3 py-2 rounded cursor-pointer transition-colors"
              style={{ color: '#f19c9c', background: 'none', border: '1px solid #e74c3c' }}
              onClick={onClearAll}
            >
              Clear all notifications
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
