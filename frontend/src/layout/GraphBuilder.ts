import type { DialecticRoleType, DialecticalNodeType, LayoutGraph, LayoutNode, RawApiNode } from './types';
import {
  THESIS_SPEAKERS,
  ANTITHESIS_SPEAKERS,
  SYNTHESIS_SPEAKERS,
  PHILOSOPHY_AGENTS,
  isInterjectionNodeType,
} from '../config/agentRegistry';

/** Pure classifier, exported so callers can reuse it without re-deriving GraphBuilder's rules. */
export function classifyNodeType(rawType: string): DialecticalNodeType {
  switch (rawType) {
    case 'question':
      return 'question';
    // A fractal echo is the next level's question re-asking the session root. It stays a
    // question node in the layout so rings/subtrees work unchanged; the renderer uses the
    // separate `isEcho` flag for the return styling.
    case 'fractal_echo':
    case 'echo':
      return 'question';
    case 'proposer':
    case 'proposer_reply':
      return 'proposer';
    case 'skeptic':
    case 'skeptic_reply':
      return 'skeptic';
    case 'mediator':
    case 'mediator_synthesis':
    case 'mediator_interjection':
    case 'moderator_synthesis':
      return 'mediator';
    default: {
      const lower = rawType.toLowerCase();
      const stripped = lower.replace(/(_reply|_synthesis|_interjection|_gate|_container_analysis)$/, '');
      if ((PHILOSOPHY_AGENTS as readonly string[]).includes(stripped)) {
        return stripped as DialecticalNodeType;
      }
      // Node types before the role-keyed rename still map onto the same faculty roles.
      if (lower.includes('socratic') || lower.includes('mediator') || lower.includes('synthesis')) return 'mediator';
      if (lower.includes('proposer')) return 'proposer';
      if (lower.includes('skeptic')) return 'skeptic';
      return 'question';
    }
  }
}

/** Classifies nodes into Thesis, Antithesis, or Synthesis roles. */
export function classifyDialecticRole(item: RawApiNode): DialecticRoleType {
  if (item.dialecticRole) return item.dialecticRole;
  // Interjections are mediation, not synthesis. Without this the collapsed node type
  // ("mediator") makes them fall through to the synthesis-speaker check below.
  if (isInterjectionNodeType(item.nodeType)) return 'mediation';
  const nodeType = classifyNodeType(item.nodeType);
  if (nodeType === 'question') return 'thesis';
  if (SYNTHESIS_SPEAKERS.has(nodeType)) return 'synthesis';
  if (THESIS_SPEAKERS.has(nodeType)) return 'thesis';
  if (ANTITHESIS_SPEAKERS.has(nodeType)) return 'antithesis';
  return 'antithesis';
}

/**
 * Turns the flat array returned by the API into a LayoutGraph: a proper
 * ancestry structure built from parentHash/parentHashes, instead of the
 * "connect to the question at this level" / "connect to the previous
 * level's question" heuristics that used to live inside MindMap.tsx.
 */
export class GraphBuilder {
  build(data: RawApiNode[]): LayoutGraph {
    const graph: LayoutGraph = {
      nodes: new Map(),
      roots: [],
    };

    // First pass: create every node so parent lookups always resolve
    // regardless of array order.
    for (const item of data) {
      graph.nodes.set(item.cid, {
        id: item.cid,
        nodeType: classifyNodeType(item.nodeType),
        level: item.level,
        content: item.content,
        phase: item.cyclePhase,
        speakerId: item.speakerId ?? '',
        dialecticRole: classifyDialecticRole(item),
        parentIds: [],
        parentHashes: this.collectParentIds(item),
        chainParentIds: [],
        children: [],
        position: { x: 0, y: 0 },
        angle: 0,
        radius: 0,
        sectorStart: 0,
        sectorEnd: 0,
        turn: item.turn,
        timestamp: item.timestamp,
        isPending: item.isPending ?? false,
        isEcho: item.isEcho === true || (item.nodeType ?? '').toLowerCase() === 'fractal_echo',
        sessionId: item.sessionId,
      });
    }

    // Second pass: wire up ancestry now that every node exists.
    for (const item of data) {
      const node = graph.nodes.get(item.cid)!;
      const parentIds = this.collectParentIds(item);

      for (const parentId of parentIds) {
        node.parentIds.push(parentId);
        graph.nodes.get(parentId)?.children.push(node.id);
      }
    }

    // Third pass: infer missing links from level structure. The API used to
    // return debate nodes with no parentHash, and the streaming path omitted
    // question nodes entirely - without this, DialecticalLayout leaves every
    // orphan at (0,0) and they stack on top of each other.
    this.wireHeuristicParents(graph);

    graph.roots = [...graph.nodes.values()].filter((n) => n.parentIds.length === 0);

    return graph;
  }

  private collectParentIds(item: RawApiNode): string[] {
    const parents: string[] = [];
    if (item.parentHash) parents.push(item.parentHash);

    if (item.parentHashes) {
      for (const p of item.parentHashes) {
        if (!parents.includes(p)) parents.push(p);
      }
    }

    return parents;
  }

  /**
   * Link debate nodes to their level's question, debaters to synthesis, and
   * questions to prior synthesis. For questions with no explicit parent,
   * finds the most recent synthesis from any lower level - this connects
   * later levels' questions to earlier levels' synthesis nodes when
   * viewing all rounds together.
   */
  private wireHeuristicParents(graph: LayoutGraph): void {
    const questionByLevel = new Map<number, LayoutNode>();
    const thesisByLevel = new Map<number, LayoutNode[]>();
    const antithesisByLevel = new Map<number, LayoutNode[]>();
    const synthesisByLevel = new Map<number, LayoutNode>();

    const getTimestamp = (n: LayoutNode): number => {
      if (!n.timestamp) return 0;
      const parsed = Date.parse(n.timestamp);
      return isNaN(parsed) ? 0 : parsed;
    };

    for (const node of graph.nodes.values()) {
      if (node.nodeType === 'question') {
        const existing = questionByLevel.get(node.level);
        if (!existing || getTimestamp(node) >= getTimestamp(existing)) {
          questionByLevel.set(node.level, node);
        }
      } else if (node.dialecticRole === 'mediation') {
        // Interjection/mediation nodes are not synthesis - they don't block
        // the next level's question and don't wire to thesis/antithesis.
      } else if (SYNTHESIS_SPEAKERS.has(node.nodeType) || node.dialecticRole === 'synthesis') {
        const existing = synthesisByLevel.get(node.level);
        if (!existing || getTimestamp(node) >= getTimestamp(existing)) {
          synthesisByLevel.set(node.level, node);
        }
      } else if (node.dialecticRole === 'thesis' || THESIS_SPEAKERS.has(node.nodeType)) {
        if (!thesisByLevel.has(node.level)) thesisByLevel.set(node.level, []);
        thesisByLevel.get(node.level)!.push(node);
      } else if (node.dialecticRole === 'antithesis' || ANTITHESIS_SPEAKERS.has(node.nodeType)) {
        if (!antithesisByLevel.has(node.level)) antithesisByLevel.set(node.level, []);
        antithesisByLevel.get(node.level)!.push(node);
      }
    }

    const linkParent = (node: LayoutNode, parentId: string) => {
      if (node.parentIds.includes(parentId)) return;
      const parent = graph.nodes.get(parentId);
      if (!parent) return;
      node.parentIds.push(parentId);
      parent.children.push(node.id);
    };

    // Chain same-role nodes at the same level: each thesis chains from its
    // predecessor, each antithesis from its predecessor. These edges are
    // rendered without arrowheads to distinguish them from the dialectical
    // flow edges (question -> debate -> synthesis).
    const linkChain = (node: LayoutNode, chainParentId: string) => {
      if (node.chainParentIds.includes(chainParentId)) return;
      const parent = graph.nodes.get(chainParentId);
      if (!parent) return;
      node.chainParentIds.push(chainParentId);
      parent.children.push(node.id);
    };

    // Find the most recent synthesis at or below a given level.
    // This allows later questions to connect to earlier synthesis nodes
    // when viewing all rounds together.
    const findSynthesisAtOrBelow = (level: number): LayoutNode | null => {
      for (let l = level; l >= 0; l--) {
        const synth = synthesisByLevel.get(l);
        if (synth) return synth;
      }
      return null;
    };

    // Track the last thesis/antithesis seen at each level for chain wiring.
    const lastThesisByLevel = new Map<number, LayoutNode>();
    const lastAntithesisByLevel = new Map<number, LayoutNode>();

    for (const node of graph.nodes.values()) {
      const isMediation = node.dialecticRole === 'mediation';
      const isSynth = !isMediation && (SYNTHESIS_SPEAKERS.has(node.nodeType) || node.dialecticRole === 'synthesis');
      const isQuestion = node.nodeType === 'question';

      if (isSynth) {
        // Wire synthesis to the LAST (terminal) thesis and antithesis at its level -
        // the two nodes immediately before synthesis, not the first ones. The first
        // nodes connect to the question; the last ones connect to synthesis.
        //
        // This runs for ALL synthesis nodes, including those with explicit API
        // parentHashes (which point to the first nodes). We replace those parents
        // with the terminal nodes so the graph shows the correct dialectical flow.
        const theses = thesisByLevel.get(node.level) || [];
        const antitheses = antithesisByLevel.get(node.level) || [];
        const lastThesis = theses.length > 0 ? theses[theses.length - 1] : null;
        const lastAntithesis = antitheses.length > 0 ? antitheses[antitheses.length - 1] : null;

        // Remove existing parent links (from API parentHashes) so we can re-wire
        // to the terminal nodes instead of the first nodes.
        for (const oldParentId of [...node.parentIds]) {
          const oldParent = graph.nodes.get(oldParentId);
          if (oldParent) {
            oldParent.children = oldParent.children.filter(id => id !== node.id);
          }
        }
        node.parentIds = [];

        if (lastThesis) linkParent(node, lastThesis.id);
        if (lastAntithesis) linkParent(node, lastAntithesis.id);
        if (!lastThesis && !lastAntithesis) {
          const q = questionByLevel.get(node.level);
          if (q) linkParent(node, q.id);
        }
      } else if (!isQuestion && !isSynth && node.parentIds.length === 0) {
        const questionNode = questionByLevel.get(node.level);
        if (questionNode) linkParent(node, questionNode.id);
      }

      if (isQuestion && node.parentIds.length === 0) {
        // For questions with no explicit parent, find the most recent
        // synthesis from any lower level. This connects each round's
        // questions to the previous round's synthesis node.
        const synthesisNode = findSynthesisAtOrBelow(node.level - 1);
        if (synthesisNode) linkParent(node, synthesisNode.id);
      }

      // Chain same-role nodes: each thesis chains from its predecessor, each
      // antithesis from its predecessor. Stored in chainParentIds so the edge
      // renderer can distinguish these from dialectical-flow edges.
      if (!isQuestion && !isSynth && !isMediation) {
        if (node.dialecticRole === 'thesis') {
          const lastThesis = lastThesisByLevel.get(node.level);
          if (lastThesis) linkChain(node, lastThesis.id);
          lastThesisByLevel.set(node.level, node);
        } else if (node.dialecticRole === 'antithesis') {
          const lastAntithesis = lastAntithesisByLevel.get(node.level);
          if (lastAntithesis) linkChain(node, lastAntithesis.id);
          lastAntithesisByLevel.set(node.level, node);
        }
      }
    }
  }
}
