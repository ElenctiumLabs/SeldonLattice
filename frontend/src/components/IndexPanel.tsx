import React, { memo } from 'react';
import { SparkleIcon, TriangleIcon, DialIcon, GateIcon, ReturnIcon } from './IconSet';
import { CycleDial } from './CycleDial';
import { roleColor, roleOf } from '../theme/prime-radiant-tokens';
import { AGENT_ICON_MAP } from '../config/agentRegistry';
import { getConfig } from '../lib/config';
import type { SessionClaim } from '../lib/api';
import type { QualityMetricsData } from './InspectorPanel';

export interface IndexItem {
  id: string;
  round: number;
  speaker: string;
  role: 'thesis' | 'antithesis' | 'synthesis' | 'gate' | 'system' | string;
  /** Merkle CID of the persisted node; absent while the node is optimistic. */
  cid?: string;
  /** Parent hashes recorded on the persisted node (Merkle lineage). */
  parentHashes?: string[];
  /** True when this entry is a fractal-echo return to the session root. */
  isEcho?: boolean;
  categorization?: string;
  content: string;
  roundQuestion?: string;
  isGateRejected?: boolean;
  metrics?: QualityMetricsData;
  phase?: string;
  /** Zero-based ascent level (round - 1) for associating claims/metrics. */
  level?: number;
  /** Structured S2 claims extracted for a synthesis node's level, when available. */
  claims?: SessionClaim[];
  /** Model reasoning/thinking trace for this node, if persisted. */
  reasoningTrace?: string | null;
  /** Moderator's report of the sharpest live tension this synthesis does not close. */
  remainingDissensus?: string | null;
  /** Moderator's self-assessment of restatement. */
  repeatsPremise?: number | null;
  /** Tensions that survived mediation. */
  unresolvedTensions?: string[] | null;
  /** Intensity of the unresolved tensions. */
  tensionIntensity?: number | null;
}

interface IndexPanelProps {
  items: IndexItem[];
  selectedId: string | null;
  onSelectItem: (item: IndexItem) => void;
  currentTurn?: number;
  totalTurns?: number;
  currentPhase?: string;
  isEquilibriumReached?: boolean;
}

/** Dynamically calculates a concise contextual categorisation / pointer from response content and metadata */
export function deriveCategorization(item: IndexItem): string {
  if (item.categorization) return item.categorization;

  const speakerLower = item.speaker.toLowerCase();
  const roleLower = item.role.toLowerCase();
  const contentLower = item.content.toLowerCase();

  // 1. Role / Speaker specific keywords
  if (speakerLower === 'system') {
    return item.round === 1 ? 'Opening question' : 'Generalizing the question';
  }

  if (speakerLower === 'gate' || roleLower === 'gate') {
    if (item.isGateRejected || contentLower.includes('reject') || contentLower.includes('drift')) {
      return 'Validation rejected - scope drift';
    }
    return 'Equilibrium achieved';
  }

  // 2. Semantic concept extraction from response text
  if (contentLower.includes('flame') && contentLower.includes('smith')) {
    return 'Flame vs. smith distinction';
  }
  if (contentLower.includes('virtue') && contentLower.includes('excess')) {
    return 'Virtue as excess';
  }
  if (contentLower.includes('object-directed') || contentLower.includes('appetitive')) {
    return 'Object-directedness';
  }
  if (contentLower.includes('objectless') && contentLower.includes('vice')) {
    return 'Counterexample: objectless vice';
  }
  if (contentLower.includes('lyre') || (contentLower.includes('cadence') && contentLower.includes('harmony'))) {
    return 'Harmony / false-cadence metaphor';
  }
  if (contentLower.includes('unconscious') || contentLower.includes('archetype')) {
    return 'Archetypal shadow integration';
  }
  if (contentLower.includes('will to power') || contentLower.includes('perspectivism')) {
    return 'Perspectivist re-valuation';
  }

  // 3. Fallback extraction: extract key phrase from first sentence
  const trimmed = item.content.replace(/^```[a-z]*\s*/i, '').trim();
  const firstLine = trimmed.split('\n')[0];
  const shortClause = firstLine.split(/[.:;!?]/)[0].trim();
  if (shortClause && shortClause.length > 5 && shortClause.length < 35) {
    return shortClause;
  }
  return shortClause ? shortClause.substring(0, 60) + '...' : 'Contextual response';
}

export const IndexPanel: React.FC<IndexPanelProps> = memo(({
  items,
  selectedId,
  onSelectItem,
  currentTurn = 0,
  totalTurns = getConfig().defaultCycleTotalTurns,
  currentPhase = 'thesis',
  isEquilibriumReached = false,
}) => {
  // Group items by round, sorting Question/System to the top of each round
  const rounds = React.useMemo(() => {
    const map = new Map<number, IndexItem[]>();
    items.forEach((item) => {
      if (!map.has(item.round)) map.set(item.round, []);
      map.get(item.round)!.push(item);
    });
    for (const [, roundItems] of map) {
      roundItems.sort((a, b) => {
        const aPinned = a.speaker.toLowerCase() === 'system' || a.speaker.toLowerCase() === 'question';
        const bPinned = b.speaker.toLowerCase() === 'system' || b.speaker.toLowerCase() === 'question';
        if (aPinned && !bPinned) return -1;
        if (!aPinned && bPinned) return 1;
        return 0;
      });
    }
    return Array.from(map.entries()).sort(([a], [b]) => a - b);
  }, [items]);

  const renderIcon = (role: string, speaker: string, isEcho = false) => {
    const s = speaker.toLowerCase();
    const dialecticRole = roleOf(s);

    if (isEcho) return <ReturnIcon size={18} color={roleColor('question')} />;
    if (s === 'system' || s === 'question') return <SparkleIcon size={18} color={roleColor('question')} />;
    if (s === 'gate' || role.toLowerCase() === 'gate') return <GateIcon size={18} color="#e74c3c" />;

    const agentSpec = AGENT_ICON_MAP[s];
    if (agentSpec) {
      const Icon = agentSpec.icon;
      return <Icon size={18} color={roleColor(agentSpec.accentColorRole)} />;
    }

    if (role.toLowerCase() === 'synthesis') return <DialIcon size={18} color={roleColor('proposer')} />;
    return <TriangleIcon size={18} color={roleColor(dialecticRole)} />;
  };

  return (
    <aside className="w-full md:w-55 lg:w-95 min-w-75 h-full flex flex-col border-r select-none flex-0" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)', color: 'var(--pr-text)' }}>
      {/* Index Header */}
      <div className="px-4 py-3 border-b flex items-center justify-between" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
        <h2 className="text-[13px] font-mono font-bold tracking-widest uppercase" style={{ color: 'var(--pr-role-proposer)' }}>INDEX</h2>
        <span className="text-[11px] font-mono" style={{ color: 'var(--pr-text-muted)' }}>
          {items.filter(item => {
            const s = item.speaker.toLowerCase();
            return s !== 'system' && s !== 'question';
          }).length} turns
        </span>
      </div>

      {/* Scrollable List of Brief Categorisation Pointers */}
      <div className="flex-1 overflow-y-auto px-3 py-3 space-y-4 custom-scrollbar" style={{ backgroundColor: 'var(--pr-bg)' }}>
        {rounds.length === 0 ? (
          <div className="px-3 py-8 text-center text-[13px] font-mono rounded-lg" style={{ color: 'var(--pr-text-dim)', border: '1px dashed var(--pr-border)' }}>
            No turns recorded yet
          </div>
        ) : (
          rounds.map(([roundNum, roundItems]) => {
            const firstQuestion = roundItems.find((i) => i.roundQuestion)?.roundQuestion || 'Dialectical ascent';
            return (
              <div key={roundNum} className="space-y-1.5">
                {/* Round Header */}
                <div className="px-2.5 py-1 text-[12px] font-mono uppercase tracking-wider flex items-center justify-between border-b pb-1" style={{ borderColor: 'var(--pr-border)' }}>
                  <span className="font-bold" style={{ color: 'var(--pr-role-proposer)' }}>ROUND {roundNum}</span>
                  <span className="truncate max-w-42.5 text-[11px]" style={{ color: 'var(--pr-text-muted)' }} title={firstQuestion}>
                    · {firstQuestion}
                  </span>
                </div>

                {/* Items in Round */}
                {roundItems.map((item) => {
                  const isSelected = item.id === selectedId;
                  const isRejected = item.isGateRejected || (item.speaker.toLowerCase() === 'gate' && item.content.toLowerCase().includes('reject'));
                  const pointerText = deriveCategorization(item);

                  return (
                    <button
                      key={item.id}
                      onClick={() => onSelectItem(item)}
                      className="w-full text-left px-3 py-2.5 rounded-lg transition-all duration-150 flex items-start gap-3 border cursor-pointer"
                      style={{
                        backgroundColor: isSelected ? 'var(--pr-surface)' : 'var(--pr-surface)',
                        borderColor: isSelected ? 'var(--pr-role-proposer)' : 'var(--pr-border)',
                        boxShadow: isSelected ? '0 0 12px rgba(120,89,26,0.15)' : 'none',
                      }}
                    >
                      <div className="mt-0.5 flex-0">
                        {renderIcon(item.role, item.speaker, item.isEcho)}
                      </div>

                      <div className="flex-1 min-w-0">
                        <div className="flex items-center justify-between gap-1.5 flex-wrap">
                          <span className="text-[13px] font-mono font-bold tracking-wider uppercase" style={{ color: 'var(--pr-text)' }}>
                            {item.speaker}
                          </span>
                          {item.isEcho && (
                            <span
                              className="text-[9px] font-mono tracking-wider uppercase px-1.5 py-0.5 rounded border"
                              style={{ color: roleColor('question'), borderColor: roleColor('question') }}
                              title="The inquiry returned to the root question"
                            >
                              Return
                            </span>
                          )}
                          <span
                            className="text-[10px] font-mono tracking-wider uppercase px-1.5 py-0.5 rounded border"
                            style={{
                              color: item.role.toLowerCase() === 'synthesis'
                                ? roleColor('proposer')
                                : item.role.toLowerCase() === 'thesis'
                                ? roleColor(roleOf(item.speaker))
                                : item.role.toLowerCase() === 'gate'
                                ? '#e74c3c'
                                : roleColor(roleOf(item.speaker)),
                              borderColor: (item.role.toLowerCase() === 'synthesis'
                                ? roleColor('proposer')
                                : item.role.toLowerCase() === 'gate'
                                ? '#e74c3c'
                                : roleColor(roleOf(item.speaker))) + '40',
                              backgroundColor: (item.role.toLowerCase() === 'synthesis'
                                ? roleColor('proposer')
                                : item.role.toLowerCase() === 'gate'
                                ? '#e74c3c'
                                : roleColor(roleOf(item.speaker))) + '18',
                            }}
                          >
                            {item.role}
                          </span>
                        </div>

                        <p
                          className={`text-[13px] font-serif mt-1 line-clamp-2 leading-snug ${
                            isRejected ? 'text-[#e74c3c] font-medium' : ''
                          }`}
                          style={{ color: isRejected ? '#e74c3c' : 'var(--pr-text)' }}
                        >
                          {pointerText}
                        </p>
                      </div>
                    </button>
                  );
                })}
              </div>
            );
          })
        )}
      </div>

      {/* Spinning Dial at bottom of Left Panel */}
      <CycleDial
        currentTurn={currentTurn}
        totalTurns={totalTurns}
        currentPhase={currentPhase}
        isComplete={isEquilibriumReached}
      />
    </aside>
  );
});
