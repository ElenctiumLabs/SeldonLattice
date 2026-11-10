import { useEffect, useState } from 'react';
import type { LiveTurn } from '../../types/liveTurn';
import { merkleCidFromTurnId } from '../../lib/transcript';
import type { IndexItem } from '../IndexPanel';

const STORAGE_KEY_LIVE_TURNS = 'seldon_live_turns';

function readLiveTurns(): LiveTurn[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY_LIVE_TURNS);
    if (!raw) return [];
    return JSON.parse(raw) as LiveTurn[];
  } catch {
    return [];
  }
}

function liveTurnsToIndexItems(turns: LiveTurn[]): IndexItem[] {
  const questionByLevel = new Map<number, string>();
  for (const turn of turns) {
    const speaker = turn.speaker.toLowerCase();
    if (speaker === 'system' || speaker === 'question') {
      questionByLevel.set(turn.level ?? 0, turn.content);
    }
  }

  // Keep only the first question/system entry for each level in the index.
  const seenQuestionLevels = new Set<number>();
  const dedupedTurns = turns.filter((turn) => {
    const speaker = turn.speaker.toLowerCase();
    if (speaker === 'system' || speaker === 'question') {
      const level = turn.level ?? 0;
      if (seenQuestionLevels.has(level)) return false;
      seenQuestionLevels.add(level);
    }
    return true;
  });

  return dedupedTurns.map((turn): IndexItem => ({
    id: turn.id,
    round: (turn.level ?? 0) + 1,
    speaker: turn.speaker.toUpperCase(),
    role: turn.dialecticRole as IndexItem['role'],
    cid: turn.cid ?? merkleCidFromTurnId(turn.id),
    parentHashes: turn.parentHashes ?? [],
    isEcho: turn.isEcho === true,
    content: turn.content,
    roundQuestion: questionByLevel.get(turn.level ?? 0),
    isGateRejected:
      turn.dialecticRole === 'gate' &&
      (turn.content.toLowerCase().includes('reject') || turn.content.toLowerCase().includes('drift')),
    metrics: turn.metrics ? { ...turn.metrics, isSustained: turn.isSustained } : turn.isSustained ? { isSustained: true } : undefined,
    phase: turn.phase,
    level: turn.level ?? 0,
    remainingDissensus: turn.remainingDissensus ?? null,
    repeatsPremise: turn.repeatsPremise ?? null,
    unresolvedTensions: turn.unresolvedTensions ?? null,
    tensionIntensity: turn.tensionIntensity ?? null,
  }));
}

/** Synchronizes the switcher's live transcript with MindMap's localStorage events. */
export function useGraphLiveTurns() {
  const [liveTurns, setLiveTurns] = useState<LiveTurn[]>(readLiveTurns);

  useEffect(() => {
    const onStorage = (event: StorageEvent) => {
      if (event.key === STORAGE_KEY_LIVE_TURNS) setLiveTurns(readLiveTurns());
    };
    const onLiveTurnsUpdated = () => setLiveTurns(readLiveTurns());
    window.addEventListener('storage', onStorage);
    window.addEventListener('seldonLiveTurnsUpdated', onLiveTurnsUpdated);
    return () => {
      window.removeEventListener('storage', onStorage);
      window.removeEventListener('seldonLiveTurnsUpdated', onLiveTurnsUpdated);
    };
  }, []);

  return { liveTurns, items: liveTurnsToIndexItems(liveTurns) };
}
