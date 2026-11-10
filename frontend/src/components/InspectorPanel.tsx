import React, { useState } from 'react';
import { SparkleIcon, TriangleIcon, DialIcon, GateIcon, ReturnIcon } from './IconSet';
import { roleColor } from '../theme/prime-radiant-tokens';
import { AGENT_ICON_MAP, speakerLabel as speakerLabelFor } from '../config/agentRegistry';
import { SigilViewer } from './SigilViewer';
import type { IndexItem } from './IndexPanel';
import { ProvenanceStrip } from './ProvenanceStrip';
import { isMerkleCid, merkleCidFromTurnId } from '../lib/transcript';

const sentenceAbbreviations = new Set([
  'mr.', 'mrs.', 'ms.', 'dr.', 'prof.', 'sr.', 'jr.', 'st.', 'vs.', 'etc.',
  'e.g.', 'i.e.', 'fig.', 'no.', 'vol.', 'approx.',
]);

/** Adds visual paragraph breaks to long, unbroken prose without changing its text. */
function readableParagraphs(raw: string): string[] {
  const blocks = raw.replace(/\r\n?/g, '\n').split(/\n[\t ]*\n/).map((block) => block.trim()).filter(Boolean);
  return blocks.flatMap((block) => {
    // Keep intentional line breaks, headings, and lists exactly as authored.
    if (block.includes('\n') || /^\s*(?:#{1,6}\s|[-*+]\s|\d+[.)]\s)/.test(block)) return [block];

    const sentenceEnds: number[] = [];
    for (let i = 0; i < block.length; i++) {
      if (!'.!?'.includes(block[i])) continue;
      if (block[i] === '.' && /\d/.test(block[i - 1] ?? '') && /\d/.test(block[i + 1] ?? '')) continue;

      let end = i + 1;
      while (/["'”’»)]/.test(block[end] ?? '')) end++;
      const next = block.slice(end).match(/^\s+(\S)/);
      if (!next) {
        if (end === block.length) sentenceEnds.push(end);
        continue;
      }

      if (block[i] === '.') {
        const preceding = block.slice(0, i + 1).match(/(?:^|\s)([\p{L}.]+)$/u)?.[1]?.toLowerCase();
        if (preceding && (sentenceAbbreviations.has(preceding) || /^[\p{L}]\.$/u.test(preceding))) continue;
      }

      const nextChar = next[1];
      if (/[\p{Lu}\d"'“‘([{]/u.test(nextChar) || block[i] !== '.') sentenceEnds.push(end);
      i = end - 1;
    }

    if (sentenceEnds.length < 4) return [block];

    const paragraphs: string[] = [];
    let start = 0;
    let sentencesInParagraph = 0;
    for (const end of sentenceEnds) {
      sentencesInParagraph++;
      if (sentencesInParagraph === 3 || end - start >= 650) {
        paragraphs.push(block.slice(start, end).trim());
        start = end;
        while (/\s/.test(block[start] ?? '')) start++;
        sentencesInParagraph = 0;
      }
    }
    if (start < block.length) paragraphs.push(block.slice(start).trim());
    return paragraphs.filter(Boolean);
  });
}

const ReadableText: React.FC<{ text: string; className?: string; style?: React.CSSProperties }> = ({ text, className, style }) => (
  <div className={className} style={style}>
    {readableParagraphs(text).map((paragraph, index) => (
      <p key={index} className={index > 0 ? 'mt-3' : undefined}>{paragraph}</p>
    ))}
  </div>
);

export interface QualityMetricsData {
  qualityScore?: number;
  ceremonyScore?: number;
  similarityScore?: number;
  variabilityScore?: number;
  stalemateSignal?: string;
  resonanceThreshold?: number;
  isStalemate?: boolean;
  isSecurityFlagged?: boolean;
  securityThreatReason?: string;
  isSustained?: boolean;
  /** Ascent-quality score. */
  ascentScore?: number;
  joinMediation?: number;
  joinNovelty?: number;
  /** Negative-dialectic measure: how far the synthesis holds the contradiction open. */
  irreducibleTension?: number;
}

interface InspectorPanelProps {
  selectedItem: IndexItem | null;
  metrics?: QualityMetricsData;
  /** Active session; enables provenance verification for the selected node. */
  sessionId?: string | null;
}

const InspectorPanel: React.FC<InspectorPanelProps> = ({
  selectedItem,
  metrics,
  sessionId,
}) => {
  const [expandedSection, setExpandedSection] = useState<'content' | 'markers' | 'provenance' | 'sigil' | null>('content');
  const [claimsExpanded, setClaimsExpanded] = useState(false);
  const [reasoningExpanded, setReasoningExpanded] = useState(false);
  const toggleSection = (section: 'content' | 'markers' | 'provenance' | 'sigil') => {
    setExpandedSection((current) => current === section ? null : section);
  };
  const renderIcon = (role: string, speaker: string, size = 16) => {
    if (!speaker && !role) return null;
    const s = speaker.toLowerCase();

    if (s === 'system' || s === 'question') return <SparkleIcon size={size} color={roleColor('question')} />;
    if (s === 'gate' || role.toLowerCase() === 'gate') return <GateIcon size={size} color="#e74c3c" />;

    const agentSpec = AGENT_ICON_MAP[s];
    if (agentSpec) {
      const Icon = agentSpec.icon;
      return <Icon size={size} color={roleColor(agentSpec.accentColorRole)} />;
    }

    if (role.toLowerCase() === 'synthesis') return <DialIcon size={size} color={roleColor('proposer')} />;
    return <TriangleIcon size={size} color={roleColor('proposer')} />;
  };

  const metricsUnavailable = !metrics;

  /** Simple deterministic hash hex string to use as sigil CID */
  const contentToCid = (item: IndexItem): string => {
    const input = `${item.id}:${item.content}`;
    let h = 0x811c9dc5;
    for (let i = 0; i < input.length; i++) {
      h ^= input.charCodeAt(i);
      h = (h * 0x01000193) >>> 0;
    }
    const hex = h.toString(16).padStart(8, '0');
    const rev = hex.split('').reverse().join('');
    // Mirror to 52 chars like a CIDv1
    return hex + rev + hex + rev.substring(0, 12);
  };

  /**
   * Real Merkle address for the selected node. Persisted nodes carry `cid`
   * directly; reconstructed turns also encode it in their `node-<cid>` id.
   * Optimistic/streaming nodes have neither, so they are shown as pending.
   */
  const provenanceCid = selectedItem
    ? isMerkleCid(selectedItem.cid)
      ? selectedItem.cid
      : merkleCidFromTurnId(selectedItem.id)
    : undefined;

  const qualityScore = metricsUnavailable ? undefined : metrics.qualityScore;
  const ceremonyScore = metricsUnavailable ? undefined : metrics.ceremonyScore;
  const similarityScore = metricsUnavailable ? undefined : metrics.similarityScore;
  const stalemateSignal = metricsUnavailable ? undefined : metrics.stalemateSignal;
  const ascentScore = metricsUnavailable ? undefined : metrics.ascentScore;
  const joinMediation = metricsUnavailable ? undefined : metrics.joinMediation;
  const joinNovelty = metricsUnavailable ? undefined : metrics.joinNovelty;
  const irreducibleTension = metricsUnavailable ? undefined : metrics.irreducibleTension;

  const ScoreBar = ({ value }: { value: number | undefined }) =>
    value !== undefined ? (
      <>
        <span style={{ color: 'var(--pr-role-judge)' }}>{value.toFixed(1)}%</span>
        <div className="w-full rounded-full h-1.5 overflow-hidden" style={{ backgroundColor: 'var(--pr-surface)' }}>
          <div
            className="h-full rounded-full transition-all duration-500"
            style={{ width: `${Math.min(100, Math.max(0, value))}%`, background: 'var(--pr-role-judge)' }}
          />
        </div>
      </>
    ) : (
      <span className="text-[11px] italic" style={{ color: 'var(--pr-text-dim)' }}>- Metrics unavailable</span>
    );

  const speakerLower = selectedItem?.speaker?.toLowerCase() ?? '';
  const agentSpec = AGENT_ICON_MAP[speakerLower];
  const speakerColor = agentSpec ? roleColor(agentSpec.accentColorRole) : roleColor('proposer');
  const speakerLabel = speakerLabelFor(selectedItem?.speaker, undefined, selectedItem?.role) || 'Unknown';

  return (
    <aside className="w-full md:w-60 lg:w-120 min-w-130 h-full flex flex-col border-l select-none flex-0" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)', color: 'var(--pr-text)' }}>
      {/* Inspector Header */}
      <div className="px-4 py-3 border-b flex items-center justify-between" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
        <h2 className="text-[13px] font-mono tracking-widest uppercase font-bold" style={{ color: 'var(--pr-text)' }}>INSPECTOR</h2>
      </div>

      {/* Expanded Text Content Area */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3 custom-scrollbar" style={{ backgroundColor: 'var(--pr-bg)' }}>
        {selectedItem ? (
          <div>
            {/* Speaker & Role Header */}
            <div className="flex items-center gap-2 mb-3 pb-2 border-b" style={{ borderColor: 'var(--pr-border)' }}>
              <div className="p-2.5 rounded-lg border-2 shadow-md" style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: speakerColor, boxShadow: `0 0 12px ${speakerColor}55` }}>
                {selectedItem.isEcho
                  ? <ReturnIcon size={22} color={roleColor('question')} />
                  : renderIcon(selectedItem.role, selectedItem.speaker, 22)}
              </div>
              <div>
                <div className="text-[13px] font-mono font-bold uppercase tracking-wider" style={{ color: speakerColor }}>
                  {speakerLabel}
                </div>
                <div className="text-[11px] font-mono" style={{ color: 'var(--pr-text-muted)' }}>
                  Round {selectedItem.round} · {selectedItem.role}
                </div>
                {selectedItem.isEcho && (
                  <div className="text-[10px] font-mono uppercase tracking-widest" style={{ color: roleColor('question') }}>
                    Return to root
                  </div>
                )}
              </div>
            </div>

            <button
              onClick={() => toggleSection('content')}
              aria-expanded={expandedSection === 'content'}
              className="w-full mb-3 flex items-center justify-between rounded-md border px-3 py-2 text-[11px] font-mono uppercase tracking-widest font-bold transition-colors cursor-pointer"
              style={{ color: 'var(--pr-text)', backgroundColor: 'var(--pr-surface-raised)', borderColor: expandedSection === 'content' ? 'var(--pr-role-judge)' : 'var(--pr-border)' }}
            >
              <span>RESPONSE TEXT & DETAILS</span>
              <span aria-hidden="true" className="text-base leading-none">{expandedSection === 'content' ? '−' : '+'}</span>
            </button>

            {expandedSection === 'content' && <>

            {/* Security Alert Badge */}
            {metrics?.isSecurityFlagged && (
              <div className="mb-3 p-3 rounded font-mono text-[12px] space-y-1" style={{ backgroundColor: 'rgba(176,90,58,0.15)', borderColor: 'var(--pr-role-question)', color: 'var(--pr-role-question)', border: '1px solid' }}>
                <div className="font-bold flex items-center gap-1 uppercase tracking-wider">
                  ⚠️ SECURITY ALERT: PROMPT INJECTION VECTOR FLAGGED
                </div>
                <div>Reason: {metrics.securityThreatReason || 'Prompt Injection Signature / Lattice-Space Activation Detected'}</div>
                <div className="text-[11px] opacity-80">This node triggered an automated security interception and was quarantined.</div>
              </div>
            )}

            {/* Sustained Outcome Badge */}
            {metrics?.isSustained && (
              <div className="mb-3 p-3 rounded font-mono text-[12px] space-y-1" style={{ backgroundColor: 'rgba(120,89,26,0.15)', borderColor: 'var(--pr-role-proposer)', color: 'var(--pr-role-proposer)', border: '1px solid' }}>
                <div className="font-bold uppercase tracking-wider">
                  SUSTAINED THEME
                </div>
                <div>The ascension gate rejected every candidate next theme; the dialectic remained on the current theme instead of forcing a low-confidence ascent. Flagged for human review.</div>
              </div>
            )}

            {/* Full Expanded Text */}
            <ReadableText
              text={selectedItem.content || '(No response text recorded)'}
              className="text-[14px] font-serif leading-relaxed whitespace-pre-wrap selection:bg-(--pr-role-judge)/20"
              style={{ color: 'var(--pr-text)' }}
            />

            {/* Structured Claims Appendix - S2 substrate, synthesis nodes only */}
            {selectedItem.role?.toLowerCase() === 'synthesis' && !!selectedItem.claims?.length && (
              <div className="mt-4 pt-3 border-t" style={{ borderColor: 'var(--pr-border)' }}>
                <button
                  onClick={() => setClaimsExpanded(!claimsExpanded)}
                  className="w-full flex items-center justify-between text-[11px] font-mono uppercase tracking-widest transition-colors cursor-pointer"
                  style={{ color: 'var(--pr-text-muted)' }}
                >
                  <span>STRUCTURED CLAIMS ({selectedItem.claims.length})</span>
                  <span className="text-[10px]">{claimsExpanded ? '▾' : '▸'}</span>
                </button>
                {claimsExpanded && (
                  <div className="mt-3 space-y-2">
                    {selectedItem.claims.map((claim, i) => (
                      <div key={i} className="p-2.5 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
                        <div className="flex items-start justify-between gap-2">
                          <span className="text-[12.5px] font-serif leading-snug" style={{ color: 'var(--pr-text)' }}>
                            {claim.claim}
                          </span>
                          {claim.confidence !== undefined && (
                            <span className="shrink-0 text-[10px] font-mono font-bold" style={{ color: 'var(--pr-role-judge)' }}>
                              {Math.round(claim.confidence * 100)}%
                            </span>
                          )}
                        </div>
                        <div className="mt-1.5 h-1 rounded-full overflow-hidden" style={{ backgroundColor: 'var(--pr-bg)' }}>
                          <div className="h-full rounded-full" style={{ width: `${Math.min(100, Math.max(0, (claim.confidence ?? 0) * 100))}%`, backgroundColor: 'var(--pr-role-judge)' }} />
                        </div>
                        {!!claim.supportedBy?.length && (
                          <div className="mt-2 flex flex-wrap gap-1">
                            {claim.supportedBy.map((src) => (
                              <span key={src} className="px-1.5 py-0.5 rounded text-[10px] font-mono" style={{ backgroundColor: 'var(--pr-surface-raised)', color: 'var(--pr-text-muted)' }}>
                                {src}
                              </span>
                            ))}
                          </div>
                        )}
                        {!!claim.falsifiedBy && (
                          <div className="mt-2 text-[11px] font-mono leading-snug" style={{ color: 'var(--pr-role-question)' }}>
                            <span className="uppercase tracking-wider text-[9px]" style={{ color: 'var(--pr-text-dim)' }}>FALSIFIED BY: </span>
                            {claim.falsifiedBy}
                          </div>
                        )}
                        {!!claim.synthesisHash && (
                          <div className="mt-1.5 text-[9px] font-mono truncate" style={{ color: 'var(--pr-text-dim)' }} title={claim.synthesisHash}>
                            {claim.synthesisHash}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            )}

            {/* Reasoning Trace - the model's deliberation record, when persisted */}
            {selectedItem?.reasoningTrace && (
              <div className="mt-4 pt-3 border-t" style={{ borderColor: 'var(--pr-border)' }}>
                <button
                  onClick={() => setReasoningExpanded(!reasoningExpanded)}
                  className="w-full flex items-center justify-between text-[11px] font-mono uppercase tracking-widest transition-colors cursor-pointer"
                  style={{ color: 'var(--pr-text-muted)' }}
                >
                  <span>REASONING TRACE</span>
                  <span className="text-[10px]">{reasoningExpanded ? '▾' : '▸'}</span>
                </button>
                {reasoningExpanded && (
                  <div className="mt-3 p-2.5 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
                    <ReadableText text={selectedItem.reasoningTrace} className="text-[11px] font-mono leading-relaxed whitespace-pre-wrap" style={{ color: 'var(--pr-text-muted)' }} />
                  </div>
                )}
              </div>
            )}

            </>}

            {/* Negative Dialectic Markers - the moderator's own report */}
            {(selectedItem?.remainingDissensus || selectedItem?.repeatsPremise != null || selectedItem?.unresolvedTensions?.length) && (
              <div className="mt-4 pt-3 border-t" style={{ borderColor: 'var(--pr-border)' }}>
                <button
                  onClick={() => toggleSection('markers')}
                  aria-expanded={expandedSection === 'markers'}
                  className="w-full flex items-center justify-between rounded-md border px-3 py-2 text-[11px] font-mono uppercase tracking-widest font-bold transition-colors cursor-pointer"
                  style={{ color: 'var(--pr-text)', backgroundColor: 'var(--pr-surface-raised)', borderColor: expandedSection === 'markers' ? 'var(--pr-role-judge)' : 'var(--pr-border)' }}
                >
                  <span>NEGATIVE DIALECTIC MARKERS</span>
                  <span aria-hidden="true" className="text-base leading-none">{expandedSection === 'markers' ? '−' : '+'}</span>
                </button>
                {expandedSection === 'markers' && (
                  <div className="mt-3 space-y-2">
                    {selectedItem.remainingDissensus && (
                      <div className="p-2.5 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
                        <span className="block text-[9px] font-mono uppercase tracking-wider mb-1" style={{ color: 'var(--pr-text-dim)' }}>REMAINING DISSENSUS</span>
                        <span className="text-[12px] font-serif leading-snug" style={{ color: 'var(--pr-text)' }}>{selectedItem.remainingDissensus}</span>
                      </div>
                    )}
                    {selectedItem.repeatsPremise != null && (
                      <div className="p-2.5 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
                        <span className="block text-[9px] font-mono uppercase tracking-wider mb-1" style={{ color: 'var(--pr-text-dim)' }}>REPEATS PREMISE</span>
                        <span className="text-[12px] font-mono font-bold" style={{ color: 'var(--pr-role-judge)' }}>{Math.round(selectedItem.repeatsPremise * 100)}%</span>
                        <div className="mt-1.5 h-1 rounded-full overflow-hidden" style={{ backgroundColor: 'var(--pr-bg)' }}>
                          <div className="h-full rounded-full" style={{ width: `${Math.min(100, Math.max(0, selectedItem.repeatsPremise * 100))}%`, backgroundColor: 'var(--pr-role-judge)' }} />
                        </div>
                      </div>
                    )}
                    {selectedItem.unresolvedTensions && selectedItem.unresolvedTensions.length > 0 && (
                      <div className="p-2.5 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
                        <span className="block text-[9px] font-mono uppercase tracking-wider mb-1" style={{ color: 'var(--pr-text-dim)' }}>
                          UNRESOLVED TENSIONS {selectedItem.tensionIntensity != null && `(INTENSITY ${selectedItem.tensionIntensity.toFixed(2)})`}
                        </span>
                        <ul className="space-y-1">
                          {selectedItem.unresolvedTensions.map((t, i) => (
                            <li key={i} className="text-[11px] font-mono leading-snug" style={{ color: 'var(--pr-text-muted)' }}>• {t}</li>
                          ))}
                        </ul>
                      </div>
                    )}
                  </div>
                )}
              </div>
            )}

            {/* Provenance: Merkle CID, lineage and signature verification */}
            <div className="mt-4 pt-3 border-t" style={{ borderColor: 'var(--pr-border)' }}>
              <button
                onClick={() => toggleSection('provenance')}
                aria-expanded={expandedSection === 'provenance'}
                className="w-full flex items-center justify-between rounded-md border px-3 py-2 text-[11px] font-mono uppercase tracking-widest font-bold transition-colors cursor-pointer"
                style={{ color: 'var(--pr-text)', backgroundColor: 'var(--pr-surface-raised)', borderColor: expandedSection === 'provenance' ? 'var(--pr-role-judge)' : 'var(--pr-border)' }}
              >
                <span>PROVENANCE</span>
                <span aria-hidden="true" className="text-base leading-none">{expandedSection === 'provenance' ? '−' : '+'}</span>
              </button>
              {expandedSection === 'provenance' && (
                <div className="mt-3 space-y-3">
                  <ProvenanceStrip
                    cid={provenanceCid}
                    parentHashes={selectedItem.parentHashes}
                    sessionId={sessionId}
                  />
                </div>
              )}
            </div>

            {/* Cryptographic sigil is an independent panel so it can be expanded on its own. */}
            <div className="mt-4 pt-3 border-t" style={{ borderColor: 'var(--pr-border)' }}>
              <button
                onClick={() => toggleSection('sigil')}
                aria-expanded={expandedSection === 'sigil'}
                className="w-full flex items-center justify-between rounded-md border px-3 py-2 text-[11px] font-mono uppercase tracking-widest font-bold transition-colors cursor-pointer"
                style={{ color: 'var(--pr-text)', backgroundColor: 'var(--pr-surface-raised)', borderColor: expandedSection === 'sigil' ? 'var(--pr-role-judge)' : 'var(--pr-border)' }}
              >
                <span>CRYPTOGRAPHIC SIGIL</span>
                <span aria-hidden="true" className="text-base leading-none">{expandedSection === 'sigil' ? '−' : '+'}</span>
              </button>
              {expandedSection === 'sigil' && (
                <div className="mt-3">
                  <SigilViewer cid={contentToCid(selectedItem)} />
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="h-full flex items-center justify-center text-center text-xs font-mono italic" style={{ color: 'var(--pr-text-dim)' }}>
            Select a mindmap node or index entry to inspect full response text
          </div>
        )}
      </div>

      {/* Telemetry & Quality Scoring Area at Bottom */}
      <div className="p-4 border-t space-y-3" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
        <div className="flex items-center justify-between text-[12px] font-mono uppercase tracking-wider" style={{ color: 'var(--pr-text)' }}>
          <span>QUALITY METRICS</span>
          <ScoreBar value={qualityScore} />
        </div>

        {/* Telemetry Metric Grid */}
        <div className="grid grid-cols-3 gap-2 text-[11px] font-mono pt-1">
          <div className="p-2 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <span className="block" style={{ color: 'var(--pr-text-muted)' }}>CEREMONY</span>
            {ceremonyScore !== undefined ? (
              <span className="font-bold" style={{ color: 'var(--pr-text)' }}>{ceremonyScore.toFixed(1)} / 8.0</span>
            ) : (
              <span className="italic" style={{ color: 'var(--pr-text-dim)' }}>n/a</span>
            )}
          </div>

          <div className="p-2 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <span className="block" style={{ color: 'var(--pr-text-muted)' }}>SIMILARITY</span>
            {similarityScore !== undefined ? (
              <span className="font-bold" style={{ color: 'var(--pr-text)' }}>{similarityScore.toFixed(2)} / 0.85</span>
            ) : (
              <span className="italic" style={{ color: 'var(--pr-text-dim)' }}>n/a</span>
            )}
          </div>

          <div className="p-2 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <span className="block" style={{ color: 'var(--pr-text-muted)' }}>ASCENT</span>
            {ascentScore !== undefined ? (
              <span className="font-bold" style={{ color: 'var(--pr-text)' }}>{ascentScore.toFixed(2)}</span>
            ) : (
              <span className="italic" style={{ color: 'var(--pr-text-dim)' }}>n/a</span>
            )}
          </div>

          <div className="p-2 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <span className="block" style={{ color: 'var(--pr-text-muted)' }}>JOIN MEDIATION</span>
            {joinMediation !== undefined ? <span className="font-bold" style={{ color: 'var(--pr-text)' }}>{joinMediation.toFixed(3)}</span> : <span className="italic" style={{ color: 'var(--pr-text-dim)' }}>n/a</span>}
          </div>

          <div className="p-2 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <span className="block" style={{ color: 'var(--pr-text-muted)' }}>JOIN NOVELTY</span>
            {joinNovelty !== undefined ? <span className="font-bold" style={{ color: 'var(--pr-text)' }}>{joinNovelty.toFixed(3)}</span> : <span className="italic" style={{ color: 'var(--pr-text-dim)' }}>n/a</span>}
          </div>

          <div className="p-2 rounded border" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
            <span className="block" style={{ color: 'var(--pr-text-muted)' }}>TENSION</span>
            {irreducibleTension !== undefined ? (
              <span className="font-bold" style={{ color: 'var(--pr-text)' }}>{irreducibleTension.toFixed(2)}</span>
            ) : (
              <span className="italic" style={{ color: 'var(--pr-text-dim)' }}>n/a</span>
            )}
          </div>
        </div>

        {/* Center Stalemate Signal / Status Text */}
        <div className="p-2 rounded border text-center" style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}>
          <span className="text-[10px] font-mono uppercase tracking-widest block mb-0.5" style={{ color: 'var(--pr-text-dim)' }}>
            DIALECTICAL STATUS
          </span>
          {selectedItem?.role?.toLowerCase() === 'synthesis' ? (
            <span className="text-[12px] font-mono font-bold tracking-wide" style={{ color: 'var(--pr-role-judge)' }}>
              {metrics?.isSustained ? 'Sustained Theme' : 'Synthesis'}
            </span>
          ) : stalemateSignal !== undefined ? (
            <span
              className={`text-[12px] font-mono font-bold tracking-wide ${
                metrics?.isStalemate ? 'text-[#e74c3c]' : 'text-(--pr-role-judge)'
              }`}
            >
              {stalemateSignal}
            </span>
          ) : selectedItem?.phase ? (
            <span className="text-[12px] font-mono font-bold tracking-wide" style={{ color: 'var(--pr-role-judge)' }}>
              {selectedItem.phase}
            </span>
          ) : (
            <span className="text-[11px] italic" style={{ color: 'var(--pr-text-dim)' }}>No evaluation</span>
          )}
        </div>
      </div>
    </aside>
  );
};
export default InspectorPanel
