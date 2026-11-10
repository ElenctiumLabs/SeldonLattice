/**
 * Centralized agent registry - single source of truth for agent classifications.
 * Every file that needs to know "is this agent a thesis speaker?" or "which node types
 * are synthesis types?" should import from here instead of duplicating arrays.
 *
 * Agents are faculty roles, not people:
 * the engine serves the philosophy module, whose debate is composed from
 * proposer / skeptic / mediator (+ shadow) - with runtime-created custom modules free to
 * reuse any of these roles.
 */

import type { IconComponentType } from '../components/IconSet';
import {
  DialIcon,
  ScaleIcon,
  TrendingUpIcon,
} from '../components/IconSet';
import type { DialecticalRole } from '../theme/prime-radiant-tokens';

// -- All agents by module --

/** Canonical agent IDs for the philosophy module (faculty roles). */
export const PHILOSOPHY_AGENTS = ['proposer', 'skeptic', 'mediator'] as const;

/** Agent IDs of the research orchestrator (it composes other modules' roles). */
export const RESEARCH_AGENTS = PHILOSOPHY_AGENTS;

// -- Dialectic roles --

/** Agents that speak as thesis (left-wing side in the layout). */
export const THESIS_SPEAKERS = new Set(['proposer']);

/** Agents that speak as antithesis (right-wing side). */
export const ANTITHESIS_SPEAKERS = new Set(['skeptic']);

/** All dialectician speakers (thesis + antithesis, excluding mediators and moderators). */
export const DIALECTICIAN_SPEAKERS = new Set(['proposer', 'skeptic']);

/** Agents that serve as mediators. */
export const MEDIATORS = new Set(['mediator']);

// -- Node types --

/** Synthesis node type strings (used for detection in transcript and graph building). */
export const SYNTHESIS_TYPES = new Set([
  'mediator_synthesis',
  'moderator_synthesis',
  'synthesis',
]);

/** Legacy node type still found in persisted graphs for completed mediator turns. */
export const LEGACY_SYNTHESIS_TYPES = new Set(['mediator']);

/** True for canonical synthesis types and the legacy persisted mediator alias. */
export function isCompletedSynthesisNodeType(nodeType: string | undefined): boolean {
  const type = (nodeType ?? '').toLowerCase();
  return isSynthesisNodeType(type) || LEGACY_SYNTHESIS_TYPES.has(type);
}

/** Maps synthesis node type → moderator speaker name. */
export const SYNTHESIS_NODE_TYPE_TO_SPEAKER: Record<string, string> = {
  'mediator_synthesis': 'mediator',
};

/** Speaker names that produce synthesis (used for role classification). */
export const SYNTHESIS_SPEAKERS = new Set(['mediator']);

/** True when the node type names a synthesis turn (role-based, not agent-based). */
export function isSynthesisNodeType(nodeType: string | undefined): boolean {
  const type = (nodeType ?? '').toLowerCase();
  return type === 'synthesis' || type.endsWith('_synthesis');
}

/** True when the node type names a mediation/interjection turn. */
export function isInterjectionNodeType(nodeType: string | undefined): boolean {
  const type = (nodeType ?? '').toLowerCase();
  return type === 'interjection' || type.endsWith('_interjection');
}

// -- Event/node markers --

/**
 * Node type emitted when the ascent gate cannot generalize and the inquiry returns to
 * the session root. It is a question node in the layout, marked with `isEcho` for the
 * return styling; see `2026-11-01-p2-fractal-echo.md`.
 */
export const FRACTAL_ECHO_NODE_TYPE = 'fractal_echo';

/** Display label for the echo speaker/turn. */
export const ECHO_SPEAKER_LABEL = 'Echo';

// -- Agent → icon/color mapping --

/** Accent color + icon spec for a single agent, resolved by MindMap's node renderer. */
export interface AgentIconSpec {
  icon: IconComponentType;
  /** Dialectical role whose color palette supplies the node accent (resolved via roleColor). */
  accentColorRole: DialecticalRole;
}

/**
 * Single source of truth mapping every agent ID to its icon and accent color role.
 * Icon and palette are inherited from the two-dialectician debate geometry:
 * the proposing agent carries the classic gold thesis accent, the objecting agent
 * the teal antithesis accent, and the Mediator the dial-moderator accent.
 * Adding a new agent requires an entry here (plus its icon in IconSet.tsx) - MindMap
 * and any other renderer just look this map up. Must cover every entry in
 * PHILOSOPHY_AGENTS (enforced by a test).
 */
export const AGENT_ICON_MAP: Record<string, AgentIconSpec> = {
  proposer: { icon: TrendingUpIcon, accentColorRole: 'skeptic' },
  skeptic: { icon: ScaleIcon, accentColorRole: 'judge' },
  mediator: { icon: DialIcon, accentColorRole: 'proposer' },
};

// -- Layout groupings --

/** Agents on the left wing (thesis side) in helical layout. */
export const LEFT_WING_AGENTS = new Set(['proposer']);

/** Agents on the right wing (antithesis side) in helical layout. */
export const RIGHT_WING_AGENTS = new Set(['skeptic']);

/** Agents in the synthesis group in helical layout. */
export const SYNTHESIS_GROUP_AGENTS = new Set(['mediator', 'moderator']);

// -- Dialectic role classification --

/** The dialectic role assigned to an agent in the reasoning flow. */
export type DialecticRole = 'thesis' | 'antithesis' | 'synthesis' | 'mediation';

/**
 * Classifies a speaker/agent into a dialectic role.
 * Single source of truth for role assignment across all modules.
 */
export function classifyAgentRole(agentId: string): DialecticRole {
  const id = agentId.toLowerCase();
  if (THESIS_SPEAKERS.has(id)) return 'thesis';
  if (ANTITHESIS_SPEAKERS.has(id)) return 'antithesis';
  // The agent is the Mediator even when it is producing synthesis: the synthesis role
  // belongs to the node/turn, not to the agent identity. This is what lets AgentPanel
  // label the agent without conflating it with the Moderator function.
  if (MEDIATORS.has(id)) return 'mediation';
  if (SYNTHESIS_SPEAKERS.has(id)) return 'synthesis';
  return 'antithesis'; // fallback
}

// -- Speaker display labels --

/** Display-friendly names for each agent ID, used in the transcript UI. */
export const SPEAKER_LABELS: Record<string, string> = {
  proposer: 'Proposer',
  skeptic: 'Skeptic',
  mediator: 'Mediator',
  moderator: 'Moderator',
  echo: ECHO_SPEAKER_LABEL,
  system: 'System',
};

/**
 * Display label for a node or turn.
 *
 * The philosophy module has one agent, the Mediator, that serves two roles: it produces
 * synthesis and it interjects when the debate stalemates. The stored SpeakerId is the agent
 * ("mediator") for both, so the label must come from the role/node type. Without this the
 * synthesis node renders as "mediator" and the Moderator function never appears.
 */
export function speakerLabel(
  speakerId: string | undefined,
  nodeType?: string,
  dialecticRole?: string,
): string {
  const id = (speakerId ?? '').toLowerCase();
  const type = (nodeType ?? '').toLowerCase();
  const role = (dialecticRole ?? '').toLowerCase();

  // Explicit identities win over the role, because the stream sometimes tags system/gate
  // turns with the synthesis role.
  if (id === 'system' || type === 'system' || role === 'system') return 'System';
  if (id === 'gate' || type === 'gate' || type.includes('gate')) return 'Gate';
  if (id === 'echo' || type === 'echo' || type === 'fractal_echo') return ECHO_SPEAKER_LABEL;
  if (type === 'question' || role === 'question') return 'Question';

  // Node type is more specific than the collapsed dialectic role ("mediator" is used for
  // both synthesis and interjection nodes after classifyNodeType).
  if (isSynthesisNodeType(type)) return 'Moderator';
  if (isInterjectionNodeType(type)) return 'Mediator';
  if (role === 'synthesis') return 'Moderator';
  if (role === 'mediation' || role === 'interjection') return 'Mediator';

  return SPEAKER_LABELS[id]
    ?? SPEAKER_LABELS[type]
    ?? (id || type ? (id || type).charAt(0).toUpperCase() + (id || type).slice(1) : 'Agent');
}
