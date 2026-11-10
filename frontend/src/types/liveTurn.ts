export interface LiveTurnMetrics {
  qualityScore?: number;
  ceremonyScore?: number;
  similarityScore?: number;
  variabilityScore?: number;
  stalemateSignal?: string;
  isStalemate?: boolean;
  /**
   * Ascent-quality score (0.0–1.0). Persisted on the synthesis node, so unlike
   * qualityScore/ceremonyScore/similarityScore (stream-only) it survives a reload.
   */
  ascentScore?: number;
  /** Concept-lattice distance of the synthesis from the debate poles' midpoint. */
  joinMediation?: number;
  /** Concept-lattice novelty relative to both debate poles. */
  joinNovelty?: number;
  isSustained?: boolean;
  /**
   * Negative-dialectic measure (0.0–1.0). Also persisted on the synthesis node and
   * therefore available on a cold load.
   */
  irreducibleTension?: number;
  isSecurityFlagged?: boolean;
  securityThreatReason?: string;
  /** New arguments / counterexamples / questions counted in the reply. */
  newArgumentCount?: number;
  /** Word count of the reply. */
  wordCount?: number;
  /** Whether the reply contained farewell language. */
  hasFarewell?: boolean;
  /** Whether the reply contained agreement markers. */
  hasAgreement?: boolean;
  /** Whether the reply contained disagreement indicators. */
  hasDisagreement?: boolean;
  /** Similarity to the same agent's own previous reply (0.0–1.0). */
  selfSimilarity?: number;
}

export interface LiveTurn {
  id: string;
  speaker: string;
  content: string;
  dialecticRole: 'thesis' | 'antithesis' | 'synthesis' | 'gate' | 'mediation' | string;
  level?: number;
  timestamp: number;
  /** Merkle CID of the persisted node. Absent for optimistic/streaming turns. */
  cid?: string;
  /** Parent hashes recorded on the persisted node (Merkle lineage). */
  parentHashes?: string[];
  /** True when this turn marks a fractal-echo return to the session root. */
  isEcho?: boolean;
  metrics?: LiveTurnMetrics;
  phase?: string;
  isSustained?: boolean;
  reasoningTrace?: string | null;
  remainingDissensus?: string | null;
  repeatsPremise?: number | null;
  unresolvedTensions?: string[] | null;
  tensionIntensity?: number | null;
}

export interface GraphNodeClickDetail {
  id?: string;
  /** Merkle CID of the underlying node (real only for persisted nodes). */
  cid?: string;
  parentHashes?: string[];
  /** True when the clicked node is a fractal-echo return to the root. */
  isEcho?: boolean;
  content?: string;
  label?: string;
  agent?: string;
  level?: number;
  dialecticRole?: string;
}
