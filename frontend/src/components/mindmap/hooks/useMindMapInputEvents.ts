import { useEffect } from 'react';
import type { RefObject } from 'react';
import type { RawApiNode } from '../../../layout';
import type { LiveTurn } from '../../../types/liveTurn';

type SetLiveTurns = (updater: LiveTurn[] | ((prev: LiveTurn[]) => LiveTurn[])) => void;
type BuildGraph = (apiData: RawApiNode[]) => void;

interface UseMindMapInputEventsOptions {
  setLiveTurns: SetLiveTurns;
  buildAndSetGraph: BuildGraph;
  lastApiDataRef: RefObject<RawApiNode[]>;
  pendingQuestionsRef: RefObject<Map<number, RawApiNode>>;
  pendingInterventionsRef: RefObject<RawApiNode[]>;
}

/** Registers window events that add optimistic questions and interventions. */
export function useMindMapInputEvents({
  setLiveTurns,
  buildAndSetGraph,
  lastApiDataRef,
  pendingQuestionsRef,
  pendingInterventionsRef,
}: UseMindMapInputEventsOptions): void {
  useEffect(() => {
    const handleUserQuestion = (event: Event) => {
      const { theme, level } = (event as CustomEvent<{ theme: string; level: number }>).detail;
      const cid = `pending-question-${level}-${Date.now()}`;

      pendingQuestionsRef.current.set(level, {
        cid,
        nodeType: 'question',
        level,
        content: theme,
        dialecticRole: 'thesis',
        isPending: true,
      });

      setLiveTurns((previous) => {
        // Replace earlier synthetic questions for this level, regardless of whether
        // the existing entry was labeled Question or System.
        const withoutStale = previous.filter((turn) => {
          if (turn.level !== level) return true;
          const speaker = turn.speaker.toLowerCase();
          return !((speaker === 'question' || speaker === 'system') && turn.id.startsWith('pending-question'));
        });
        return [
          ...withoutStale,
          {
            id: cid,
            speaker: 'Question',
            content: theme,
            dialecticRole: 'thesis',
            level,
            timestamp: Date.now(),
          },
        ];
      });

      buildAndSetGraph(lastApiDataRef.current);
    };

    const handleMediatorInterjection = (event: Event) => {
      const { content, signal, level, turn, speaker, speakerId } = (event as CustomEvent).detail;
      const resolvedSpeakerId = (speakerId || speaker || 'moderator').toLowerCase();
      const cid = `${resolvedSpeakerId}-interjection-${level}-${turn}-${Date.now()}`;
      pendingInterventionsRef.current.push({
        cid,
        nodeType: `${resolvedSpeakerId}_interjection`,
        speakerId: resolvedSpeakerId,
        level,
        content: signal ? `[${signal}] ${content}` : content,
        dialecticRole: 'interjection',
        turn: turn ?? 0,
        isPending: true,
      });
      buildAndSetGraph(lastApiDataRef.current);
    };

    window.addEventListener('userQuestion', handleUserQuestion);
    window.addEventListener('mediatorInterjection', handleMediatorInterjection);
    return () => {
      window.removeEventListener('userQuestion', handleUserQuestion);
      window.removeEventListener('mediatorInterjection', handleMediatorInterjection);
    };
  }, [buildAndSetGraph, lastApiDataRef, pendingInterventionsRef, pendingQuestionsRef, setLiveTurns]);
}
