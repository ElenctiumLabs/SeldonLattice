import type { Node, Edge, XYPosition } from 'reactflow';

/** The debate participants (faculty roles; custom modules reuse these). Everything else is a "question" node. */
export type AgentType = 'proposer' | 'skeptic' | 'mediator';
export type DialecticalNodeType = 'question' | AgentType;

export type DialecticRoleType = 'thesis' | 'antithesis' | 'synthesis' | 'interjection' | 'mediation';

/**
 * Shape of a single record as returned by GET /api/nodes. This is the only
 * place that should know about the wire format - everything downstream
 * (GraphBuilder, DialecticalLayout, toReactFlow) works against LayoutNode
 * instead, so the layout engine has no idea ReactFlow or fetch exist.
 */
export interface RawApiNode {
  cid: string;
  nodeType: string;
  level: number;
  content: string;
  cyclePhase?: string;
  parentHash?: string;
  parentHashes?: string[];
  speakerId?: string;
  dialecticRole?: DialecticRoleType;
  turn?: number;
  isSustained?: boolean;
  timestamp?: string;
  sessionId?: string;
  /** True for optimistic nodes injected by the streaming path before Merkle confirms them. */
  isPending?: boolean;
  /**
   * True when this question is a fractal-echo return to the session root, not a fresh
   * ascent rung. The node is still a real question node in the Merkle chain.
   */
  isEcho?: boolean;
  // Negative-dialectic markers (present on synthesis nodes).
  remainingDissensus?: string | null;
  repeatsPremise?: number | null;
  unresolvedTensions?: string[] | null;
  tensionIntensity?: number | null;
  ascentScore?: number;
  joinMediation?: number | null;
  joinNovelty?: number | null;
  irreducibleTension?: number;
  // Per-turn response quality metrics, persisted on every scored node. Null when the
  // quality gates did not run for that node.
  qualityScore?: number | null;
  ceremonyScore?: number | null;
  similarityToPrevious?: number | null;
  newArgumentCount?: number | null;
  wordCount?: number | null;
  hasFarewell?: boolean | null;
  hasAgreement?: boolean | null;
  hasDisagreement?: boolean | null;
  selfSimilarity?: number | null;
}

/** A node inside the layout engine, agnostic of React Flow. */
export interface LayoutNode {
  id: string;
  nodeType: DialecticalNodeType;
  level: number;
  content: string;
  phase?: string;
  speakerId: string;
  dialecticRole: DialecticRoleType;
  parentIds: string[];
  /** Explicit parent hashes as recorded on the node (before heuristic wiring). */
  parentHashes: string[];
  /** Chain links: same-role predecessor at the same level (rendered without arrowheads). */
  chainParentIds: string[];
  children: string[];
  position: XYPosition;
  angle: number;
  radius: number;
  sectorStart: number;
  sectorEnd: number;
  turn?: number;
  timestamp?: string;
  /** True for optimistic nodes injected by the streaming path before Merkle confirms them. */
  isPending?: boolean;
  /** True when this question node marks a fractal-echo return to the session root. */
  isEcho?: boolean;
  /** Session ID - used to scope heuristic wiring so nodes from different sessions are never cross-wired. */
  sessionId?: string;
}

export interface LayoutGraph {
  nodes: Map<string, LayoutNode>;
  roots: LayoutNode[];
}

export interface LayoutResult {
  nodes: Node[];
  edges: Edge[];
}
