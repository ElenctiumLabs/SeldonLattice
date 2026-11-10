import { useState, useCallback, useEffect, lazy } from 'react';
import Header from './components/Header';
import Settings from './components/Settings';
import { ErrorBoundary } from './components/ErrorBoundary';
import NotificationToast from './components/NotificationToast';
import NotificationCenter from './components/NotificationCenter';
import OnboardingTour, { useOnboarding } from './components/OnboardingTour';
import SessionManager from './components/SessionManager';
import { useNotifications } from './hooks/useNotifications';
import { useEngineEnabled } from './hooks/useServerStatus';
import {
  getSettingsStatus,
  type Framework,
} from './lib/api/settingsApi';

const GraphViewSwitcher = lazy(() => import('./components/GraphViewSwitcher'));
const CitationsPanel = lazy(() => import('./components/CitationsPanel'));

export type ModuleType = 'philosophy' | 'research' | 'custom';

// ChatMessage kept as exported type for downstream consumers
export interface ChatMessage {
  id: string;
  role: 'user' | 'system' | 'proposer' | 'skeptic' | 'mediator';
  content: string;
  timestamp: Date;
}

//  Framework metadata 

const FRAMEWORK_META: Record<string, { title: string; desc: string; agents: string; colour: string }> = {
  philosophy: {
    title: 'Philosophical Reasoning',
    desc: 'The proposing agent and the objecting agent debate under formal logic, mediated and moderated by the Mediator.',
    agents: 'Proposer · Skeptic · Mediator',
    colour: '#11b880',
  },
  research: {
    title: 'Research Orchestration',
    desc: 'Chains the philosophy module with any custom modules created at runtime for multi-pass scholarship with evidence chains.',
    agents: 'Philosophy + custom modules (orchestrated)',
    colour: '#4c89ff',
  },
};

//  App 

export default function App() {
  const [activeModule, setActiveModule] = useState<ModuleType | null>(() =>
    sessionStorage.getItem('seldon-active-module') as ModuleType | null,
  );
  // No session is adopted at startup. The app opens on the framework home screen with
  // an empty canvas; a session is only loaded when the user picks one from the session
  // manager (which dispatches `reloadSession`). Restoring a stored session id here made
  // every launch and every faculty switch silently replay the last session touched.
  const [currentSessionId, setCurrentSessionId] = useState<string | null>(null);
  const [showSettings, setShowSettings] = useState(false);
  const [showNotifications, setShowNotifications] = useState(false);
  const [showSessionManager, setShowSessionManager] = useState(false);
  const [showCitations, setShowCitations] = useState(false);
  const engineEnabled = useEngineEnabled();

  // Gate: show Settings page before loading graph for unconfigured frameworks
  const [pendingModule, setPendingModule] = useState<ModuleType | null>(null);
  const [showSettingsGate, setShowSettingsGate] = useState(false);
  const [bootError, setBootError] = useState<string | null>(null);

  // Session lock: once a question has been submitted, framework switching is disabled
  const [sessionActive, setSessionActive] = useState(false);

  const { isOpen: onboardingOpen, shouldShow: showOnboarding, startTour, completeTour: completeOnboarding } = useOnboarding();
  const {
    notifications,
    toastQueue,
    unreadCount,
    addNotification,
    dismissToast,
    markAsRead,
    markAllRead,
    clearAll,
  } = useNotifications();

  //  Event listeners 

  useEffect(() => {
    const handler = () => {
      if (sessionActive) {
        addNotification('warning', 'Session Active', 'Session manager cannot be opened while a session is underway.');
        return;
      }
      // Opening the picker clears whatever is on the canvas. Sessions are shown only
      // once explicitly chosen, so leaving the previous one visible behind the list
      // would contradict what is about to be loaded.
      window.dispatchEvent(new CustomEvent('clearMindMap', { detail: { sessionId: '' } }));
      setCurrentSessionId(null);
      setShowSessionManager(true);
    };
    window.addEventListener('openSessionManager', handler);
    return () => window.removeEventListener('openSessionManager', handler);
  }, [sessionActive, addNotification]);

  useEffect(() => {
    const handler = () => setShowCitations(true);
    window.addEventListener('openCitationsPanel', handler as EventListener);
    return () => window.removeEventListener('openCitationsPanel', handler as EventListener);
  }, []);

  useEffect(() => {
    const handler = () => setShowNotifications(true);
    window.addEventListener('openNotifications', handler as EventListener);
    return () => window.removeEventListener('openNotifications', handler as EventListener);
  }, []);


  useEffect(() => {
    const handler = (e: Event) => {
      const ce = e as CustomEvent;
      const { type, title, message } = ce.detail ?? {};
      if (type && title && message) addNotification(type, title, message);
    };
    window.addEventListener('seldonNotification', handler);
    return () => window.removeEventListener('seldonNotification', handler);
  }, [addNotification]);

  // Listen for the first turn being submitted - locks framework switching and syncs session ID
  useEffect(() => {
    const handler = (e: Event) => {
      const ce = e as CustomEvent;
      if (ce.detail?.sessionId) {
        setCurrentSessionId(ce.detail.sessionId);
      }
      setSessionActive(true);
    };
    window.addEventListener('seldonSessionStarted', handler);
    return () => window.removeEventListener('seldonSessionStarted', handler);
  }, []);

  // Also treat any session reload as active and sync session ID (+ framework)
  useEffect(() => {
    const handler = (e: Event) => {
      const ce = e as CustomEvent;
      if (ce.detail?.sessionId) {
        setCurrentSessionId(ce.detail.sessionId);
      }
      if (ce.detail?.framework) {
        setActiveModule(ce.detail.framework as ModuleType);
        sessionStorage.setItem('seldon-active-module', ce.detail.framework);
      }
      setSessionActive(true);
    };
    window.addEventListener('reloadSession', handler);
    return () => window.removeEventListener('reloadSession', handler);
  }, []);

  // Listen for session completion to unlock framework switcher while retaining sessionId
  useEffect(() => {
    const handler = (e: Event) => {
      const ce = e as CustomEvent;
      if (ce.detail?.sessionId) {
        setCurrentSessionId(ce.detail.sessionId);
      }
      setSessionActive(false);
    };
    window.addEventListener('seldonSessionEnded', handler);
    return () => window.removeEventListener('seldonSessionEnded', handler);
  }, []);

  // Listen for session clearing to unlock framework switcher
  useEffect(() => {
    const handler = () => {
      setCurrentSessionId(null);
      setSessionActive(false);
    };
    window.addEventListener('seldonSessionCleared', handler);
    return () => window.removeEventListener('seldonSessionCleared', handler);
  }, []);

  // Listen for navigation home request to reset to home screen
  useEffect(() => {
    const handler = () => {
      // Reset active module and session state
      setActiveModule(null);
      sessionStorage.removeItem('seldon-active-module');
      setCurrentSessionId(null);
      setSessionActive(false);
    };
    window.addEventListener('seldonNavigateHome', handler);
    return () => window.removeEventListener('seldonNavigateHome', handler);
  }, []);

  //  Onboarding 

  // Open onboarding tour when it should be shown for the active module
  useEffect(() => {
    if (showOnboarding && activeModule) {
      startTour();
    }
  }, [showOnboarding, activeModule, startTour]);

  //  Session handling 

  const handleSessionSelect = useCallback((session: { sessionId: string; rootQuestion: string; maxLevel: number; framework?: string }) => {
    setCurrentSessionId(session.sessionId);
    setSessionActive(true);

    // Sessions are framework-scoped - switching to a session from another
    // framework must switch the active module so the graph and streaming
    // endpoints agree with the session's origin.
    const framework = (session.framework || 'philosophy') as ModuleType;
    setActiveModule(framework);
    sessionStorage.setItem('seldon-active-module', framework);

    window.dispatchEvent(new CustomEvent('reloadSession', {
      detail: { sessionId: session.sessionId, rootQuestion: session.rootQuestion, maxLevel: session.maxLevel, framework },
    }));
  }, []);

  //  Framework selection with settings gate 

  const handleSelectModule = useCallback(async (mod: ModuleType) => {
    if (sessionActive) {
      addNotification('warning', 'Session Active', 'End the current session before switching frameworks.');
      return;
    }

    // Check if this framework has been configured before. A failure here is loud:
    // there is no "proceed with defaults" path.
    try {
      const status = await getSettingsStatus(mod as Framework);
      if (!status.isConfigured) {
        // First visit: show settings gate
        setPendingModule(mod);
        setShowSettingsGate(true);
        return;
      }
    } catch (error) {
      setBootError(error instanceof Error ? error.message : String(error));
      return;
    }

    setActiveModule(mod);
    sessionStorage.setItem('seldon-active-module', mod);
    setShowSettings(false);
  }, [sessionActive, addNotification]);

  const handleSaveAndBegin = useCallback(() => {
    if (!pendingModule) return;
    setActiveModule(pendingModule);
    sessionStorage.setItem('seldon-active-module', pendingModule);
    setPendingModule(null);
    setShowSettingsGate(false);
  }, [pendingModule]);

  const handleCloseGate = useCallback(() => {
    setPendingModule(null);
    setShowSettingsGate(false);
  }, []);

  const handleOpenSettings = useCallback(() => setShowSettings(true), []);
  const handleCloseSettings = useCallback(() => setShowSettings(false), []);

  //  Loud boot failure: settings are missing or invalid. No default path.

  if (bootError) {
    return (
      <div className="flex items-center justify-center h-dvh p-6" style={{ background: '#080a0f', color: '#f1f5f9' }}>
        <div className="max-w-2xl w-full rounded-xl p-6" style={{ border: '1px solid rgba(224,122,95,0.45)', background: 'rgba(185,90,58,0.10)' }}>
          <h1 className="text-[16px] font-mono uppercase tracking-widest mb-3" style={{ color: '#e07a5f' }}>
            Settings unavailable
          </h1>
          <p className="text-[12px] font-mono mb-4" style={{ color: '#e07a5f' }}>
            The application will not run on defaults. Fix the settings row or restart so first-run seeding can create it.
          </p>
          <pre className="text-[12px] font-mono whitespace-pre-wrap mb-5" style={{ color: '#f1f5f9' }}>
            {bootError}
          </pre>
          <button
            onClick={() => { setBootError(null); window.location.reload(); }}
            className="px-4 py-2 rounded text-[12px] font-mono uppercase tracking-wider cursor-pointer"
            style={{ background: '#e07a5f', color: '#080a0f' }}
          >
            Reload
          </button>
        </div>
      </div>
    );
  }


  //  Settings gate overlay 

  if (showSettingsGate && pendingModule) {
    return (
      <ErrorBoundary label="SettingsGate" onError={(e) => console.error('[SettingsGate Error]', e)}>
        <Settings
          onBack={handleCloseGate}
          activeModule={pendingModule}
          sessionActive={false}
          gateMode
          onSaveAndBegin={handleSaveAndBegin}
        />
      </ErrorBoundary>
    );
  }

  //  Home screen (no framework selected) 

  if (!activeModule) {
    return (
      <div
        className="h-dvh p-8 relative overflow-y-auto"
        style={{ background: 'var(--pr-bg)', color: 'var(--pr-text)' }}
      >
        {/* Ambient background radials */}
        <div
          className="pointer-events-none absolute inset-0"
          style={{
            background:
              'radial-gradient(ellipse 70% 50% at 50% -10%, rgba(251,191,36,0.06) 0%, transparent 70%), ' +
              'radial-gradient(ellipse 50% 40% at 80% 100%, rgba(99,102,241,0.05) 0%, transparent 60%), ' +
              'radial-gradient(ellipse 40% 30% at 10% 80%, rgba(16,185,129,0.04) 0%, transparent 60%)',
          }}
        />

        <div className="max-w-4xl mx-auto text-center animate-fade-in-up relative z-10 py-8">
          {/*  Sigil Logo  */}
          <div className="relative w-20 h-20 mx-auto mb-8">
            {/* Outer glow ring */}
            <div
              className="absolute inset-0 rounded-full"
              style={{
                background: 'radial-gradient(circle, rgba(251,191,36,0.2) 0%, transparent 70%)',
                animation: 'slow-blink 4s ease-in-out infinite',
                transform: 'scale(1.6)',
              }}
            />
            {/* Gold square backing */}
            <div
              className="absolute inset-0 rounded-md"
              style={{
                background: 'linear-gradient(135deg, var(--pr-role-proposer) 0%, var(--pr-role-proposer-soft) 100%)',
                boxShadow: '0 0 24px rgba(120,89,26,0.3), inset 0 1px 0 rgba(255,255,255,0.3)',
              }}
            />
            {/* SVG sigil */}
            <svg
              className="relative w-20 h-20 p-4"
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

          {/*  Title  */}
          <h1
            className="mb-3 leading-none tracking-wide"
            style={{
              fontFamily: 'var(--pr-font-display)',
              fontSize: 'clamp(2.4rem, 5vw, 3.6rem)',
              fontWeight: 600,
              background: 'linear-gradient(135deg, var(--pr-role-proposer) 0%, var(--pr-role-proposer-soft) 40%, var(--pr-role-proposer) 75%, var(--pr-role-proposer-soft) 100%)',
              backgroundSize: '200% auto',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
              backgroundClip: 'text',
              animation: 'shimmer 5s linear infinite',
            }}
          >
            Seldon Lattice
          </h1>

          {/*  Subtitle  */}
          <p
            className="mb-14 uppercase tracking-[0.14em]"
            style={{
              fontFamily: "'Cardo', serif",
              fontSize: '0.92rem',
              fontWeight: 600,
              color: 'var(--pr-text-muted)',
            }}
          >
            Select Your Dialectical Framework
          </p>

          {/*  Session-lock warning  */}
          {sessionActive && (
            <div
              className="mb-8 px-5 py-3 rounded-lg border text-sm mx-auto"
              style={{
                fontFamily: 'var(--pr-font-mono)',
                borderColor: 'var(--pr-role-proposer)',
                background: 'var(--pr-surface-raised)',
                color: 'var(--pr-role-proposer)',
                maxWidth: 520,
                backdropFilter: 'blur(8px)',
              }}
            >
              ⚠   A session is active - framework switching is locked.
            </div>
          )}

          {/*  Framework Cards  */}
          <div className="flex flex-wrap justify-center gap-4">
            {(['philosophy', 'research'] as const).map((mod, idx) => {
              const meta = FRAMEWORK_META[mod];
              const locked = sessionActive;
              return (
                <button
                  key={mod}
                  onClick={() => !locked && handleSelectModule(mod)}
                  disabled={locked}
                  className="flex flex-col items-start text-left relative overflow-hidden pr-glass-surface"
                  style={{
                    width: 'clamp(280px, 30%, 340px)',
                    padding: '24px 22px',
                    borderRadius: '10px',
                    border: '1px solid var(--pr-border)',
                    background: 'var(--pr-surface)',
                    boxShadow: 'var(--pr-card-shadow)',
                    cursor: locked ? 'not-allowed' : 'pointer',
                    opacity: locked ? 0.45 : 1,
                    transition: 'border-color 0.25s ease, box-shadow 0.25s ease, background 0.25s ease, transform 0.2s ease',
                    animation: `fadeInUp 0.45s ease-out ${idx * 0.06}s both`,
                  }}
                  onMouseEnter={(e) => {
                    if (!locked) {
                      const el = e.currentTarget as HTMLElement;
                      el.style.borderColor = meta.colour + '70';
                      el.style.background = 'var(--pr-surface-raised)';
                      el.style.boxShadow = `0 0 30px ${meta.colour}20, var(--pr-card-shadow)`;
                      el.style.transform = 'translateY(-2px)';
                    }
                  }}
                  onMouseLeave={(e) => {
                    const el = e.currentTarget as HTMLElement;
                    el.style.borderColor = 'var(--pr-border)';
                    el.style.background = 'var(--pr-surface)';
                    el.style.boxShadow = 'var(--pr-card-shadow)';
                    el.style.transform = 'translateY(0)';
                  }}
                >
                  {/* Colour orb */}
                  <div
                    className="mb-5 rounded-full"
                    style={{
                      width: 10,
                      height: 10,
                      background: meta.colour,
                      boxShadow: `0 0 10px ${meta.colour}99, 0 0 20px ${meta.colour}40`,
                    }}
                  />

                  {/* Card title */}
                  <h2
                    className="mb-2 leading-snug"
                    style={{
                      fontFamily: 'var(--pr-font-display)',
                      fontSize: '1.2rem',
                      fontWeight: 600,
                      color: 'var(--pr-text)',
                    }}
                  >
                    {meta.title}
                  </h2>

                  {/* Description - EB Garamond for elegance */}
                  <p
                    className="mb-5 leading-relaxed"
                    style={{
                      fontFamily: 'var(--pr-font-body)',
                      fontSize: '1.02rem',
                      fontWeight: 500,
                      color: 'var(--pr-text-muted)',
                      flexGrow: 1,
                    }}
                  >
                    {meta.desc}
                  </p>

                  {/* Agents badge */}
                  <span
                    className="mt-auto block w-full"
                    style={{
                      fontFamily: 'var(--pr-font-mono)',
                      fontSize: '0.68rem',
                      letterSpacing: '0.04em',
                      color: meta.colour,
                      background: meta.colour + '18',
                      border: `1px solid ${meta.colour}35`,
                      borderRadius: '8px',
                      padding: '5px 10px',
                      whiteSpace: 'normal',
                      lineHeight: 1.6,
                      wordBreak: 'break-word',
                    }}
                  >
                    {meta.agents}
                  </span>
                </button>
              );
            })}
          </div>
        </div>

        {/* Shimmer keyframes (inline to avoid global pollution) */}
        <style>{`
          @keyframes shimmer {
            0%   { background-position: 0% center; }
            100% { background-position: 200% center; }
          }
        `}</style>
      </div>
    );
  }

  //  Main app view 

  return (
    <div className="flex flex-col h-dvh select-none overflow-hidden" style={{ background: 'var(--pr-bg)' }}>
      <Header
        currentSessionId={currentSessionId}
        onSessionSelect={handleSessionSelect}
        activeModule={activeModule}
        onOpenSettings={handleOpenSettings}
        onOpenNotifications={() => setShowNotifications(true)}
        unreadCount={unreadCount}
        sessionActive={sessionActive}
        engineEnabled={engineEnabled}
      />

      <div className="flex-1 overflow-hidden relative w-full h-full">
        <ErrorBoundary label="GraphView" onError={(e) => console.error('[GraphView Error]', e)}>
          <GraphViewSwitcher activeModule={activeModule} currentSessionId={currentSessionId} />
        </ErrorBoundary>
      </div>

      {/* Toast notifications */}
      <div className="fixed top-4 right-4 z-[100] flex flex-col gap-2">
        {toastQueue.map((n) => (
          <NotificationToast key={n.id} notification={n} onDismiss={dismissToast} />
        ))}
      </div>

      {/* Notification center panel */}
      {showNotifications && (
        <NotificationCenter
          notifications={notifications}
          onMarkAsRead={markAsRead}
          onMarkAllRead={markAllRead}
          onClearAll={clearAll}
          onClose={() => setShowNotifications(false)}
        />
      )}

      {/* Settings modal */}
      {showSettings && (
        <ErrorBoundary label="Settings" onError={(e) => console.error('[Settings Error]', e)}>
          <Settings
            onBack={handleCloseSettings}
            activeModule={activeModule ?? 'philosophy'}
            sessionActive={sessionActive}
          />
        </ErrorBoundary>
      )}

      {/* Onboarding tour */}
      <OnboardingTour
        isOpen={onboardingOpen}
        onClose={completeOnboarding}
        onComplete={completeOnboarding}
      />

      {/* Session Manager */}
      <SessionManager
        isVisible={showSessionManager}
        onClose={() => setShowSessionManager(false)}
        onSessionSelect={handleSessionSelect}
        currentSessionId={currentSessionId}
        activeModule={activeModule ?? 'philosophy'}
      />

      {/* Citations panel */}
      {showCitations && (
        <CitationsPanel onClose={() => setShowCitations(false)} />
      )}
    </div>
  );
}
