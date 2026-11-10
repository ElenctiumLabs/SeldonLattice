import { useState } from 'react';
import SessionHistory from './SessionHistory';
import { ExportDialog } from './ExportDialog';
import { useTheme } from '../hooks/useTheme';
import type { ModuleType } from '../App';

interface Session {
  sessionId: string;
  rootQuestion: string;
  createdAt: string;
  lastUpdatedAt: string | null;
  nodeCount: number;
  maxLevel: number;
}

interface HeaderProps {
  currentSessionId: string | null;
  onSessionSelect: (session: Session) => void;
  activeModule?: ModuleType | null;
  onOpenSettings?: () => void;
  onOpenNotifications?: () => void;
  unreadCount?: number;
  sessionActive?: boolean;
  engineEnabled?: boolean;
}

export default function Header({
  currentSessionId,
  onSessionSelect,
  activeModule,
  onOpenSettings,
  onOpenNotifications,
  unreadCount = 0,
  sessionActive = false,
  engineEnabled = true,
}: HeaderProps) {
  const [menuOpen, setMenuOpen] = useState(false);
  const [showExport, setShowExport] = useState(false);
  const { theme, toggleTheme } = useTheme();

  // Export reads the persisted live turns at render time so the dialog always
  // reflects the latest streamed state. The reported Theme is the ROOT question
  // (the level-0 Question turn), never the latest level thesis - matching how
  // the THESIS bar anchors the page.
  const liveTurnsForExport: Parameters<typeof ExportDialog>[0]['liveTurns'] = (() => {
    try {
      return JSON.parse(localStorage.getItem('seldon_live_turns') ?? '[]');
    } catch {
      return [];
    }
  })();
  const rootQuestionForExport: string | null =
    liveTurnsForExport.find(
      (t) => t.speaker.toLowerCase() === 'question' && (t.level ?? 0) === 0,
    )?.content ?? null;

  // Per-module accent colours (match FRAMEWORK_META in App.tsx)
  const MODULE_COLOURS: Record<string, string> = {
    philosophy:            '#11b880',
    research:              '#4c89ff',
    custom:                '#fbbf24',
  };

  const MODULE_LABELS: Record<string, string> = {
    philosophy:            'Philosophical Reasoning',
    research:              'Research Orchestration',
    custom:                'Custom',
  };

  const moduleColour = activeModule ? (MODULE_COLOURS[activeModule] ?? '#fbbf24') : null;

  return (
    <>
    <header
      className="pr-header flex items-center justify-between px-4 md:px-6 py-3 select-none pr-glass-surface z-50"
      style={{
        background: 'var(--pr-surface)',
        borderBottom: moduleColour
          ? `1px solid ${moduleColour}55`
          : '1px solid var(--pr-border)',
        boxShadow: moduleColour
          ? `0 1px 0 ${moduleColour}30, 0 2px 8px ${moduleColour}10`
          : '0 1px 3px rgba(0,0,0,0.04)',
      }}
    >
      {/* Left logo & title */}
      <div className="flex items-center gap-3">
        {/* Refined sigil logo */}
        <div className="relative w-8 h-8 flex-0">
          <div
            className="absolute inset-0 rounded"
            style={{
              background: 'linear-gradient(135deg, var(--pr-role-proposer) 0%, var(--pr-role-proposer-soft) 100%)',
              boxShadow: '0 0 12px rgba(120,89,26,0.25), inset 0 1px 0 rgba(255,255,255,0.25)',
            }}
          />
          <svg
            className="relative w-8 h-8 p-1.5"
            viewBox="0 0 24 24"
            fill="none"
            stroke="var(--pr-bg)"
            strokeWidth="1.5"
            strokeLinecap="round"
          >
            <circle cx="12" cy="12" r="9" />
            <path d="M12 5v14M5 12h14" />
            <circle cx="12" cy="12" r="3" fill="var(--pr-bg)" />
          </svg>
        </div>

        <div>
          {/* Cormorant Garamond display title */}
          <h1
            style={{
              fontFamily: 'var(--pr-font-display)',
              fontSize: '1.3rem',
              fontWeight: 1000,
              lineHeight: 0.8,
              letterSpacing: '0.02em',
              color: 'var(--pr-role-proposer)',
            }}
          >
            Seldon Lattice
          </h1>
          <div className="flex items-center gap-2 mt-1">
            {/* Cardo small-caps subtitle with sturdy stems */}
            <p
              style={{
                fontFamily: "'Cardo', serif",
                fontSize: '0.80rem',
                fontWeight: 800,
                letterSpacing: '0.02em',
                textTransform: 'uppercase',
                color: 'var(--pr-text-muted)',
              }}
            >
              Dialectical Reasoning Engine
            </p>
            {activeModule && (
              <>
                <span style={{ fontSize: '1rem', color: 'var(--pr-border-strong)' }}>/</span>
                <span
                  className="px-2 py-0.3 rounded-full text-[12px] font-mono font-semibold tracking-wider uppercase"
                  style={{
                    color: moduleColour ?? 'var(--pr-role-proposer-soft)',
                    background: moduleColour ? `${moduleColour}18` : 'transparent',
                    border: `0.8px solid ${moduleColour ?? 'transparent'}20`,
                    letterSpacing: '0.05em',
                    whiteSpace: 'nowrap',
                  }}
                >
                  {MODULE_LABELS[activeModule] ?? activeModule}
                </span>
              </>
            )}
          </div>
        </div>
      </div>

      {/* Desktop: actions, history, theme toggle, settings */}
      <div className="hidden md:flex items-center gap-2.5">
        {/* Sol / Luna Theme Toggle */}
        <button
          onClick={toggleTheme}
          className="flex items-center gap-1.5 px-3 h-10 rounded border transition-all cursor-pointer font-mono text-[11px] font-semibold tracking-wider uppercase"
          style={{
            borderColor: 'var(--pr-border)',
            background: 'var(--pr-surface-raised)',
            color: 'var(--pr-text)',
          }}
          title={theme === 'dark' ? 'Switch to Sol (Light Mode)' : 'Switch to Nigredo (Dark Mode)'}
        >
          {theme === 'dark' ? (
            <>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <circle cx="12" cy="12" r="5" />
                <line x1="12" y1="1" x2="12" y2="3" />
                <line x1="12" y1="21" x2="12" y2="23" />
                <line x1="4.22" y1="4.22" x2="5.64" y2="5.64" />
                <line x1="18.36" y1="18.36" x2="19.78" y2="19.78" />
                <line x1="1" y1="12" x2="3" y2="12" />
                <line x1="21" y1="12" x2="23" y2="12" />
                <line x1="4.22" y1="19.78" x2="5.64" y2="18.36" />
                <line x1="18.36" y1="5.64" x2="19.78" y2="4.22" />
              </svg>
              <span>SOL</span>
            </>
          ) : (
            <>
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
              </svg>
              <span>LUNA</span>
            </>
          )}
        </button>

        <button
          onClick={() => setShowExport(true)}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors cursor-pointer"
          style={{
            borderColor: 'var(--pr-border)',
            background: 'var(--pr-surface-raised)',
            color: 'var(--pr-text)',
          }}
          title="Export"
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3" />
          </svg>
        </button>
        <button
          disabled={sessionActive}
          onClick={() => !sessionActive && window.dispatchEvent(new CustomEvent('openAgentPanel'))}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors"
          style={{
            borderColor: 'var(--pr-border)',
            background: sessionActive ? 'transparent' : 'var(--pr-surface-raised)',
            color: sessionActive ? 'var(--pr-text-dim)' : 'var(--pr-text)',
            opacity: sessionActive ? 0.4 : 1,
            cursor: sessionActive ? 'not-allowed' : 'pointer',
          }}
          title={sessionActive ? 'Agent configuration is locked while a session is underway' : 'Agent Configuration'}
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <path d="M12 2a10 10 0 0110 10v4a2 2 0 01-4 0v-4a6 6 0 00-12 0v4a2 2 0 01-4 0V12A10 10 0 0112 2z" />
            <path d="M12 12v8M9 15h6v6M9 9h6V3" />
          </svg>
        </button>
        <button
          onClick={() => window.dispatchEvent(new CustomEvent('openCitationsPanel'))}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors cursor-pointer"
          style={{
            borderColor: 'var(--pr-border)',
            background: 'var(--pr-surface-raised)',
            color: 'var(--pr-text)',
          }}
          title="Citations"
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <path d="M21 6v12a2 2 0 01-2 2H5a2 2 0 01-2-2V6" />
            <path d="M7 6v12M17 6v12" />
          </svg>
        </button>
        <button
          onClick={() => window.dispatchEvent(new CustomEvent('openNotifications'))}
          className="relative flex items-center justify-center w-10 h-10 rounded border transition-colors cursor-pointer"
          style={{
            borderColor: 'var(--pr-border)',
            background: 'var(--pr-surface-raised)',
            color: 'var(--pr-text)',
          }}
          title="Notifications"
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <path d="M18 8A6 6 0 006 8c0 7-3 9-3 9h18s-3-2-3-9M13.73 21a2 2 0 01-3.46 0" />
          </svg>
          {unreadCount > 0 && (
            <span
              className="absolute -top-1 -right-1 flex items-center justify-center min-w-4.5 h-4.5 px-1 rounded-full text-[10px] font-mono font-bold"
              style={{ backgroundColor: 'var(--pr-role-proposer)', color: 'var(--pr-bg)' }}
            >
              {unreadCount > 99 ? '99+' : unreadCount}
            </span>
          )}
        </button>
        <button
          disabled={sessionActive || !engineEnabled}
          onClick={() => { if (!sessionActive && engineEnabled) window.dispatchEvent(new CustomEvent('openSessionManager')); }}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors"
          style={{
            borderColor: 'var(--pr-border)',
            background: (sessionActive || !engineEnabled) ? 'transparent' : 'var(--pr-surface-raised)',
            color: (sessionActive || !engineEnabled) ? 'var(--pr-text-dim)' : 'var(--pr-text)',
            opacity: (sessionActive || !engineEnabled) ? 0.4 : 1,
            cursor: (sessionActive || !engineEnabled) ? 'not-allowed' : 'pointer',
          }}
          title={!engineEnabled ? 'Start the LLM servers first' : sessionActive ? 'Session manager is disabled while a session is underway' : 'Session Manager'}
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <rect x="2" y="7" width="20" height="14" rx="2" />
            <path d="M16 3h2a2 2 0 012 2v0a2 2 0 01-2 2h-2M10 3H8a2 2 0 00-2 2v0a2 2 0 002 2h2M16 3h2a2 2 0 012 2v0a2 2 0 01-2 2h-2" />
            <path d="M10 3H8a2 2 0 00-2 2v0a2 2 0 002 2h2" />
          </svg>
        </button>
        <button
          disabled={sessionActive}
          onClick={() => !sessionActive && window.dispatchEvent(new CustomEvent('seldonNavigateHome'))}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors"
          style={{
            borderColor: 'var(--pr-border)',
            background: sessionActive ? 'transparent' : 'var(--pr-surface-raised)',
            color: sessionActive ? 'var(--pr-text-dim)' : 'var(--pr-text)',
            opacity: sessionActive ? 0.4 : 1,
            cursor: sessionActive ? 'not-allowed' : 'pointer',
          }}
          title={sessionActive ? 'Stop current session before returning Home' : 'Home'}
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <path d="M3 12l9-9 9 9" />
            <path d="M9 21V9h6v12" />
          </svg>
        </button>
        <SessionHistory currentSessionId={currentSessionId} onSessionSelect={onSessionSelect} disabled={sessionActive} />
        <button
          disabled={sessionActive}
          onClick={() => !sessionActive && onOpenSettings?.()}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors"
          style={{
            borderColor: 'var(--pr-border)',
            background: sessionActive ? 'transparent' : 'var(--pr-surface-raised)',
            color: sessionActive ? 'var(--pr-text-dim)' : 'var(--pr-text)',
            opacity: sessionActive ? 0.4 : 1,
            cursor: sessionActive ? 'not-allowed' : 'pointer',
          }}
          title={sessionActive ? 'Settings cannot be changed while a session is underway' : 'Settings'}
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z" />
          </svg>
        </button>
      </div>

      {/* Mobile: hamburger menu & theme toggle */}
      <div className="flex md:hidden items-center gap-2">
        <button
          onClick={toggleTheme}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors cursor-pointer"
          style={{
            borderColor: 'var(--pr-border)',
            background: 'var(--pr-surface-raised)',
            color: 'var(--pr-text)',
          }}
          title={theme === 'dark' ? 'Switch to Sol' : 'Switch to Luna'}
        >
          {theme === 'dark' ? (
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <circle cx="12" cy="12" r="5" />
              <line x1="12" y1="1" x2="12" y2="3" />
              <line x1="12" y1="21" x2="12" y2="23" />
            </svg>
          ) : (
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z" />
            </svg>
          )}
        </button>
        <SessionHistory currentSessionId={currentSessionId} onSessionSelect={onSessionSelect} disabled={sessionActive} />
        <button
          onClick={() => setMenuOpen(!menuOpen)}
          className="flex items-center justify-center w-10 h-10 rounded border transition-colors cursor-pointer"
          style={{
            borderColor: 'var(--pr-border)',
            background: 'var(--pr-surface-raised)',
            color: 'var(--pr-text)',
          }}
          title="Menu"
        >
          <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
            {menuOpen ? (
              <path d="M18 6L6 18M6 6l12 12" />
            ) : (
              <path d="M3 12h18M3 6h18M3 18h18" />
            )}
          </svg>
        </button>
      </div>

      {/* Mobile dropdown menu */}
      {menuOpen && (
        <div
          className="absolute top-full right-0 left-0 z-50 p-4 md:hidden border-b"
          style={{
            background: 'var(--pr-surface)',
            borderColor: 'var(--pr-border)',
            boxShadow: '0 8px 24px rgba(0,0,0,0.12)',
          }}
        >
          <div className="space-y-3">
            <button
              disabled={sessionActive}
              onClick={() => { if (!sessionActive) { onOpenSettings?.(); setMenuOpen(false); } }}
              className="w-full flex items-center gap-2 px-3 py-2.5 rounded border transition-colors text-[13px] font-mono"
              style={{
                borderColor: 'var(--pr-border)',
                background: sessionActive ? 'transparent' : 'var(--pr-surface-raised)',
                color: sessionActive ? 'var(--pr-text-dim)' : 'var(--pr-text)',
                opacity: sessionActive ? 0.4 : 1,
                cursor: sessionActive ? 'not-allowed' : 'pointer',
              }}
              title={sessionActive ? 'Settings cannot be changed while a session is underway' : 'Settings'}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                <circle cx="12" cy="12" r="3" />
                <path d="M19.4 15a1.65 1.65 0 00.33 1.82l.06.06a2 2 0 010 2.83 2 2 0 01-2.83 0l-.06-.06a1.65 1.65 0 00-1.82-.33 1.65 1.65 0 00-1 1.51V21a2 2 0 01-4 0v-.09A1.65 1.65 0 009 19.4a1.65 1.65 0 00-1.82.33l-.06.06a2 2 0 01-2.83-2.83l.06-.06A1.65 1.65 0 004.68 15a1.65 1.65 0 00-1.51-1H3a2 2 0 010-4h.09A1.65 1.65 0 004.6 9a1.65 1.65 0 00-.33-1.82l-.06-.06a2 2 0 012.83-2.83l.06.06A1.65 1.65 0 009 4.68a1.65 1.65 0 001-1.51V3a2 2 0 014 0v.09a1.65 1.65 0 001 1.51 1.65 1.65 0 001.82-.33l.06-.06a2 2 0 012.83 2.83l-.06.06A1.65 1.65 0 0019.4 9a1.65 1.65 0 001.51 1H21a2 2 0 010 4h-.09a1.65 1.65 0 00-1.51 1z" />
              </svg>
              Settings
            </button>
            <button
              onClick={() => { setShowExport(true); setMenuOpen(false); }}
              className="w-full flex items-center gap-2 px-3 py-2.5 rounded border transition-colors cursor-pointer text-[13px] font-mono"
              style={{
                borderColor: 'var(--pr-border)',
                background: 'var(--pr-surface-raised)',
                color: 'var(--pr-text)',
              }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3" />
              </svg>
              Export
            </button>
            <button
              onClick={() => { onOpenNotifications?.(); setMenuOpen(false); }}
              className="w-full flex items-center gap-2 px-3 py-2.5 rounded border transition-colors cursor-pointer text-[13px] font-mono"
              style={{
                borderColor: 'var(--pr-border)',
                background: 'var(--pr-surface-raised)',
                color: 'var(--pr-text)',
              }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                <path d="M18 8A6 6 0 006 8c0 7-3 9-3 9h18s-3-2-3-9M13.73 21a2 2 0 01-3.46 0" />
              </svg>
              Notifications
              {unreadCount > 0 && (
                <span
                  className="ml-auto flex items-center justify-center min-w-4.5 h-4.5 px-1 rounded-full text-[10px] font-mono font-bold"
                  style={{ backgroundColor: 'var(--pr-role-proposer)', color: 'var(--pr-bg)' }}
                >
                  {unreadCount > 99 ? '99+' : unreadCount}
                </span>
              )}
            </button>
            <button
              disabled={sessionActive || !engineEnabled}
              onClick={() => { if (!sessionActive && engineEnabled) { window.dispatchEvent(new CustomEvent('openSessionManager')); setMenuOpen(false); } }}
              className="w-full flex items-center gap-2 px-3 py-2.5 rounded border transition-colors text-[13px] font-mono"
              style={{
                borderColor: 'var(--pr-border)',
                background: (sessionActive || !engineEnabled) ? 'transparent' : 'var(--pr-surface-raised)',
                color: (sessionActive || !engineEnabled) ? 'var(--pr-text-dim)' : 'var(--pr-text)',
                opacity: (sessionActive || !engineEnabled) ? 0.4 : 1,
                cursor: (sessionActive || !engineEnabled) ? 'not-allowed' : 'pointer',
              }}
              title={!engineEnabled ? 'Start the LLM servers first' : sessionActive ? 'Session manager is disabled while a session is underway' : 'Session Manager'}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5">
                <rect x="2" y="7" width="20" height="14" rx="2" />
                <path d="M16 3h2a2 2 0 012 2v0a2 2 0 01-2 2h-2M10 3H8a2 2 0 00-2 2v0a2 2 0 002 2h2M16 3h2a2 2 0 012 2v0a2 2 0 01-2 2h-2" />
                <path d="M10 3H8a2 2 0 00-2 2v0a2 2 0 002 2h2" />
              </svg>
              Session Manager
            </button>
          </div>
        </div>
      )}
    </header>

    {showExport && (
      <ExportDialog
        isOpen={showExport}
        onClose={() => setShowExport(false)}
        sessionId={currentSessionId ?? (() => { try { return localStorage.getItem('seldon_active_session_id'); } catch { return null; } })()}
        theme={rootQuestionForExport ?? localStorage.getItem('seldon_active_theme') ?? ''}
        liveTurns={liveTurnsForExport}
      />
    )}
    </>
  );
}

