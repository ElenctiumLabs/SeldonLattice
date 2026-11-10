import React from 'react'
import type { ShortcutGroup } from '../hooks/useKeyboardShortcuts'

interface ShortcutsHelpProps {
  groups: ShortcutGroup[]
  onClose: () => void
}

function formatKey(s: { key: string; ctrl?: boolean; shift?: boolean; alt?: boolean }): string {
  const parts: string[] = []
  if (s.ctrl) parts.push('Ctrl')
  if (s.shift) parts.push('Shift')
  if (s.alt) parts.push('Alt')
  parts.push(s.key.length === 1 ? s.key.toUpperCase() : s.key)
  return parts.join(' + ')
}

export const ShortcutsHelp: React.FC<ShortcutsHelpProps> = ({ groups, onClose }) => {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-sm p-4"
      onClick={onClose}
    >
      <div
        className="rounded-xl shadow-2xl w-full max-w-md max-h-[80vh] overflow-hidden animate-fade-in-up pr-glass-surface"
        style={{
          backgroundColor: 'var(--pr-surface)',
          border: '1px solid var(--pr-border)',
          color: 'var(--pr-text)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="px-5 py-4 border-b flex items-center justify-between" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
          <h2 className="text-[13px] font-mono tracking-widest uppercase font-bold" style={{ color: 'var(--pr-text)' }}>
            KEYBOARD SHORTCUTS
          </h2>
          <button
            onClick={onClose}
            className="w-8 h-8 rounded flex items-center justify-center transition-colors cursor-pointer text-lg font-mono"
            style={{ color: 'var(--pr-text-dim)', border: '1px solid var(--pr-border)', background: 'var(--pr-surface)' }}
          >
            ×
          </button>
        </div>

        <div className="overflow-y-auto p-5 space-y-5 custom-scrollbar" style={{ backgroundColor: 'var(--pr-bg)' }}>
          {groups.map((group) => (
            <div key={group.label}>
              <h3 className="text-[11px] font-mono uppercase tracking-widest mb-2 font-bold" style={{ color: 'var(--pr-text-muted)' }}>
                {group.label}
              </h3>
              <div className="space-y-1.5">
                {group.shortcuts.map((s) => (
                  <div key={s.key + String(s.ctrl)} className="flex items-center justify-between py-1 border-b last:border-b-0" style={{ borderColor: 'var(--pr-border)' }}>
                    <span className="text-[13px]" style={{ color: 'var(--pr-text)' }}>{s.description}</span>
                    <kbd
                      className="ml-4 px-2 py-0.5 rounded text-[11px] font-mono whitespace-nowrap font-bold"
                      style={{
                        backgroundColor: 'var(--pr-surface-raised)',
                        border: '1px solid var(--pr-border)',
                        color: 'var(--pr-role-proposer)',
                      }}
                    >
                      {formatKey(s)}
                    </kbd>
                  </div>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};