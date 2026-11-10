import { useState, useEffect, useMemo, lazy, useRef } from 'react';
import { IndexPanel, type IndexItem } from './IndexPanel';
import InspectorPanel from './InspectorPanel';
import AgentPanel from './AgentPanel';
import type { ModuleType } from '../App';
import { DIALECTICIAN_SPEAKERS } from '../config/agentRegistry';
import type { GraphNodeClickDetail } from '../types/liveTurn';
import { isMerkleCid, merkleCidFromTurnId } from '../lib/transcript';
import { useGraphLiveTurns } from './hooks/useGraphLiveTurns';
import { api } from '../lib/api';
import type { SessionClaim } from '../lib/api';
import { useRequiredFrameworkSettings } from '../hooks/useFrameworkSettings';
import { useMediaQuery } from '../hooks/useMediaQuery';
import {
  DEFAULT_ROUND_SELECTION,
  focusLevelForSelection,
  selectionForModuleChange,
} from '../lib/ascentLevel';

const MindMap = lazy(() => import('./MindMap'));

type MobileTab = 'index' | 'graph' | 'inspector' | 'agents';

interface GraphViewSwitcherProps {
  activeModule?: ModuleType | null;
  currentSessionId?: string | null;
}

const STORAGE_KEY_SELECTED_ID = 'seldon_selected_id';
const STORAGE_KEY_SELECTED_ROUND = 'seldon_selected_round';
const STORAGE_KEY_SELECTED_ROUND_MODULE = 'seldon_selected_round_module';

let _switcherSeq = 0;
function uniqueGraphItemId(prefix = 'graph'): string {
  _switcherSeq = (_switcherSeq + 1) % 1_000_000_000;
  return `${prefix}-${Date.now()}-${_switcherSeq}`;
}



export default function GraphViewSwitcher({ activeModule, currentSessionId }: GraphViewSwitcherProps) {
  const previousModuleRef = useRef<string | null>(activeModule ?? null);

  const [selectedRound, setSelectedRound] = useState<number | 'all' | null>(() => {
    const saved = localStorage.getItem(STORAGE_KEY_SELECTED_ROUND);
    // Ignore a selection made in a different framework - a "Round 10"
    // read as a foreign layer index would focus the wrong level.
    const savedModule = localStorage.getItem(STORAGE_KEY_SELECTED_ROUND_MODULE);
    if (savedModule && activeModule && savedModule !== activeModule) {
      return DEFAULT_ROUND_SELECTION;
    }
    if (saved === 'null') return null;
    return saved ? (saved === 'all' ? 'all' : (parseInt(saved, 10) as number)) : DEFAULT_ROUND_SELECTION;
  });

  // Round numbers are framework-specific: a "Round 10"
  // Carrying one across a switch made a new
  // session start at level 9 (10 - 1), run one level, and stop at the ceiling.
  useEffect(() => {
    setSelectedRound((current) => {
      const next = selectionForModuleChange(previousModuleRef.current, activeModule, current);
      previousModuleRef.current = activeModule ?? null;
      return next;
    });
  }, [activeModule]);

  const { liveTurns, items } = useGraphLiveTurns();
  const [selectedId, setSelectedId] = useState<string | null>(() => {
    try {
      return localStorage.getItem(STORAGE_KEY_SELECTED_ID);
    } catch {
      return null;
    }
  });
  const [transientItems, setTransientItems] = useState<IndexItem[]>([]);
  const [inspectorOnlyItem, setInspectorOnlyItem] = useState<IndexItem | null>(null);
  const [claimsByLevel, setClaimsByLevel] = useState<Map<number, SessionClaim[]>>(new Map());
  const { layoutMode } = useRequiredFrameworkSettings(activeModule);
  const isDesktop = useMediaQuery('(min-width: 768px)');
  const [mobileTab, setMobileTab] = useState<MobileTab>('graph');
  const [showAgentPanel, setShowAgentPanel] = useState(false);
  const [maxTurns, setMaxTurns] = useState(10);

  //  Fetch dialectics config for maxTurns 
  useEffect(() => {
    if (!activeModule) return;
    api.getDialecticsConfig(activeModule)
      .then((config) => {
        if (config?.qualityGates?.maxTurns) {
          setMaxTurns(config.qualityGates.maxTurns);
        }
      })
      .catch(() => {});
  }, [activeModule]);

  // (Removed legacy sync layout mode since it's fetched on mount)

  // Persist selectedId and selectedRound so quality metrics survive page refresh
  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY_SELECTED_ID, selectedId ?? '');
    } catch {}
  }, [selectedId]);

  //  Fetch the session's structured S2 claims (trailing-debounced so a
  // fresh synthesis persisted mid-stream gets picked up without storming the API) 
  useEffect(() => {
    if (!currentSessionId) return;
    let cancelled = false;
    const timer = setTimeout(() => {
      api.getSessionClaims(currentSessionId)
        .then((res) => {
          if (cancelled) return;
          const byLevel = new Map<number, SessionClaim[]>();
          for (const claim of res.claims) {
            const arr = byLevel.get(claim.level) ?? [];
            arr.push(claim);
            byLevel.set(claim.level, arr);
          }
          setClaimsByLevel(byLevel);
        })
        .catch(() => {});
    }, 400);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [currentSessionId, liveTurns.length]);

  useEffect(() => {
    try {
      localStorage.setItem(STORAGE_KEY_SELECTED_ROUND, selectedRound === null ? 'null' : String(selectedRound));
      // Record which framework the selection belongs to. Round numbers and layer
      // indices are framework-specific, so on a later page load a stored selection
      // is only restored when it was made in the framework now active.
      if (activeModule) localStorage.setItem(STORAGE_KEY_SELECTED_ROUND_MODULE, activeModule);
    } catch {}
  }, [selectedRound, activeModule]);

  //  Listen for mindmap graph node click events 
  useEffect(() => {
    const handleGraphNodeClick = (e: Event) => {
      const detail: GraphNodeClickDetail = (e as CustomEvent).detail;
      if (!detail) return;

      const rawId = detail.id || '';
      const strippedId = rawId.replace(/^node-/, '');
      const detailSpeaker = (detail.agent || detail.label || '').toLowerCase();
      const detailLevel = detail.level !== undefined ? detail.level : 0;
      const detailRound = detailLevel + 1;

      // 1. Match exact ID, or ID with/without 'node-' prefix
      let matched = items.find(
        (i) => i.id === rawId || i.id === strippedId || `node-${i.id}` === rawId || i.id.replace(/^node-/, '') === strippedId
      );

      // 2. Fallback: match by round/level and speaker to retrieve live turns with quality metrics
      if (!matched && detailSpeaker && detailSpeaker !== 'question' && detailSpeaker !== 'system') {
        const candidates = items.filter(
          (i) => i.round === detailRound && i.speaker.toLowerCase() === detailSpeaker
        );
        if (candidates.length > 0) {
          matched = candidates[candidates.length - 1];
        }
      }

      const roleHint = (detail.dialecticRole || '').toLowerCase();
      const isQuestionNode = detailSpeaker === 'question' || detailSpeaker === 'system' || roleHint === 'question' || roleHint === 'system';

      if (matched) {
        setSelectedId(matched.id);
        setInspectorOnlyItem(null);
        setTransientItems((prev) => prev.filter((i) => i.id !== matched.id));
      } else if (isQuestionNode) {
        const inspectorItem: IndexItem = {
          id: detail.id || uniqueGraphItemId('graph-q'),
          cid: isMerkleCid(detail.cid) ? detail.cid : merkleCidFromTurnId(detail.id),
          parentHashes: detail.parentHashes ?? [],
          isEcho: detail.isEcho === true,
          round: detailRound,
          speaker: (detail.agent || detail.label || 'Question').toUpperCase(),
          role: detail.dialecticRole || 'thesis',
          content: detail.content || detail.label || '',
        };
        setInspectorOnlyItem(inspectorItem);
        setSelectedId(null);
      } else {
        const newItem: IndexItem = {
          id: detail.id || uniqueGraphItemId('graph-node'),
          cid: isMerkleCid(detail.cid) ? detail.cid : merkleCidFromTurnId(detail.id),
          parentHashes: detail.parentHashes ?? [],
          isEcho: detail.isEcho === true,
          round: detailRound,
          speaker: (detail.agent || detail.label || 'Agent').toUpperCase(),
          role: detail.dialecticRole || 'thesis',
          content: detail.content || detail.label || '',
        };
        setSelectedId(newItem.id);
        setInspectorOnlyItem(null);
        setTransientItems((prev) => {
          const filtered = prev.filter((i) => i.id !== newItem.id);
          return [...filtered, newItem];
        });
      }
    };

    window.addEventListener('graphNodeClick', handleGraphNodeClick);
    return () => window.removeEventListener('graphNodeClick', handleGraphNodeClick);
  }, [items]);

  //  Listen for openAgentPanel event 
  useEffect(() => {
    const handleOpenAgentPanel = () => {
      setShowAgentPanel(true);
      if (window.innerWidth < 768) {
        setMobileTab('agents');
      }
    };
    window.addEventListener('openAgentPanel', handleOpenAgentPanel);
    return () => window.removeEventListener('openAgentPanel', handleOpenAgentPanel);
  }, []);

  //  Derived state 
  const baseItems = [...items, ...transientItems];
  const allItems = baseItems.map((item) => {
    if (item.claims || item.role !== 'synthesis' || item.level === undefined) return item;
    const claims = claimsByLevel.get(item.level);
    return claims?.length ? { ...item, claims } : item;
  });
  const selectedItem = allItems.find((i) => i.id === selectedId) || inspectorOnlyItem || null;
  const itemIndexMap = new Map(allItems.map((item, i) => [item.id, i]));

  const filteredItems = useMemo(() => {
    if (selectedRound === null) return [];
    
    let filtered = allItems;
    if (selectedRound !== 'all') {
      filtered = allItems.filter(i => i.round === selectedRound);
    }

    return [...filtered].sort((a, b) => {
      if (a.round !== b.round) return a.round - b.round;
      return (itemIndexMap.get(a.id) ?? 0) - (itemIndexMap.get(b.id) ?? 0);
    });
  }, [allItems, selectedRound]);

  // Derive phase and completion from live turns
  const latestTurn = liveTurns.length > 0 ? liveTurns[liveTurns.length - 1] : null;
  const currentPhase = (latestTurn?.dialecticRole ?? 'thesis') as string;
  const maxLevel = Math.max(...liveTurns.map((t) => t.level ?? 0), 0);
  const dialecticianTurns = liveTurns.filter(
    (t) => (t.level ?? 0) === maxLevel && DIALECTICIAN_SPEAKERS.has(t.speaker.toLowerCase())
  );
  const turnsInCurrentRound = Math.ceil(dialecticianTurns.length / 2);
  const isEquilibrium = currentPhase === 'gate' && !latestTurn?.content.toLowerCase().includes('reject');

  const roundLevels = Array.from(new Set(liveTurns.map((t) => t.level ?? 0))).sort((a, b) => a - b);
  const roundButtons = roundLevels.length > 0 ? roundLevels : [0];

  const hasSelectedLevel = selectedRound !== null;
  const activeLevelProp = focusLevelForSelection(selectedRound);

  return (
    <div className="w-full h-full flex flex-col overflow-hidden select-none" style={{ backgroundColor: 'var(--pr-bg)' }}>
      {/*  Mobile Tab Bar (visible < md)  */}
      <div className="flex md:hidden items-center border-b z-20" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
        {(['index', 'graph', 'inspector', 'agents'] as MobileTab[]).map((tab) => (
          <button
            key={tab}
            onClick={() => setMobileTab(tab)}
            className="flex-1 py-3 text-[12px] font-mono uppercase tracking-wider transition-all cursor-pointer border-b-2"
            style={{
              borderColor: mobileTab === tab ? 'var(--pr-role-proposer)' : 'transparent',
              color: mobileTab === tab ? 'var(--pr-text)' : 'var(--pr-text-dim)',
              fontWeight: mobileTab === tab ? 700 : 400,
            }}
          >
            {tab === 'index' ? 'Index' : tab === 'graph' ? 'Graph' : tab === 'inspector' ? 'Inspector' : 'Agents'}
          </button>
        ))}
      </div>

      {/*  Sub-header: Layer / Round Selection Bar (hidden on mobile)  */}
      <div className="hidden md:flex items-center justify-between gap-3 px-6 py-2.5 border-b z-20" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
        <div className="flex items-center gap-2 flex-wrap">
          {/* Label indicating mode */}
          <span className="text-[11px] font-mono uppercase tracking-widest font-bold mr-1" style={{ color: 'var(--pr-text-muted)' }}>
            {'Rounds:'}
          </span>

          {/* Round Pills */}
          <>
            {roundButtons.map((lvl) => {
                const isSelected = selectedRound === lvl + 1;
                return (
                  <button
                    key={lvl}
                    onClick={() => setSelectedRound(isSelected ? null : lvl + 1)}
                    className="flex items-center gap-1.5 px-3 py-1 rounded-lg text-[13px] font-mono transition-all cursor-pointer border"
                    style={{
                      backgroundColor: isSelected ? 'var(--pr-surface)' : 'var(--pr-surface-raised)',
                      borderColor: isSelected ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
                      color: isSelected ? 'var(--pr-text)' : 'var(--pr-text-muted)',
                      boxShadow: isSelected ? '0 0 10px rgba(120,89,26,0.2)' : 'none',
                      fontWeight: isSelected ? 700 : 400,
                    }}
                  >
                    <span>Round {lvl + 1}</span>
                  </button>
                );
              })}

              <button
                onClick={() => setSelectedRound(selectedRound === 'all' ? null : 'all')}
                className="px-3 py-1 rounded-lg text-[13px] font-mono transition-all cursor-pointer border"
                style={{
                  backgroundColor: selectedRound === 'all' ? 'var(--pr-surface)' : 'var(--pr-surface-raised)',
                  borderColor: selectedRound === 'all' ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
                  color: selectedRound === 'all' ? 'var(--pr-text)' : 'var(--pr-text-muted)',
                  boxShadow: selectedRound === 'all' ? '0 0 10px rgba(120,89,26,0.2)' : 'none',
                  fontWeight: selectedRound === 'all' ? 700 : 400,
                }}
              >
                All Rounds
            </button>
          </>
        </div>

        {/* Validation Surface: Alert when no level is selected */}
        {!hasSelectedLevel && (
          <div className="flex items-center gap-2 px-3 py-1 rounded-md text-[12px] font-mono animate-pulse" style={{ backgroundColor: 'rgba(176,90,58,0.15)', borderColor: 'var(--pr-role-question)', color: 'var(--pr-role-question)', border: '1px solid' }}>
            <span className="font-bold">⚠  </span>
            <span>At least 1 level must be selected for the session to be active</span>
          </div>
        )}
      </div>

      {/*  Mobile Round Selector (compact, visible < md)  */}
      <div className="flex md:hidden items-center gap-1.5 px-3 py-2 border-b z-20 overflow-x-auto" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
        {roundButtons.map((lvl) => (
          <button
            key={lvl}
            onClick={() => setSelectedRound(selectedRound === lvl + 1 ? null : lvl + 1)}
            className="px-2.5 py-1 rounded text-[11px] font-mono whitespace-nowrap transition-all cursor-pointer border"
            style={{
              backgroundColor: selectedRound === lvl + 1 ? 'var(--pr-surface)' : 'transparent',
              borderColor: selectedRound === lvl + 1 ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
              color: selectedRound === lvl + 1 ? 'var(--pr-text)' : 'var(--pr-text-dim)',
              fontWeight: selectedRound === lvl + 1 ? 700 : 400,
            }}
          >
            R{lvl + 1}
          </button>
        ))}
        <button
          onClick={() => setSelectedRound(selectedRound === 'all' ? null : 'all')}
          className="px-2.5 py-1 rounded text-[11px] font-mono whitespace-nowrap transition-all cursor-pointer border"
          style={{
            backgroundColor: selectedRound === 'all' ? 'var(--pr-surface)' : 'transparent',
            borderColor: selectedRound === 'all' ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
            color: selectedRound === 'all' ? 'var(--pr-text)' : 'var(--pr-text-dim)',
            fontWeight: selectedRound === 'all' ? 700 : 400,
          }}
        >
          All
        </button>
      </div>

      {/*  Main Workspace  */}
      <div className="flex-1 min-h-0 relative overflow-hidden">
        {isDesktop ? (
          /* Desktop layout */
          <div className="flex w-full h-full">
            <IndexPanel
              items={filteredItems}
              selectedId={selectedId}
              onSelectItem={(item) => { setSelectedId(item.id); setInspectorOnlyItem(null); }}
              currentTurn={turnsInCurrentRound}
              totalTurns={maxTurns}
              currentPhase={currentPhase}
              isEquilibriumReached={isEquilibrium}
            />
            <main className="flex-1 min-w-0 min-h-0 relative overflow-hidden" style={{ backgroundColor: 'var(--pr-bg)' }}>
              <MindMap
                layoutMode={layoutMode}
                activeModule={activeModule}
                activeLevel={activeLevelProp}
              />
            </main>
            <InspectorPanel
              selectedItem={selectedItem}
              metrics={selectedItem?.metrics}
              sessionId={currentSessionId}
            />
            {showAgentPanel && (
              <div className="w-80 min-w-[20rem] border-l overflow-hidden" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)' }}>
                <AgentPanel module={activeModule ?? 'philosophy'} onClose={() => { setShowAgentPanel(false); }} />
              </div>
            )}
          </div>
        ) : (
          /* Mobile layout */
          <div className="flex w-full h-full">
            {mobileTab === 'index' && (
              <IndexPanel
                items={filteredItems}
                selectedId={selectedId}
                onSelectItem={(item) => { setSelectedId(item.id); setInspectorOnlyItem(null); }}
                currentTurn={turnsInCurrentRound}
                totalTurns={maxTurns}
                currentPhase={currentPhase}
                isEquilibriumReached={isEquilibrium}
              />
            )}
            {mobileTab === 'graph' && (
              <main className="w-full h-full relative overflow-hidden" style={{ backgroundColor: 'var(--pr-bg)' }}>
                <MindMap
                  layoutMode={layoutMode}
                  activeModule={activeModule}
                  activeLevel={activeLevelProp}
                />
              </main>
            )}
            {mobileTab === 'inspector' && (
              <InspectorPanel
                selectedItem={selectedItem}
                metrics={selectedItem?.metrics}
                sessionId={currentSessionId}
              />
            )}
            {mobileTab === 'agents' && (
              <div className="w-full h-full overflow-hidden p-4" style={{ backgroundColor: 'var(--pr-bg)' }}>
                <AgentPanel module={activeModule ?? 'philosophy'} onClose={() => { setMobileTab('graph'); setShowAgentPanel(false); }} />
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
