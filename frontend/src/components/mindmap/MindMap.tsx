import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  useReactFlow,
  ReactFlowProvider,
} from 'reactflow';
import type { Node } from 'reactflow';
import 'reactflow/dist/style.css';

import type { RawApiNode } from '../../layout';
import { QuestionFan } from '../QuestionFan';
import { ShortcutsHelp } from '../ShortcutsHelp';
import { ReplayControls, useReplay } from '../ReplayControls';
import { useKeyboardShortcuts, type ShortcutGroup } from '../../hooks/useKeyboardShortcuts';
import type { ModuleType } from '../../App';
import type { LiveTurn } from '../../types/liveTurn';
import { findQuestionForLevel, liveTurnsFromApiNodes, reachedSustainedPause } from '../../lib/transcript';
import { NEW_SESSION_START_LEVEL } from '../../lib/ascentLevel';
import { api } from '../../lib/api';

import { useRequiredFrameworkSettings } from '../../hooks/useFrameworkSettings';
import { ROUTES } from '../../lib/api';
import { getConfig } from '../../lib/config';
import {
  THESIS_SPEAKERS,
  SYNTHESIS_TYPES,
  isCompletedSynthesisNodeType,
  speakerLabel,
} from '../../config/agentRegistry';
import { mindMapHoverRef } from './mindMapHover';
import type { HoverInfo } from './mindMapHover';
import MindMapCanvas from './MindMapCanvas';
import { resolveSynthesisInfo } from './mindMapGraphData';
import { normalizeMindMapStreamData } from './mindMapStreamData';
import { createDialecticianStreamRecords } from './mindMapTurnData';
import { useMindMapGraph } from './hooks/useMindMapGraph';
import { useMindMapInputEvents } from './hooks/useMindMapInputEvents';
import { resolveMindMapResumePoint } from './mindMapSessionData';
import { readPersistedLiveTurns, STORAGE_KEY_LIVE_TURNS, useMindMapLiveTurns } from './hooks/useMindMapLiveTurns';
import {
  ZoomInIcon,
  ZoomOutIcon,
  FitViewIcon,
} from '../IconSet';

// Derived from appsettings.json FrontendConfig  maxLevels
const MAX_LEVELS = getConfig().maxLevels;

let _uniqueSeq = 0;
function generateTurnId(prefix: string): string {
  _uniqueSeq = (_uniqueSeq + 1) % 1_000_000_000;
  return `${prefix}-${Date.now()}-${_uniqueSeq}`;
}

//  Props

interface MindMapProps {
  layoutMode?: 'dialectical' | 'helical';
  activeModule?: ModuleType | null;
  activeLevel?: number | 'all' | null;
}

const STORAGE_KEY_SESSION_ID = 'seldon_active_session_id';
const STORAGE_KEY_LEVEL = 'seldon_active_level';
const STORAGE_KEY_THEME = 'seldon_active_theme';
const STORAGE_KEY_STREAMING = 'seldon_is_streaming';
const STORAGE_KEY_INPUT = 'seldon_path_input';

// Settings loaded asynchronously via getSettings
//  Inner component (needs ReactFlowProvider ancestor)

function MindMapContent({ layoutMode = 'helical', activeModule, activeLevel }: MindMapProps) {
  const { fitView, zoomIn, zoomOut } = useReactFlow();
  const [hoverInfo, setHoverInfo] = useState<HoverInfo | null>(null);

  // Register hover callback in an effect - NOT during render (fixes setState-in-render warning)
  useEffect(() => {
    mindMapHoverRef.current = setHoverInfo;
    return () => { mindMapHoverRef.current = null; };
  }, []);

  // Graph state
  const [, setCurrentLevel] = useState<number | null>(null);

  // Entry bar & active theme state
  const [pathInput, setPathInput] = useState('');
  const [activeTheme, setActiveTheme] = useState('');
  const [isStreaming, setIsStreaming] = useState(false);
  const [awaitingNewQuestion, setAwaitingNewQuestion] = useState(false);
  const [sustainedPauseContext, setSustainedPauseContext] = useState<{
    theme: string;
    synthesis: string;
    unresolvedTensions: string[];
  } | null>(null);
  const [, setStreamError] = useState<string | null>(null);

  const { useStaticQuestions } = useRequiredFrameworkSettings(activeModule);

  // Keyboard shortcuts
  const [showShortcuts, setShowShortcuts] = useState(false);

  // Replay mode
  const [replayMode, setReplayMode] = useState(false);

  const { liveTurnsRef, setLiveTurns, flushLiveTurns } = useMindMapLiveTurns();

  // Stopped state - true after user explicitly halts ascent (shows Resume button)
  const [, setIsStopped] = useState(false);

  // Refs
  const reactFlowWrapper = useRef<HTMLDivElement>(null);
  const nodesRef = useRef<Node[]>([]);
  const lastApiDataRef = useRef<RawApiNode[]>([]);
  const pendingQuestionsRef = useRef<Map<number, RawApiNode>>(new Map());
  const pendingDebateNodesRef = useRef<Map<string, RawApiNode>>(new Map());
  const pendingInterventionsRef = useRef<RawApiNode[]>([]);
  const eventSourceRef = useRef<EventSource | null>(null);
  const currentSessionIdRef = useRef<string | null>(null);
  const currentThemeRef = useRef<string>('');
  const currentLevelRef = useRef<number>(0);
  const stopRequestedRef = useRef<boolean>(false);
  const levelTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Ref to always hold the latest startStreaming function (avoids stale closure in recursive calls)
  const startStreamingRef = useRef<(theme: string, level: number, sessionId: string) => void>(() => {});

  const {
    nodes,
    edges,
    buildAndSetGraph,
    fetchNodes,
    resetGraph,
  } = useMindMapGraph({
    layoutMode,
    fitView,
    liveTurnsRef,
    setLiveTurns,
    nodesRef,
    lastApiDataRef,
    pendingQuestionsRef,
    pendingDebateNodesRef,
    pendingInterventionsRef,
  });

  useMindMapInputEvents({
    setLiveTurns,
    buildAndSetGraph,
    lastApiDataRef,
    pendingQuestionsRef,
    pendingInterventionsRef,
  });

  // Focus on level when activeLevel prop changes
  useEffect(() => {
    const cfg = getConfig();
    if (activeLevel === 'all' || activeLevel === undefined) {
      fitView({ padding: cfg.fitViewAllPadding, duration: cfg.fitViewAllDuration });
      setCurrentLevel(null);
    } else if (typeof activeLevel === 'number') {
      const levelNodes = nodesRef.current.filter((n) => (n.data.level || 0) === activeLevel);
      if (levelNodes.length === 0) return;
      fitView({ nodes: levelNodes, padding: cfg.fitViewLevelPadding, duration: cfg.fitViewLevelDuration });
      setCurrentLevel(activeLevel);
    }
  }, [activeLevel, fitView]);

  //  Streaming engine

  const stopStreaming = useCallback(() => {
    stopRequestedRef.current = true;
    if (eventSourceRef.current) {
      eventSourceRef.current.close();
      eventSourceRef.current = null;
    }
    if (levelTimeoutRef.current) {
      clearTimeout(levelTimeoutRef.current);
      levelTimeoutRef.current = null;
    }
    setIsStreaming(false);
    setIsStopped(true);
    try {
      localStorage.setItem(STORAGE_KEY_STREAMING, 'false');
    } catch {}
    pendingInterventionsRef.current = [];
    flushLiveTurns();
    // Sync the graph with the Merkle store so persisted nodes (including the
    // question node) appear immediately after stopping, and survive refresh.
    fetchNodes(currentSessionIdRef.current);
  }, [fetchNodes, flushLiveTurns]);

  // Resume from the current level after Stop was hit
  const resumeStreaming = useCallback(() => {
    const sessionId = currentSessionIdRef.current;
    const theme = currentThemeRef.current;
    const level = currentLevelRef.current;
    if (!sessionId || !theme) return;
    setIsStopped(false);
    stopRequestedRef.current = false;
    startStreamingRef.current(theme, level, sessionId);
  }, []);

  const startStreaming = useCallback(
    (theme: string, level: number, sessionId: string) => {
      // Close any existing stream
      if (eventSourceRef.current) {
        eventSourceRef.current.close();
        eventSourceRef.current = null;
      }

      stopRequestedRef.current = false;
      currentSessionIdRef.current = sessionId;
      currentThemeRef.current = theme;
      currentLevelRef.current = level;
      setCurrentLevel(level);
      setActiveTheme(theme);
      setStreamError(null);
      setIsStreaming(true);

      window.dispatchEvent(new CustomEvent('seldonSessionStarted', {
        detail: { sessionId, theme, level }
      }));

      try {
        localStorage.setItem(STORAGE_KEY_SESSION_ID, sessionId);
        localStorage.setItem(STORAGE_KEY_THEME, theme);
        localStorage.setItem(STORAGE_KEY_INPUT, theme);
        localStorage.setItem(STORAGE_KEY_LEVEL, String(level));
        localStorage.setItem(STORAGE_KEY_STREAMING, 'true');
      } catch {}

      const url = `${ROUTES.ascendStream}?sessionId=${sessionId}&level=${level}&theme=${encodeURIComponent(theme)}&module=${encodeURIComponent(activeModule || 'philosophy')}`;

      if (import.meta.env.DEV) {
        console.debug('[MindMap] Opening stream:', url);
      }

      const eventSource = new EventSource(url);
      eventSourceRef.current = eventSource;

      eventSource.onopen = () => {
        if (import.meta.env.DEV) {
          console.debug('[MindMap] Stream opened for level', level);
        }
      };

      eventSource.onmessage = (event) => {
        if (stopRequestedRef.current) {
          eventSource.close();
          setIsStreaming(false);
          return;
        }

        let rawData: Record<string, any>;
        try {
          rawData = JSON.parse(event.data);
         } catch {
          if (import.meta.env.DEV) {
            console.warn('[MindMap] Failed to parse SSE data:', event.data);
          }
          return;
        }

        const data = normalizeMindMapStreamData(rawData);
        const eventType = data.type;
        // SpeakerId is the clean agent name ("proposer", "skeptic", etc.)
        // Type can be a suffixed node type like "proposer_reply", "skeptic_reply"
        const speakerId = data.speakerId ?? '';
        // Derive a clean display speaker: prefer SpeakerId, fall back to stripping _reply suffix from Type
        const displaySpeaker: string = speakerId || eventType.replace(/_reply$/, '');
        const content = data.content;
        const exitReason = data.exitReason as string | undefined;

        if (import.meta.env.DEV) {
          console.debug('[MindMap] SSE event:', eventType, speakerId);
        }

        //  Handle quality-gate forced exit
        if (exitReason) {
          if (import.meta.env.DEV) {
            console.warn('[MindMap] Quality gate exit:', exitReason);
          }
          setLiveTurns((prev) => [
            ...prev,
            {
              id: generateTurnId(`exit-${level}`),
              speaker: 'System',
              content: `[Dialogue terminated: ${exitReason}]`,
              dialecticRole: 'synthesis',
              level,
              timestamp: Date.now(),
            },
          ]);
          window.dispatchEvent(new CustomEvent('seldonNotification', {
            detail: { type: 'warning', title: 'Quality Gate', message: `Debate terminated: ${exitReason}` },
          }));
          eventSource.close();
          setIsStreaming(false);
          fetchNodes(sessionId);
          window.dispatchEvent(new CustomEvent('seldonNotification', {
            detail: { type: 'error', title: 'Session Ended', message: `Quality gate triggered: ${exitReason}` },
          }));
          return;
        }

        //  Gate rejection (ascension candidate rejected, will retry)
        if (eventType === 'gate_rejection') {
          // Show gate rejections as a synthesis-colored notice but don't close the stream
          window.dispatchEvent(new CustomEvent('seldonNotification', {
            detail: { type: 'warning', title: 'Gate Rejected', message: 'Ascension candidate rejected \u2014 retrying' },
          }));
          setLiveTurns((prev) => [
            ...prev,
            {
              id: generateTurnId(`gate-${level}`),
              speaker: 'Gate',
              content: `[Ascension validation rejected - retrying]
              ${content}`,
              dialecticRole: 'synthesis',
              level,
              timestamp: Date.now(),
            },
          ]);
          return;
        }

        //  Fractal echo: the ascent gate could not generalize, so the inquiry returns to
        //  its root. This arrives BEFORE the synthesis event; the stream is not closed
        //  here. Seed the next level's question with the return marker so both the graph
        //  and the index show the boundary immediately.
        if (eventType === 'fractal_echo') {
          const rootText: string =
            content || (data.nextTheme) || theme;
          const echoLevel = level + 1;
          const echoCid = generateTurnId(`fractal-echo-${level}`);

          pendingQuestionsRef.current.set(echoLevel, {
            cid: echoCid,
            nodeType: 'fractal_echo',
            level: echoLevel,
            content: rootText,
            dialecticRole: 'thesis',
            isPending: true,
            isEcho: true,
          });

          setLiveTurns((prev) => {
            const withoutStale = prev.filter(
              (t) => !(t.isEcho && (t.level ?? 0) === echoLevel)
            );
            return [
              ...withoutStale,
              {
                id: `node-${echoCid}`,
                speaker: 'Echo',
                content: rootText,
                dialecticRole: 'thesis',
                level: echoLevel,
                timestamp: Date.now(),
                isEcho: true,
              },
            ];
          });

          buildAndSetGraph(lastApiDataRef.current);

          window.dispatchEvent(new CustomEvent('seldonNotification', {
            detail: {
              type: 'info',
              title: 'Fractal Echo',
              message: `Returning to the root question: ${rootText}`,
            },
          }));
          return;
        }

        //  Synthesis / level complete
        if (SYNTHESIS_TYPES.has(eventType)) {
          const metaphor: string = data.metaphor ?? content;
          const isSustained: boolean = data.isSustained === true;
          const rawNext = data.nextTheme;
          const canChain = Boolean(rawNext && rawNext.trim());
          // A sustained rung with no next theme means the inquiry is spent: the gate could not
          // generalize and the root has already been reopened (or is the current rung). Stop
          // and hand over instead of re-asking until the level ceiling.
          const pauseForNewQuestion = isSustained && !canChain;
          const rawConnective = data.connectiveSentence;
          const rawThesis = data.synthesisThesis;
          const nextTheme: string =
            (rawNext && rawNext.trim()) ||
            (rawConnective && rawConnective.trim()) ||
            (rawThesis && rawThesis.trim()) ||
            `Further inquiry into ${theme}`;
          const synthInfo = resolveSynthesisInfo(displaySpeaker, activeModule ?? undefined);
          const synthesisLabel = synthInfo.speaker;
          const synthesisSpeakerId = synthInfo.speakerId;
          const synthesisNodeType = synthInfo.nodeType;

          setLiveTurns((prev) => {
            const currentLvlTurns = prev.filter((t) => (t.level ?? 0) === level);
            const otherLvlTurns = prev.filter((t) => (t.level ?? 0) !== level);

            // Deduplicate by turn ID, not by speaker name - the same speaker
            // (e.g. the Mediator) can legitimately have
            // multiple turns within the same level (interjection + synthesis,
            // or per-turn mediation after both dialecticians).  Speaker-name
            // dedup would evict those earlier turns, leaving only the last.
            const seenIds = new Set<string>();
            const dedupedCurrent = currentLvlTurns.filter((t) => {
              if (seenIds.has(t.id)) return false;
              seenIds.add(t.id);
              return true;
            });

            const synthTurnId = generateTurnId(`synth-${level}`);
            const synthTurn: LiveTurn = {
              id: synthTurnId,
              speaker: synthesisLabel,
              content: metaphor,
              dialecticRole: 'synthesis',
              level,
              timestamp: Date.now(),
              isSustained,
              // Negative-dialectic markers off the synthesis event, so the Inspector can
              // show them live rather than only after a reload reconstructs from nodes.
              remainingDissensus: data.remainingDissensus ?? null,
              repeatsPremise: data.repeatsPremise ?? null,
              unresolvedTensions: data.unresolvedTensions ?? null,
              tensionIntensity: data.tensionIntensity ?? null,
            };

            return [...otherLvlTurns, ...dedupedCurrent, synthTurn];
          });

          const synthNodeCid = generateTurnId(`synth-node-${level}`);
          pendingDebateNodesRef.current.set(`${level}-synthesis`, {
            cid: synthNodeCid,
            nodeType: synthesisNodeType,
            level,
            content: metaphor,
            dialecticRole: 'synthesis',
            speakerId: synthesisSpeakerId,
            isPending: true,
          });
          buildAndSetGraph(lastApiDataRef.current);

          eventSource.close();
          pendingInterventionsRef.current = [];
          fetchNodes(sessionId).then(() => {
            // Chain to next level if we haven't hit the max
            const nextLevel = level + 1;
            if (nextLevel < MAX_LEVELS && !stopRequestedRef.current && !pauseForNewQuestion) {
              levelTimeoutRef.current = setTimeout(() => {
                // CRITICAL: check again inside the callback - stopStreaming may have
                // been called in the window between synthesis and this fire.
                if (stopRequestedRef.current) return;

                // A fractal echo already seeded this level's question and return marker.
                // Keep it (and skip `userQuestion`, which would overwrite the node type)
                // so the return survives the chain into the next stream.
                const pendingReturn = pendingQuestionsRef.current.get(nextLevel);
                if (pendingReturn?.isEcho) {
                  pendingQuestionsRef.current.set(nextLevel, {
                    ...pendingReturn,
                    content: nextTheme,
                  });
                  buildAndSetGraph(lastApiDataRef.current);
                } else {
                  // Add pending question node for next level
                  pendingQuestionsRef.current.set(nextLevel, {
                    cid: generateTurnId(`pending-question-${nextLevel}`),
                    nodeType: 'question',
                    level: nextLevel,
                    content: nextTheme,
                    dialecticRole: 'thesis',
                    isPending: true,
                  });
                  buildAndSetGraph(lastApiDataRef.current);

                  // Dispatch event for any listeners
                  window.dispatchEvent(
                    new CustomEvent('userQuestion', {
                      detail: { theme: nextTheme, level: nextLevel },
                    })
                  );
                }

                // Use the ref to avoid stale closure on recursive call
                startStreamingRef.current(nextTheme, nextLevel, sessionId);
              }, getConfig().chainDelayMs);
            } else {
              setIsStreaming(false);
              setIsStopped(false);
              flushLiveTurns();
              try {
                localStorage.setItem(STORAGE_KEY_STREAMING, 'false');
              } catch {}
              if (pauseForNewQuestion) {
                // This question belongs to a round that must not start automatically.
                pendingQuestionsRef.current.delete(nextLevel);
                setAwaitingNewQuestion(true);
                setPathInput('');
                setSustainedPauseContext({
                  theme,
                  synthesis: metaphor,
                  unresolvedTensions: Array.isArray(data.unresolvedTensions)
                    ? data.unresolvedTensions.map((item: unknown) => String(item))
                    : typeof data.unresolvedTensions === 'string'
                    ? [data.unresolvedTensions]
                    : [],
                });
                try { localStorage.removeItem(STORAGE_KEY_INPUT); } catch {}
                window.dispatchEvent(new CustomEvent('seldonNotification', {
                  detail: {
                    type: 'warning',
                    title: 'Your question',
                    message: 'This theme has remained sustained for three rounds. Enter a new question or reframe to continue.',
                  },
                }));
                return;
              }
              window.dispatchEvent(new CustomEvent('seldonSessionEnded', {
                detail: { sessionId }
              }));
              window.dispatchEvent(new CustomEvent('seldonNotification', {
                detail: { type: 'success', title: 'Session Complete', message: 'All reasoning levels have been completed.' },
              }));
            }
          });

          window.dispatchEvent(new CustomEvent('seldonNotification', {
            detail: isSustained
              ? { type: 'warning', title: 'Sustained Theme', message: `Level ${level}: ascension gate rejected every candidate - staying on the current theme` }
              : { type: 'success', title: 'Synthesis', message: `Level ${level} synthesis reached by ${synthesisLabel}` },
          }));
          return;
        }

        // Moderator / Stalemate interjection
        const isInterjectionEvent =
          eventType === 'mediator' ||
          eventType === 'mediator_gate' ||
          eventType.includes('interjection') ||
          (data.triggeringSignal != null);

        if (isInterjectionEvent) {
          const signal = data.triggeringSignal;
          // The agent is the Mediator; the role is mediation. Normalise the raw SpeakerId
          // so the intervention never appears as a synthesis/Moderator turn.
          const speakerName = speakerLabel(
            displaySpeaker || (data.speakerId ?? 'mediator'),
            eventType,
            'mediation',
          );
          const label = signal
            ? `[${speakerName} intervention \u2014 ${signal}]`
            : `[${speakerName} interjection]`;

          window.dispatchEvent(new CustomEvent('seldonNotification', {
            detail: {
              type: 'info',
              title: `${speakerName} Intervention`,
              message: signal ? `Triggered by: ${signal}` : `${speakerName} interjected`,
            },
          }));

          const turnVal = data.turn ?? 0;
          const speakerId = (data.speakerId ?? displaySpeaker ?? 'moderator').toLowerCase();
          const cid = generateTurnId(`${speakerId}-interjection-${level}-${turnVal}`);

          setLiveTurns((prev) => [
            ...prev,
            {
              id: cid,
              speaker: speakerName,
              content: `${label}\n${content}`,
              dialecticRole: 'mediation',
              level,
              timestamp: Date.now(),
              metrics: signal
                ? {
                    stalemateSignal: String(signal),
                    isStalemate: true,
                  }
                : undefined,
            },
          ]);

          // Add pending intervention node
          pendingInterventionsRef.current.push({
            cid,
            nodeType: `${speakerId}_interjection`,
            speakerId,
            level,
            content: signal ? `[${signal}] ${content}` : content,
            dialecticRole: 'mediation',
            turn: turnVal,
            isPending: true,
          });
          buildAndSetGraph(lastApiDataRef.current);
          return;
        }

        //  System message
        if (eventType === 'system') {
          setLiveTurns((prev) => [
            ...prev,
            {
              id: generateTurnId(`sys-${level}`),
              speaker: 'System',
              content,
              dialecticRole: 'system',
              level,
              timestamp: Date.now(),
            },
          ]);
          eventSource.close();
          setIsStreaming(false);
          fetchNodes(sessionId);
          flushLiveTurns();
          return;
        }

        //  Dialectician / mediation turn
        // Use the backend's dialecticRole if available, otherwise classify client-side
        const rawBackendRole = data.dialecticRole as string | undefined;
        const backendRole = typeof rawBackendRole === 'string'
          ? (rawBackendRole.toLowerCase() as LiveTurn['dialecticRole'])
          : undefined;
        const isThesisSpeaker = THESIS_SPEAKERS.has(displaySpeaker.toLowerCase());
        const dialecticianRole: LiveTurn['dialecticRole'] = backendRole ?? (isThesisSpeaker ? 'thesis' : 'antithesis');

        if (content) {
          const turnVal = data.turn ?? 0;
          const turnCid = generateTurnId(`turn-${displaySpeaker}-${level}-${turnVal}`);
          const records = createDialecticianStreamRecords({
            data,
            id: turnCid,
            eventType,
            speaker: displaySpeaker,
            content,
            role: dialecticianRole,
            level,
            turn: turnVal,
            timestamp: Date.now(),
          });

          setLiveTurns((prev) => [...prev, records.liveTurn]);

          // Key includes turn number so multiple turns from the same speaker at
          // the same level are each tracked as separate pending nodes (instead of
          // each new turn overwriting the previous one and causing earlier nodes
          // to disappear from the graph mid-stream).
          pendingDebateNodesRef.current.set(`${level}-${displaySpeaker.toLowerCase()}-${turnVal}`, records.pendingNode);

          buildAndSetGraph(lastApiDataRef.current);
        }
       };

      eventSource.onerror = (err) => {
        console.error('[MindMap] SSE stream error', err);
        // CONNECTING means it's trying to reconnect (we don't want auto-reconnect for SSE)
        if (eventSource.readyState === EventSource.CLOSED) {
          eventSource.close();
          setIsStreaming(false);
          fetchNodes(sessionId);
          if (!stopRequestedRef.current) {
            window.dispatchEvent(new CustomEvent('seldonNotification', {
              detail: { type: 'error', title: 'Stream Ended', message: 'The reasoning session ended unexpectedly.' },
            }));
          }
        } else if (eventSource.readyState === EventSource.CONNECTING) {
          // Auto-reconnect attempt - close it, we manage our own reconnect via level chaining
          eventSource.close();
          if (!stopRequestedRef.current) {
            setStreamError('Stream connection lost.');
          }
          setIsStreaming(false);
        }
      };
    },
    [activeModule, buildAndSetGraph, fetchNodes, flushLiveTurns, setLiveTurns]
  );

  // Keep startStreamingRef always pointing to the latest startStreaming (avoids stale closure)
  useEffect(() => {
    startStreamingRef.current = startStreaming;
  }, [startStreaming]);

  //  Form submit

  const handlePathSubmit = async (e: React.SubmitEvent) => {
    e.preventDefault();
    const text = pathInput.trim();
    if (!text || isStreaming) return;

    if (activeLevel === null) {
      window.dispatchEvent(new CustomEvent('seldonNotification', {
        detail: { type: 'warning', title: 'Layer Required', message: 'Please select at least 1 layer in the top bar to activate reasoning.' },
      }));
      return;
    }

    setActiveTheme(text);
    setAwaitingNewQuestion(false);
    setSustainedPauseContext(null);
    try {
      localStorage.setItem(STORAGE_KEY_INPUT, text);
    } catch {}
    setStreamError(null);

    try {
      // 1. Create session (scoped to the active framework + current user)
      const { sessionId: newSessionId } = await api.createSession(text, activeModule ?? 'philosophy');
      if (!newSessionId) throw new Error('No sessionId returned from /api/sessions');

      currentSessionIdRef.current = newSessionId;
      if (import.meta.env.DEV) {
        console.debug('[MindMap] Created session:', newSessionId);
      }

      // A new session always begins at its root. `activeLevel` is a *view* filter
      // (which round the graph is focused on), not a start level: inheriting it here
      // started fresh sessions at whatever round the previously viewed session had
      // reached. A session taken to level 9 left "Round 10" selected, so
      // the next session began at level 9, ran a single level, and stopped
      // because level 10 is not below the 10-level ceiling.
      const startingLevel = NEW_SESSION_START_LEVEL;

      // 2. Optimistically place thesis question node
      pendingQuestionsRef.current.clear();
      pendingDebateNodesRef.current.clear();
      pendingInterventionsRef.current = [];
      lastApiDataRef.current = [];

      pendingQuestionsRef.current.set(startingLevel, {
        cid: generateTurnId(`question-${startingLevel}`),
        nodeType: 'question',
        level: startingLevel,
        content: text,
        dialecticRole: 'thesis',
        isPending: true,
      });
      buildAndSetGraph([]);

      // 3. Start SSE stream
      startStreaming(text, startingLevel, newSessionId);
    } catch (err) {
      console.error('[MindMap] Failed to submit dialectical path:', err);
      setStreamError(err instanceof Error ? err.message : String(err));
      setIsStreaming(false);
    }
  };

  //  Keyboard shortcuts

  const shortcutGroups: ShortcutGroup[] = [
    {
      label: 'Session',
      shortcuts: [
        {
          key: 'Enter', ctrl: true, description: 'Submit debate theme',
          action: () => {
            if (pathInput.trim() && !isStreaming) {
              const fakeEvent = { preventDefault: () => {} } as React.SubmitEvent;
              handlePathSubmit(fakeEvent);
            }
          },
        },
        {
          key: 'n', ctrl: true, description: 'New debate',
          action: () => {
            setPathInput('');
            try { localStorage.removeItem(STORAGE_KEY_INPUT); } catch {}
          },
        },
        {
          key: ' ', description: 'Pause / resume streaming',
          action: () => {
            if (isStreaming) {
              stopStreaming();
            } else if (currentSessionIdRef.current) {
              resumeStreaming();
            }
          },
        },
      ],
    },
    {
      label: 'View',
      shortcuts: [
        {
          key: 'f', description: 'Fit view',
          action: () => fitView({ padding: getConfig().fitViewShortcutPadding, duration: getConfig().fitViewShortcutDuration }),
        },
        {
          key: '+', ctrl: true, description: 'Zoom in',
          action: () => zoomIn({ duration: getConfig().zoomDuration }),
        },
        {
          key: '-', ctrl: true, description: 'Zoom out',
          action: () => zoomOut({ duration: getConfig().zoomDuration }),
        },
      ],
    },
    {
      label: 'General',
      shortcuts: [
        {
          key: 'Escape', description: 'Close dialog',
          action: () => setShowShortcuts(false),
          preventDefault: false,
        },
        {
          key: '?', description: 'Show keyboard shortcuts',
          action: () => setShowShortcuts((v) => !v),
        },
      ],
    },
  ];

  useKeyboardShortcuts(shortcutGroups);

  //  Global event listeners & state hydration

  useEffect(() => {
    // Sessions are loaded ONLY on explicit selection from the session manager.
    //
    // This used to hydrate the previously-active session from localStorage on every
    // mount, so opening a faculty (or reloading the page) silently replayed whatever
    // session was last touched. The canvas now starts empty every time and the user
    // chooses what to open.
    //
    // What is still restored is the unsaved *input* text, which is a draft the user
    // typed rather than session state - losing it on navigation would be its own bug.
    const savedInput = localStorage.getItem(STORAGE_KEY_INPUT);
    if (savedInput) {
      setPathInput(savedInput);
    }

    // Reclaim the liveTurns ref from localStorage for the session being opened. The
    // ref is the metrics source used during reconstruction, so seeding it here (not
    // only in the state initialiser) is what keeps stream-only metrics across a
    // reload. It is intentionally NOT applied to the visible canvas until a session
    // is actually selected.
    const savedTurns = localStorage.getItem(STORAGE_KEY_LIVE_TURNS);
    if (savedTurns) {
      try {
        const restored = JSON.parse(savedTurns) as LiveTurn[];
        if (Array.isArray(restored)) liveTurnsRef.current = restored;
      } catch {}
    }

    const handleRefresh = () => fetchNodes(currentSessionIdRef.current);

    const handleClearMindMap = (e: Event) => {
      const { sessionId } = (e as CustomEvent<{ sessionId: string }>).detail;
      currentSessionIdRef.current = sessionId;
      resetGraph();
      setLiveTurns([]);
      lastApiDataRef.current = [];
      pendingQuestionsRef.current.clear();
      pendingDebateNodesRef.current.clear();
      pendingInterventionsRef.current = [];
      setAwaitingNewQuestion(false);
      setSustainedPauseContext(null);
      try {
        localStorage.removeItem(STORAGE_KEY_SESSION_ID);
        localStorage.removeItem(STORAGE_KEY_LEVEL);
        localStorage.removeItem(STORAGE_KEY_THEME);
        localStorage.removeItem(STORAGE_KEY_STREAMING);
        localStorage.removeItem(STORAGE_KEY_LIVE_TURNS);
        localStorage.removeItem('seldon_selected_id');
        localStorage.removeItem('seldon_selected_round');
        localStorage.removeItem('seldon_selected_round_module');
      } catch {}
    };

    const handleReloadSession = (e: Event) => {
      const { sessionId, rootQuestion, maxLevel } = (e as CustomEvent<{ sessionId: string; rootQuestion?: string; maxLevel?: number }>).detail;
      currentSessionIdRef.current = sessionId;
      setAwaitingNewQuestion(false);
      setSustainedPauseContext(null);

      // Stop any active stream first
      stopRequestedRef.current = true;
      if (eventSourceRef.current) {
        eventSourceRef.current.close();
        eventSourceRef.current = null;
      }
      if (levelTimeoutRef.current) {
        clearTimeout(levelTimeoutRef.current);
        levelTimeoutRef.current = null;
      }
      setIsStreaming(false);
      setIsStopped(false);

      // Seed the metrics source before reconstructing.
      //
      // The ref was previously left as-is, which on a fresh page meant it was empty, so
      // reconstruction had no prior turns to inherit from and every turn came back
      // metric-less. This is the path a session selected from the session manager takes.
      //
      // Metrics now come off the node itself, so this fallback only covers stream-only
      // values on turns the store has not written yet - but seeding it is still what
      // makes the manager path behave like the refresh path.
      liveTurnsRef.current = readPersistedLiveTurns();

      // Restore theme and input from the session's ROOT question - this is what
      // the THESIS bar always displays, never the latest level thesis.
      if (rootQuestion) {
        setPathInput(rootQuestion);
        setActiveTheme(rootQuestion);
        try {
          localStorage.setItem(STORAGE_KEY_INPUT, rootQuestion);
        } catch {}
      }

      // Restore the session level
      if (maxLevel !== undefined) {
        currentLevelRef.current = maxLevel;
        setCurrentLevel(maxLevel);
        try { localStorage.setItem(STORAGE_KEY_LEVEL, String(maxLevel)); } catch {}
      }

      // Persist session ID
      try {
        localStorage.setItem(STORAGE_KEY_SESSION_ID, sessionId);
        localStorage.setItem(STORAGE_KEY_STREAMING, 'false');
      } catch {}

      // Fetch nodes and reconstruct liveTurns from Merkle store
      api.getNodesFlat({ sessionId, pageSize: 200 })
        .then((apiData: RawApiNode[]) => {
          lastApiDataRef.current = apiData;
          buildAndSetGraph(apiData);

          // Restore the handover state from persisted synthesis nodes. A sustained rung
          // that returned to the root continued the ladder; a sustained rung that did not
          // stopped there, and reopening the session must not silently resume past it.
          const completedSyntheses = apiData
            .filter((node) => isCompletedSynthesisNodeType(node.nodeType))
            .sort((a, b) => (a.level ?? 0) - (b.level ?? 0));
          const pauseReached = reachedSustainedPause(completedSyntheses, apiData, rootQuestion);
          setAwaitingNewQuestion(pauseReached);
          if (pauseReached) {
            const latestSynthesis = completedSyntheses[completedSyntheses.length - 1];
            const tensions = latestSynthesis?.unresolvedTensions;
            setPathInput('');
            setSustainedPauseContext({
              theme: findQuestionForLevel(apiData, latestSynthesis?.level ?? 0) ?? rootQuestion ?? '',
              synthesis: latestSynthesis?.content ?? '',
              unresolvedTensions: Array.isArray(tensions)
                ? tensions.map((item: unknown) => String(item))
                : typeof tensions === 'string'
                ? [tensions]
                : [],
            });
          } else {
            setSustainedPauseContext(null);
          }

          // RESUME anchor: the session continues at the level AFTER its deepest
          // completed synthesis, seeded by that level's question (the latest
          // thesis) - which is distinct from the root question the page shows.
          // Without this, Resume would re-stream using the root as the theme.
          const resumePoint = resolveMindMapResumePoint(apiData, rootQuestion, maxLevel, MAX_LEVELS);
          if (resumePoint.canResume) {
            currentThemeRef.current = resumePoint.resumeTheme;
            currentLevelRef.current = resumePoint.resumeLevel;
            setCurrentLevel(resumePoint.resumeLevel);
            try { localStorage.setItem(STORAGE_KEY_THEME, resumePoint.resumeTheme); } catch {}
          } else {
            // Fully completed session - anchor to the deepest level's question
            // so any manual RESUME re-runs that level with its true theme.
            currentThemeRef.current = resumePoint.currentTheme;
          }

          const previousTurns = liveTurnsRef.current;
          const reconstructed = liveTurnsFromApiNodes(apiData, previousTurns);

          // Quality metrics are read off the node during reconstruction, so no separate
          // history fetch is needed to populate them. The previous enrichment pass
          // re-matched turns by (speaker, level, ordinal position) against
          // /history - but it compared a 0-based position against the endpoint's
          // 1-based turnNumber, so it never matched and silently contributed nothing.
          setLiveTurns(reconstructed);
        })
        .catch(() => fetchNodes(sessionId));
    };

    window.addEventListener('refreshMindMap', handleRefresh);
    window.addEventListener('clearMindMap', handleClearMindMap);
    window.addEventListener('reloadSession', handleReloadSession);

    return () => {
      window.removeEventListener('refreshMindMap', handleRefresh);
      window.removeEventListener('clearMindMap', handleClearMindMap);
      window.removeEventListener('reloadSession', handleReloadSession);
      if (eventSourceRef.current) eventSourceRef.current.close();
      if (levelTimeoutRef.current) clearTimeout(levelTimeoutRef.current);
      flushLiveTurns();
    };
  }, [fetchNodes, buildAndSetGraph, flushLiveTurns, liveTurnsRef, resetGraph, setLiveTurns]);

  // Render

  const onGraphNodeClick = useCallback((_: React.MouseEvent, node: Node) => {
    window.dispatchEvent(new CustomEvent('graphNodeClick', { detail: node.data }));
  }, []);

  // Replay mode hook
  const { visibleTurns, ...replay } = useReplay(replayMode ? liveTurnsRef.current : []);

  // Filter nodes/edges for progressive reveal during replay
  const displayNodes = useMemo(() => {
    if (!replayMode) return nodes;
    const visibleCids = new Set(
      visibleTurns
        .map(t => t.id.startsWith('node-') ? t.id.slice(5) : null)
        .filter(Boolean)
    );
    return nodes.filter(n => visibleCids.has(n.id));
  }, [nodes, replayMode, visibleTurns]);

  const displayEdges = useMemo(() => {
    if (!replayMode) return edges;
    const visibleIds = new Set(displayNodes.map(n => n.id));
    return edges.filter(e => visibleIds.has(e.source) && visibleIds.has(e.target));
  }, [edges, replayMode, displayNodes]);

  // Refit view when replay position changes
  useEffect(() => {
    if (replayMode) {
      const t = setTimeout(() => fitView({ padding: 0.4, duration: 200 }), 50);
      return () => clearTimeout(t);
    }
  }, [replayMode, replay.currentIndex, fitView]);

  return (
    <div className="w-full h-full relative overflow-hidden" ref={reactFlowWrapper}>
      <MindMapCanvas
        layoutMode={layoutMode}
        nodes={displayNodes}
        edges={displayEdges}
        onNodeClick={onGraphNodeClick}
        hoverInfo={hoverInfo}
      />

      {/*  Fixed Bottom Input & Controls Bar  */}
      <div
        className="absolute bottom-0 left-0 right-0 z-30 p-2 md:p-3 pb-[calc(0.5rem+env(safe-area-inset-bottom,0px))] md:pb-3 border-t flex flex-col gap-1.5 md:gap-2 select-none pr-glass-surface"
        style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)' }}
      >
        {awaitingNewQuestion && sustainedPauseContext && (
          <section
            aria-live="polite"
            className="w-full max-w-xl mx-auto rounded border px-3 py-2 text-xs space-y-1"
            style={{ borderColor: 'var(--pr-role-question)', color: 'var(--pr-text)' }}
          >
            <p className="font-mono font-bold uppercase tracking-wide" style={{ color: 'var(--pr-role-question)' }}>
              Three sustained rounds · enter a new question or reframe
            </p>
            <p><strong>Theme:</strong> {sustainedPauseContext.theme}</p>
            <p><strong>Latest synthesis:</strong> {sustainedPauseContext.synthesis}</p>
            {sustainedPauseContext.unresolvedTensions.length > 0 && (
              <p><strong>Unresolved tension:</strong> {sustainedPauseContext.unresolvedTensions.join('; ')}</p>
            )}
          </section>
        )}
        {/* Row 1: Input Prompt */}
        {/* Center Section: Input Prompt */}
        <form
          onSubmit={handlePathSubmit}
          className="flex-1 min-w-0 max-w-xl mx-auto flex items-center border rounded px-2 md:px-3 py-2 md:py-1.5 transition-all"
          style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}
        >
          {useStaticQuestions && !isStreaming && !awaitingNewQuestion ? (
            <div className="flex-1 flex items-center h-full">
              <QuestionFan
                module={activeModule || 'philosophy'}
                onSelect={(text) => {
                  setPathInput(text);
                  try { localStorage.setItem(STORAGE_KEY_INPUT, text); } catch {}
                }}
                disabled={isStreaming || activeLevel === null}
                selectedText={pathInput}
              />
            </div>
          ) : (
            <>
              <span className="hidden sm:inline text-[13px] font-mono font-bold uppercase tracking-wider mr-2 whitespace-nowrap" style={{ color: 'var(--pr-text)' }}>
                {isStreaming ? 'ASCENDING ›' : awaitingNewQuestion ? 'NEW QUESTION ›' : 'THESIS›'}
              </span>
              <input
                type="text"
                value={pathInput}
                onChange={(e) => {
                  const val = e.target.value;
                  setPathInput(val);
                  try {
                    localStorage.setItem(STORAGE_KEY_INPUT, val);
                  } catch {}
                }}
                disabled={isStreaming || activeLevel === null}
                placeholder={
                  activeLevel === null
                    ? '⚠ Select at least 1 layer/round above to activate reasoning...'
                    : awaitingNewQuestion
                    ? 'Enter a new question or reframe the inquiry…'
                    : isStreaming
                    ? `Ascending: ${activeTheme || 'Reasoning stream active'}`
                    : 'What is courage?'
                }
                className="w-full min-w-0 bg-transparent text-[14px] outline-none font-serif disabled:opacity-50 overflow-hidden text-ellipsis whitespace-nowrap"
                style={{ color: 'var(--pr-text)' }}
              />
            </>
          )}
        </form>

        {/* Row 2: Canvas Controls + Action Buttons */}
        <div className="flex items-center justify-center gap-1.5 md:gap-2 w-full flex-0">
          {/* MindMap Canvas Controls (hidden on mobile - pinch-to-zoom) */}
          <div className="hidden md:flex items-center gap-1.5">
            <button
              onClick={() => zoomOut()}
              className="p-2 rounded border transition-all cursor-pointer"
              style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              title="Zoom Out"
            >
              <ZoomOutIcon size={14} />
            </button>
            <button
              onClick={() => zoomIn()}
              className="p-2 rounded border transition-all cursor-pointer"
              style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              title="Zoom In"
            >
              <ZoomInIcon size={14} />
            </button>
            <button
              onClick={() => fitView()}
              className="p-2 rounded border transition-all cursor-pointer"
              style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              title="Center View"
            >
              <FitViewIcon size={14} />
            </button>
          </div>
          <div className="w-px h-5" style={{ backgroundColor: 'var(--pr-border)' }} />
          {isStreaming ? (
            <button
              type="button"
              onClick={stopStreaming}
              className="px-3 md:px-4 py-2 md:py-1.5 rounded text-[12px] md:text-[13px] font-mono font-bold uppercase tracking-wider transition-all cursor-pointer min-h-11 md:min-h-0"
              style={{
                backgroundColor: 'rgba(176,90,58,0.2)',
                color: 'var(--pr-role-question)',
                border: '1px solid var(--pr-role-question)',
              }}
            >
              STOP
            </button>
          ) : (
            <>
              <button
                type="button"
                disabled={activeLevel === null}
                onClick={() => {
                  if (activeLevel === null) return;
                  if (!awaitingNewQuestion && currentSessionIdRef.current && (currentThemeRef.current || pathInput.trim())) {
                    resumeStreaming();
                  } else if (pathInput.trim()) {
                    // Submit new session - reuse form handler logic
                    const fakeEvent = { preventDefault: () => {} } as React.SubmitEvent;
                    handlePathSubmit(fakeEvent);
                  }
                }}
                className="px-3 md:px-4 py-2 md:py-1.5 rounded text-[12px] md:text-[13px] font-mono font-bold uppercase tracking-wider transition-all min-h-11 md:min-h-0"
                style={{
                  backgroundColor: activeLevel === null ? 'var(--pr-surface-raised)' : 'var(--pr-role-proposer)',
                  color: activeLevel === null ? 'var(--pr-text-dim)' : 'var(--pr-bg)',
                  cursor: activeLevel === null ? 'not-allowed' : 'pointer',
                  border: activeLevel === null ? '1px solid var(--pr-border)' : 'none',
                }}
                title={activeLevel === null ? 'Select at least 1 layer above to activate reasoning' : undefined}
              >
                {currentSessionIdRef.current && !awaitingNewQuestion ? 'RESUME' : 'SUBMIT'}
              </button>
              <button
                type="button"
                onClick={() => {
                  setPathInput('');
                  resetGraph();
                  setLiveTurns([]);
                  setReplayMode(false);
                  currentSessionIdRef.current = null;
                  currentThemeRef.current = '';
                  currentLevelRef.current = 0;
                  setAwaitingNewQuestion(false);
                  setSustainedPauseContext(null);
                  setCurrentLevel(0);
                  pendingQuestionsRef.current.clear();
                  pendingDebateNodesRef.current.clear();
                  pendingInterventionsRef.current = [];

                  try {
                    localStorage.removeItem(STORAGE_KEY_SESSION_ID);
                    localStorage.removeItem(STORAGE_KEY_LEVEL);
                    localStorage.removeItem(STORAGE_KEY_THEME);
                    localStorage.removeItem(STORAGE_KEY_STREAMING);
                    localStorage.removeItem(STORAGE_KEY_LIVE_TURNS);
                    localStorage.removeItem('seldon_selected_id');
                    localStorage.removeItem('seldon_selected_round');
                    localStorage.removeItem('seldon_selected_round_module');
                    localStorage.removeItem(STORAGE_KEY_INPUT);
                  } catch {}

                  window.dispatchEvent(new CustomEvent('seldonSessionCleared'));
                }}
                className="px-3 md:px-4 py-2 md:py-1.5 rounded text-[12px] md:text-[13px] font-mono font-bold uppercase tracking-wider transition-all cursor-pointer min-h-11 md:min-h-0"
                style={{
                  backgroundColor: 'var(--pr-surface)',
                  color: 'var(--pr-text)',
                  border: '1px solid var(--pr-border)',
                }}
              >
                NEW
              </button>
              {!isStreaming && liveTurnsRef.current.length > 0 && (
                <button
                  type="button"
                  onClick={() => {
                    setReplayMode((v) => !v);
                    if (replayMode) replay.reset();
                  }}
                  className="px-3 py-2 rounded border transition-all cursor-pointer min-h-11 md:min-h-0 flex items-center justify-center"
                  style={{
                    backgroundColor: replayMode ? 'var(--pr-role-proposer)' : 'var(--pr-surface)',
                    color: replayMode ? 'var(--pr-bg)' : 'var(--pr-text-muted)',
                    borderColor: replayMode ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
                  }}
                  title={replayMode ? 'Exit replay' : 'Replay session'}
                >
                  <svg className="w-4 h-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <polygon points="5,3 19,12 5,21" />
                  </svg>
                </button>
              )}
            </>
          )}
        </div>
      </div>

      {/* Replay Mode Controls */}
      {replayMode && !isStreaming && liveTurnsRef.current.length > 0 && (
        <ReplayControls
          turns={liveTurnsRef.current}
          isPlaying={replay.isPlaying}
          currentIndex={replay.currentIndex}
          speed={replay.speed}
          onPlay={replay.play}
          onPause={replay.pause}
          onStepForward={replay.stepForward}
          onStepBack={replay.stepBack}
          onReset={replay.reset}
          onSpeedChange={replay.setSpeed}
          onJumpTo={replay.jumpTo}
        />
      )}

      {/* Keyboard Shortcuts Help Dialog */}
      {showShortcuts && (
        <ShortcutsHelp groups={shortcutGroups} onClose={() => setShowShortcuts(false)} />
      )}
    </div>
  );
}

//  Public export

export default function MindMap({ layoutMode = 'helical', activeModule, activeLevel }: MindMapProps) {
  return (
    <ReactFlowProvider>
      <MindMapContent layoutMode={layoutMode} activeModule={activeModule} activeLevel={activeLevel} />
    </ReactFlowProvider>
  );
}
