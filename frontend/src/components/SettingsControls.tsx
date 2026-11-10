import React from 'react';

export function SectionCard({ title, subtitle, children }: { title: string; subtitle: string; children: React.ReactNode }) {
  return (
    <div>
      <div className="mb-5">
        <h2 style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)', fontSize: 16, marginBottom: 2 }}>{title}</h2>
        <p style={{ fontFamily: 'var(--pr-font-mono)', color: 'var(--pr-text-dim)', fontSize: 11 }}>{subtitle}</p>
      </div>
      <div className="space-y-3">{children}</div>
    </div>
  );
}

export function Row({ label, desc, children }: { label: string; desc: string; children: React.ReactNode }) {
  return (
    <div
      className="flex items-center justify-between gap-4 px-4 py-3 rounded-lg border"
      style={{ borderColor: 'var(--pr-border)', background: 'var(--pr-surface)' }}
    >
      <div className="flex-1 min-w-0">
        <p style={{ fontFamily: 'var(--pr-font-mono)', color: 'var(--pr-text)', fontSize: 12, marginBottom: 1 }}>{label}</p>
        <p style={{ fontFamily: 'var(--pr-font-mono)', color: 'var(--pr-text-dim)', fontSize: 10 }}>{desc}</p>
      </div>
      <div className="flex-0">{children}</div>
    </div>
  );
}

export function TextInput({ value, onChange, placeholder, type = 'text', disabled, onKeyDown, onBlur, maxLength }: {
  value: string; onChange: (v: string) => void; placeholder?: string; type?: string; disabled?: boolean; onKeyDown?: (e: React.KeyboardEvent<HTMLInputElement>) => void; onBlur?: () => void; maxLength?: number;
}) {
  return (
    <input
      type={type}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      disabled={disabled}
      onKeyDown={onKeyDown}
      onBlur={onBlur}
      maxLength={maxLength}
      className="px-2.5 py-1.5 rounded border text-[12px] transition-colors"
      style={{
        fontFamily: 'var(--pr-font-mono)',
        color: 'var(--pr-text)',
        background: disabled ? 'var(--pr-bg)' : 'var(--pr-surface-raised)',
        borderColor: 'var(--pr-border)',
        width: 200,
        opacity: disabled ? 0.5 : 1,
      }}
    />
  );
}

export function ToggleGroup({ options, value, onChange, accentColour, disabled }: {
  options: { value: string; label: string }[];
  value: string;
  onChange: (v: string) => void;
  accentColour: string;
  disabled?: boolean;
}) {
  return (
    <div className="flex gap-1">
      {options.map((o) => (
        <button
          key={o.value}
          onClick={() => !disabled && onChange(o.value)}
          disabled={disabled}
          className="px-3 py-1.5 rounded text-[12px] transition-all border"
          style={{
            fontFamily: 'var(--pr-font-mono)',
            color: value === o.value ? 'var(--pr-bg)' : 'var(--pr-text-muted)',
            background: value === o.value ? accentColour : 'transparent',
            borderColor: value === o.value ? accentColour : 'var(--pr-border)',
            cursor: disabled ? 'not-allowed' : 'pointer',
            opacity: disabled ? 0.5 : 1,
          }}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}

export function SliderField({ min, max, step = 1, value, onChange, suffix, format, disabled, accentColour }: {
  min: number; max: number; step?: number; value: number | undefined; onChange: (v: number) => void;
  suffix?: string; format?: (v: number) => string; disabled?: boolean; accentColour: string;
}) {
  const displayValue = value ?? min;
  return (
    <div className="flex items-center gap-3">
      <input
        type="range" min={min} max={max} step={step} value={displayValue}
        onChange={(e) => onChange(step < 1 ? parseFloat(e.target.value) : parseInt(e.target.value, 10))}
        disabled={disabled}
        className="w-28"
        style={{ accentColor: accentColour, opacity: disabled ? 0.5 : 1 }}
      />
      <span style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 12, color: 'var(--pr-text)', minWidth: 42, textAlign: 'right' }}>
        {format ? format(displayValue) : displayValue}{suffix}
      </span>
    </div>
  );
}

export function Toggle({ checked, onChange, disabled, colour }: {
  checked: boolean; onChange: (v: boolean) => void; disabled?: boolean; colour?: string;
}) {
  const c = colour ?? 'var(--pr-role-judge)';
  return (
    <button
      onClick={() => !disabled && onChange(!checked)}
      disabled={disabled}
      className="relative w-11 h-6 rounded-full transition-colors border overflow-hidden flex-0"
      style={{
        background: checked ? c : 'var(--pr-surface-raised)',
        borderColor: checked ? c : 'var(--pr-border-strong)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.5 : 1,
      }}
    >
      <span
        className="absolute top-0.5 w-4 h-4 rounded-full transition-all shadow-sm"
        style={{ left: checked ? 22 : 2, background: checked ? '#ffffff' : 'var(--pr-text-dim)' }}
      />
    </button>
  );
}

export function ProviderSelect({
  value,
  providers,
  onChange,
  disabled,
}: {
  value: string;
  providers: string[];
  onChange: (v: string) => void;
  disabled?: boolean;
}) {
  return (
    <select
      value={value}
      onChange={(e) => onChange(e.target.value)}
      disabled={disabled}
      className="px-2.5 py-1.5 rounded border text-[12px]"
      style={{
        fontFamily: 'var(--pr-font-mono)',
        color: 'var(--pr-text)',
        background: disabled ? 'var(--pr-bg)' : 'var(--pr-surface-raised)',
        borderColor: 'var(--pr-border)',
        width: 200,
        opacity: disabled ? 0.5 : 1,
        cursor: disabled ? 'not-allowed' : 'pointer',
      }}
    >
      {providers.map((p) => <option key={p} value={p}>{p}</option>)}
      {!providers.includes(value) && <option value={value}>{value}</option>}
    </select>
  );
}

//  Confirm Dialog

export function ConfirmDialog({
  open,
  title,
  message,
  variant = 'danger',
  onConfirm,
  onCancel,
}: {
  open: boolean;
  title: string;
  message: string;
  variant?: 'danger' | 'warning';
  onConfirm: () => void;
  onCancel: () => void;
}) {
  if (!open) return null;

  const accent = variant === 'danger' ? '#b78383' : '#d4a843';
  const accentDim = variant === 'danger' ? 'rgba(183,131,131,0.12)' : 'rgba(212,168,67,0.12)';
  const border = variant === 'danger' ? 'rgba(183,131,131,0.3)' : 'rgba(212,168,67,0.3)';

  return (
    <div
      className="fixed inset-0 z-500 flex items-center justify-center"
      style={{ backgroundColor: 'rgba(15, 12, 8, 0.7)', backdropFilter: 'blur(4px)' }}
      onClick={onCancel}
      onKeyDown={(e) => e.key === 'Escape' && onCancel()}
      role="button"
      tabIndex={0}
    >
      <div
        className="w-full max-w-md rounded-xl overflow-hidden animate-fade-in-up"
        style={{
          background: 'var(--pr-surface)',
          border: `1px solid ${border}`,
          boxShadow: `0 24px 60px rgba(0,0,0,0.35), 0 0 0 1px ${border}`,
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          className="flex items-center gap-3 px-6 py-4 border-b"
          style={{ borderColor: 'var(--pr-border)' }}
        >
          <div
            className="w-8 h-8 rounded-lg flex items-center justify-center flex-0"
            style={{ background: accentDim, border: `1px solid ${border}` }}
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke={accent} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
              <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
              <line x1="12" y1="9" x2="12" y2="13" />
              <line x1="12" y1="17" x2="12.01" y2="17" />
            </svg>
          </div>
          <h3
            style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)', fontSize: 15, fontWeight: 600 }}
          >
            {title}
          </h3>
        </div>

        {/* Body */}
        <div className="px-6 py-5">
          <p
            style={{
              fontFamily: 'var(--pr-font-mono)',
              color: 'var(--pr-text-dim)',
              fontSize: 12,
              lineHeight: 1.6,
            }}
          >
            {message}
          </p>
        </div>

        {/* Footer */}
        <div
          className="flex items-center justify-end gap-3 px-6 py-4 border-t"
          style={{ borderColor: 'var(--pr-border)', background: 'var(--pr-bg)' }}
        >
          <button
            onClick={onCancel}
            className="px-4 py-2 rounded-lg transition-colors cursor-pointer"
            style={{
              fontFamily: 'var(--pr-font-mono)',
              fontSize: 12,
              color: 'var(--pr-text-muted)',
              background: 'transparent',
              border: '1px solid var(--pr-border)',
            }}
          >
            cancel
          </button>
          <button
            onClick={onConfirm}
            className="px-4 py-2 rounded-lg transition-all cursor-pointer"
            style={{
              fontFamily: 'var(--pr-font-mono)',
              fontSize: 12,
              color: '#ffffff',
              background: accent,
              border: 'none',
              boxShadow: `0 0 12px ${accent}30`,
            }}
          >
            confirm
          </button>
        </div>
      </div>
    </div>
  );
}

//  Servers Section
