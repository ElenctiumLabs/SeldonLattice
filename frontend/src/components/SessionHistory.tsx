import React, { useState, useEffect, useMemo } from 'react';
import { api } from '../lib/api';
import { getConfig } from '../lib/config';
import type { Session } from '../lib/api';

interface SessionHistoryProps {
  onSessionSelect: (session: Session) => void;
  currentSessionId: string | null;
  disabled?: boolean;
}

export default function SessionHistory({ onSessionSelect, currentSessionId, disabled = false }: SessionHistoryProps) {
  const [sessions, setSessions] = useState<Session[]>([]);
  const [isOpen, setIsOpen] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [search, setSearch] = useState('');

  const fetchSessions = async () => {
    setIsLoading(true);
    try {
      const data = await api.listSessions();
      setSessions(data);
    } catch (e) {
      console.error('Failed to fetch sessions:', e);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (isOpen) {
      fetchSessions();
    }
  }, [isOpen]);

  const filteredSessions = useMemo(() => {
    if (!search.trim()) return sessions;
    const q = search.toLowerCase();
    return sessions.filter((s) => s.rootQuestion.toLowerCase().includes(q));
  }, [sessions, search]);

  const handleSessionClick = (session: Session) => {
    onSessionSelect(session);
    setIsOpen(false);
    setSearch('');
  };

  const handleDeleteSession = async (e: React.MouseEvent, sessionId: string) => {
    e.stopPropagation();
    if (!confirm('Archive this session? It will be retained for audit.')) return;

    try {
      await api.deleteSession(sessionId);
      setSessions(sessions.filter(s => s.sessionId !== sessionId));
    } catch (e) {
      console.error('Failed to delete session:', e);
    }
  };

  const formatDate = (dateString: string) => {
    const date = new Date(dateString);
    return date.toLocaleDateString() + ' ' + date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  };

  return (
    <div className="relative">
      <button
        disabled={disabled}
        onClick={() => !disabled && setIsOpen(!isOpen)}
        className="px-3 py-1.5 text-xs transition-colors rounded"
        title={disabled ? 'Cannot switch sessions while a session is underway' : 'Session History'}
        style={{
          fontFamily: 'var(--pr-font-mono)',
          color: disabled ? 'var(--pr-text-dim)' : 'var(--pr-text)',
          border: '1px solid var(--pr-border)',
          background: 'var(--pr-surface-raised)',
          opacity: disabled ? 0.4 : 1,
          cursor: disabled ? 'not-allowed' : 'pointer',
        }}
      >
        history
      </button>

      {isOpen && (
        <div
          className="absolute top-full right-0 mt-2 w-80 max-h-96 overflow-y-auto z-50"
          style={{
            background: 'var(--pr-surface)',
            border: '1px solid var(--pr-border)',
            borderRadius: 'var(--pr-radius)',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.3)',
          }}
        >
          <div className="p-3 border-b" style={{ borderColor: 'var(--pr-border)' }}>
            <h3
              className="text-sm font-medium mb-2"
              style={{ fontFamily: 'var(--pr-font-mono)', color: 'var(--pr-text)' }}
            >
              Session History
            </h3>
            {sessions.length > 0 && (
              <input
                type="text"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Search sessions..."
                className="w-full px-2 py-1.5 text-xs rounded outline-none"
                style={{
                  fontFamily: 'var(--pr-font-mono)',
                  background: 'var(--pr-bg)',
                  border: '1px solid var(--pr-border)',
                  color: 'var(--pr-text)',
                }}
              />
            )}
          </div>

          {isLoading ? (
            <div className="p-4 text-center" style={{ color: 'var(--pr-text-muted)' }}>
              Loading...
            </div>
          ) : filteredSessions.length === 0 ? (
            <div className="p-4 text-center" style={{ color: 'var(--pr-text-muted)' }}>
              {sessions.length === 0 ? 'No sessions yet' : 'No matching sessions'}
            </div>
          ) : (
            <div className="p-2">
              {filteredSessions.map((session) => {
                const isComplete = session.maxLevel >= getConfig().completionThreshold;
                const isCurrent = session.sessionId === currentSessionId;
                return (
                  <div
                    key={session.sessionId}
                    onClick={() => handleSessionClick(session)}
                    className="p-3 mb-2 cursor-pointer transition-colors hover:bg-opacity-10"
                    style={{
                      background: isCurrent ? 'rgba(184, 146, 74, 0.1)' : 'transparent',
                      border: isCurrent ? '1px solid var(--pr-role-question)' : '1px solid var(--pr-border)',
                      borderRadius: 'var(--pr-radius)',
                    }}
                  >
                    <div className="flex justify-between items-start mb-1">
                      <p
                        className="text-xs font-medium line-clamp-2 flex-1"
                        style={{ color: 'var(--pr-text)', fontFamily: 'var(--pr-font-mono)' }}
                      >
                        {session.rootQuestion}
                      </p>
                      <button
                        onClick={(e) => handleDeleteSession(e, session.sessionId)}
                        className="ml-2 text-xs opacity-50 hover:opacity-100 shrink-0"
                        style={{ color: 'var(--pr-text-muted)' }}
                      >
                        ×
                      </button>
                    </div>
                    <div className="flex justify-between items-center text-xs" style={{ color: 'var(--pr-text-muted)' }}>
                      <span>{formatDate(session.createdAt)}</span>
                      <div className="flex items-center gap-1.5">
                        <span>{session.framework}</span>
                        <span>L{session.maxLevel} · {session.nodeCount} nodes</span>
                        <span
                          className="text-[9px] px-1.5 py-0.5 rounded font-bold uppercase tracking-wider"
                          style={{
                            background: isComplete ? 'rgba(80,227,194,0.15)' : 'rgba(212,175,55,0.15)',
                            color: isComplete ? '#10b981' : '#f1f5f9',
                          }}
                        >
                          {isComplete ? 'Complete' : 'In Progress'}
                        </span>
                      </div>
                    </div>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
