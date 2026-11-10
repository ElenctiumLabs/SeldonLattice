import { useState, useEffect, useMemo } from 'react';
import { api } from '../lib/api';
import { getConfig } from '../lib/config';
import type { Session } from '../lib/api';
import type { ModuleType } from '../App';
import TemplateSelector, { type SessionTemplate } from './TemplateSelector';

interface SessionManagerProps {
  isVisible: boolean;
  onClose: () => void;
  onSessionSelect: (session: Session) => void;
  currentSessionId: string | null;
  activeModule: ModuleType;
}

type SessionWithStatus = Session;

export default function SessionManager({
  isVisible,
  onClose,
  onSessionSelect,
  currentSessionId,
  activeModule,
}: SessionManagerProps) {
  const [sessions, setSessions] = useState<SessionWithStatus[]>([]);
  const [archivedSessions, setArchivedSessions] = useState<SessionWithStatus[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [showArchived, setShowArchived] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [showTemplates, setShowTemplates] = useState(false);
  const [compareMode, setCompareMode] = useState(false);
  const [compareSessions, setCompareSessions] = useState<Set<string>>(new Set());

  useEffect(() => {
    if (isVisible) {
      fetchSessions();
      fetchArchived();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isVisible, activeModule]);

  // Sessions are framework-specific: only the current framework's sessions are shown.
  const fetchSessions = async () => {
    setIsLoading(true);
    try {
      const data = await api.listSessions({ framework: activeModule });
      setSessions(data);
    } catch (e) {
      console.error('Failed to fetch sessions:', e);
    } finally {
      setIsLoading(false);
    }
  };

  const fetchArchived = async () => {
    try {
      const data = await api.listSessions({ framework: activeModule, status: 'archived' });
      setArchivedSessions(data);
    } catch (e) {
      console.error('Failed to fetch archived sessions:', e);
    }
  };

  const reload = async () => {
    await Promise.all([fetchSessions(), fetchArchived()]);
  };

  // Archive (soft-delete): the row and its Merkle nodes are retained for audit.
  const handleDelete = async (e: React.MouseEvent, sessionId: string) => {
    e.stopPropagation();
    if (!confirm('Archive this session? It will be hidden from the active list but retained for audit.')) return;

    try {
      await api.deleteSession(sessionId);
      await reload();
    } catch (e) {
      console.error('Failed to archive session:', e);
      alert('Failed to archive session. Please try again.');
    }
  };

  const handleCreateFromTemplate = async (template: SessionTemplate) => {
    try {
      const resp = await api.createSession(template.rootQuestion, template.module);
      const newSession: Session = {
        sessionId: resp.sessionId,
        rootQuestion: template.rootQuestion,
        framework: template.module,
        createdAt: new Date().toISOString(),
        lastUpdatedAt: new Date().toISOString(),
        nodeCount: 0,
        maxLevel: 0,
        status: 'active',
      };
      setSessions((prev) => [newSession, ...prev]);
      setShowTemplates(false);
      onSessionSelect(newSession);
      onClose();
    } catch (e) {
      console.error('Failed to create session from template:', e);
      alert('Failed to create session. Please try again.');
    }
  };

  const toggleCompare = (sessionId: string) => {
    setCompareSessions((prev) => {
      const next = new Set(prev);
      if (next.has(sessionId)) {
        next.delete(sessionId);
      } else {
        next.add(sessionId);
      }
      return next;
    });
  };

  const clearComparison = () => {
    setCompareSessions(new Set());
    setCompareMode(false);
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  };

  const filteredSessions = useMemo(() => {
    const list = showArchived ? archivedSessions : sessions;
    if (!searchQuery.trim()) return list;
    const q = searchQuery.toLowerCase();
    return list.filter((s) => s.rootQuestion.toLowerCase().includes(q));
  }, [searchQuery, sessions, archivedSessions, showArchived]);

  const displayedSessions = compareMode
    ? sessions.filter((s) => compareSessions.has(s.sessionId))
    : filteredSessions;

  if (!isVisible) return null;

  return (
    <div
      className="fixed inset-0 z-300 flex items-start justify-end"
      style={{ backgroundColor: 'rgba(15, 12, 8, 0.45)', backdropFilter: 'blur(4px)' }}
      onClick={onClose}
    >
      <div
        className="w-full max-w-2xl h-full flex flex-col animate-fade-in-up pr-glass-surface"
        style={{
          backgroundColor: 'var(--pr-surface)',
          borderLeft: '1px solid var(--pr-border)',
          boxShadow: '-12px 0 40px rgba(0,0,0,0.2)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          className="px-6 py-4 border-b flex items-center justify-between"
          style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}
        >
          <div>
            <h2 className="text-xl font-semibold" style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)' }}>
              Session Manager
            </h2>
            <p className="text-xs font-mono mt-0.5" style={{ color: 'var(--pr-text-muted)' }}>
              {showArchived ? 'Archived sessions' : 'Active sessions'} • {filteredSessions.length} shown
            </p>
          </div>
          <div className="flex items-center gap-2">
            {compareMode && (
              <button
                onClick={clearComparison}
                className="text-[11px] font-mono px-2.5 py-1 rounded cursor-pointer transition-colors"
                style={{ backgroundColor: 'var(--pr-surface)', border: '1px solid var(--pr-border)', color: 'var(--pr-text)' }}
              >
                Clear Compare
              </button>
            )}
            <button
              onClick={onClose}
              className="w-8 h-8 rounded flex items-center justify-center cursor-pointer text-lg font-mono"
              style={{ backgroundColor: 'var(--pr-surface)', border: '1px solid var(--pr-border)', color: 'var(--pr-text-dim)' }}
            >
              ×
            </button>
          </div>
        </div>

        {/* Controls */}
        <div className="px-6 py-3.5 border-b" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)' }}>
          <div className="flex items-center gap-3 mb-3">
            <input
              type="text"
              placeholder="Search sessions..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="flex-1 px-3 py-2 rounded border text-[13px] font-mono"
              style={{
                backgroundColor: 'var(--pr-surface-raised)',
                borderColor: 'var(--pr-border)',
                color: 'var(--pr-text)',
              }}
            />
            <button
              onClick={() => setShowArchived(!showArchived)}
              className="px-3 py-2 rounded text-[12px] font-mono cursor-pointer transition-colors"
              style={{
                backgroundColor: showArchived ? 'var(--pr-role-judge)' : 'var(--pr-surface-raised)',
                color: showArchived ? '#ffffff' : 'var(--pr-text-muted)',
                border: '1px solid var(--pr-border)',
              }}
            >
              {showArchived ? 'Active' : 'Archived'}
            </button>
            <button
              onClick={() => setShowTemplates(true)}
              className="px-3 py-2 rounded text-[12px] font-mono font-semibold cursor-pointer transition-all"
              style={{
                backgroundColor: 'var(--pr-role-judge)',
                color: '#ffffff',
                border: 'none',
              }}
            >
              New from Template
            </button>
          </div>

          <div className="flex items-center gap-4 text-[11px] font-mono" style={{ color: 'var(--pr-text-dim)' }}>
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={compareMode}
                onChange={(e) => setCompareMode(e.target.checked)}
              />
              Compare mode
            </label>
            <span>{activeModule} module</span>
            <span>Active: {sessions.length}</span>
            <span>Archived: {archivedSessions.length}</span>
            {compareSessions.size > 0 && <span>Comparing: {compareSessions.size} sessions</span>}
          </div>
        </div>

        {/* Session List */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3" style={{ backgroundColor: 'var(--pr-bg)' }}>
          {isLoading ? (
            <div className="p-4 text-center font-mono text-sm" style={{ color: 'var(--pr-text-muted)' }}>
              Loading sessions...
            </div>
          ) : displayedSessions.length === 0 ? (
            <div className="p-4 text-center font-mono text-sm" style={{ color: 'var(--pr-text-dim)' }}>
              {searchQuery ? 'No matching sessions' : showArchived ? 'No archived sessions' : 'No active sessions'}
            </div>
          ) : (
            displayedSessions.map((session) => {
              const isCurrent = session.sessionId === currentSessionId;
              const level = session.maxLevel || 0;
              const isComplete = level >= getConfig().completionThreshold;
              const inCompare = compareSessions.has(session.sessionId);

              return (
                <div
                  key={session.sessionId}
                  className="p-3.5 rounded-lg border cursor-pointer transition-all"
                  style={{
                    backgroundColor: isCurrent ? 'var(--pr-surface-raised)' : 'var(--pr-surface)',
                    borderColor: isCurrent ? 'var(--pr-role-proposer)' : inCompare ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
                    borderLeft: `3px solid ${isCurrent ? 'var(--pr-role-proposer)' : level > 0 ? 'var(--pr-role-judge)' : 'var(--pr-border)'}`,
                  }}
                  onClick={() => {
                    if (compareMode) {
                      toggleCompare(session.sessionId);
                    } else {
                      onSessionSelect(session);
                      onClose();
                    }
                  }}
                >
                  <div className="flex items-start justify-between">
                    <div className="flex-1 min-w-0">
                      <p
                        className="text-[13px] font-mono line-clamp-2"
                        style={{ color: 'var(--pr-text)' }}
                      >
                        {session.rootQuestion}
                      </p>
                      <div
                        className="flex items-center gap-3 mt-2 text-[11px] font-mono"
                        style={{ color: 'var(--pr-text-muted)' }}
                      >
                        <span>ID: {session.sessionId.slice(0, 8)}...</span>
                        <span>· {session.framework}</span>
                        <span>· L{session.maxLevel}</span>
                        <span>· {session.nodeCount} nodes</span>
                        <span>· {formatDate(session.createdAt)}</span>
                      </div>
                    </div>

                    <div className="flex items-center gap-2 ml-2 shrink-0">
                      {compareMode && (
                        <input
                          type="checkbox"
                          checked={inCompare}
                          onChange={(e) => {
                            e.stopPropagation();
                            toggleCompare(session.sessionId);
                          }}
                          className="cursor-pointer"
                        />
                      )}
                      <span
                        className="text-[10px] px-1.5 py-0.5 rounded font-bold uppercase"
                        style={{
                          backgroundColor: isCurrent
                            ? 'rgba(120,89,26,0.15)'
                            : isComplete
                              ? 'rgba(4,99,64,0.15)'
                              : 'rgba(120,89,26,0.08)',
                          color: isCurrent
                            ? 'var(--pr-role-proposer)'
                            : isComplete
                              ? 'var(--pr-role-judge)'
                              : 'var(--pr-text-muted)',
                        }}
                      >
                        {isCurrent ? 'Current' : isComplete ? 'Done' : 'Active'}
                      </span>
                      {!isCurrent && !showArchived && (
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            handleDelete(e, session.sessionId);
                          }}
                          className="text-[14px] font-mono cursor-pointer opacity-50 hover:opacity-100"
                          style={{ color: 'var(--pr-role-question)' }}
                          title="Archive session"
                        >
                          ×
                        </button>
                      )}
                    </div>
                  </div>
                </div>
              );
            })
          )}
        </div>

        {/* Footer */}
        <div
          className="px-6 py-3.5 border-t flex items-center justify-between"
          style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}
        >
          <p className="text-[11px] font-mono" style={{ color: 'var(--pr-text-dim)' }}>
            {activeModule} framework - sessions are private to the current user
          </p>
          <button
            onClick={onClose}
            className="px-4 py-1.5 rounded text-[12px] font-mono uppercase font-semibold cursor-pointer transition-all"
            style={{
              backgroundColor: 'var(--pr-role-proposer)',
              color: 'var(--pr-bg)',
              border: 'none',
            }}
          >
            Close
          </button>
        </div>

        {/* Template Selector Modal */}
        {showTemplates && (
          <TemplateSelector
            onSelect={handleCreateFromTemplate}
            onClose={() => setShowTemplates(false)}
          />
        )}
      </div>
    </div>
  );
}