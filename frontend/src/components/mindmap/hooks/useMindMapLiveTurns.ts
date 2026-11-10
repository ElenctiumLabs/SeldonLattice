import { useCallback, useRef, useState } from 'react';
import type { LiveTurn } from '../../../types/liveTurn';
import { createDebouncedPersist } from '../../../lib/persistDebounced';

export const STORAGE_KEY_LIVE_TURNS = 'seldon_live_turns';

export function readPersistedLiveTurns(): LiveTurn[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY_LIVE_TURNS);
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as LiveTurn[]) : [];
  } catch {
    return [];
  }
}

/** Keeps the live-turn ref, render trigger, and debounced storage sync together. */
export function useMindMapLiveTurns() {
  const liveTurnsRef = useRef<LiveTurn[]>(readPersistedLiveTurns());
  const [, setLiveTurnsState] = useState<LiveTurn[]>([]);
  const persistRef = useRef(
    createDebouncedPersist(() => {
      try {
        localStorage.setItem(STORAGE_KEY_LIVE_TURNS, JSON.stringify(liveTurnsRef.current));
        window.dispatchEvent(new Event('seldonLiveTurnsUpdated'));
      } catch {}
    })
  );

  const setLiveTurns = useCallback((updater: LiveTurn[] | ((prev: LiveTurn[]) => LiveTurn[])) => {
    const next = typeof updater === 'function' ? updater(liveTurnsRef.current) : updater;
    liveTurnsRef.current = next;
    persistRef.current.schedule();
    setLiveTurnsState(next);
  }, []);

  const flushLiveTurns = useCallback(() => persistRef.current.flush(), []);

  return { liveTurnsRef, setLiveTurns, flushLiveTurns };
}
