import type { Edge, Node } from 'reactflow';
import type { LayoutGraph, LayoutNode, LayoutResult } from './types';
import { roleColor, roleOf } from '../theme/prime-radiant-tokens';
import { AGENT_ICON_MAP, speakerLabel } from '../config/agentRegistry';

function toFlowNode(node: LayoutNode): Node {
  const rawLabel = speakerLabel(node.speakerId, node.nodeType, node.dialecticRole);
  return {
    id: node.id,
    type: 'dialectical',
    position: node.position,
    data: {
      // Use the "node-{cid}" prefix so data.id matches the LiveTurn IDs produced
      // by liveTurnsFromApiNodes(). Graph nodes emitted during live SSE use
      // synthetic CIDs, so their data.id becomes "node-pending-question-…" -
      // which will never match a real LiveTurn entry, correctly falling through
      // to the transient-item path in GraphViewSwitcher.
      id: `node-${node.id}`,
      cid: node.id,
      parentHashes: node.parentHashes,
      isEcho: node.isEcho === true,
      label: node.isEcho ? 'Return to Root' : rawLabel,
      agent: node.nodeType,
      role: roleOf(node.nodeType),
      dialecticRole: node.dialecticRole,
      content: node.content,
      phase: node.phase,
      level: node.level,
      isPending: node.isPending ?? false,
    },
  };
}

function toFlowEdges(graph: LayoutGraph): Edge[] {
  const edges: Edge[] = [];

  for (const node of graph.nodes.values()) {
    // Chain edges (same-role predecessor) - no arrowhead, thinner, dashed.
    for (const chainParentId of node.chainParentIds) {
      if (!graph.nodes.has(chainParentId)) continue;
      const chainParent = graph.nodes.get(chainParentId)!;
      edges.push({
        id: `e-chain-${chainParentId}-${node.id}`,
        source: chainParentId,
        target: node.id,
        animated: false,
        type: 'floating',
        style: {
          stroke: roleColor(roleOf(chainParent.nodeType)),
          strokeWidth: 1,
          opacity: 0.4,
          strokeDasharray: '4 2',
        },
      });
    }

    for (const parentId of node.parentIds) {
      if (!graph.nodes.has(parentId)) continue;

      const parent = graph.nodes.get(parentId)!;

      // Skip duplicate edges from the same source to the same role at the same level.
      // If a question has multiple proposer replies, only draw one edge to the first
      // proposer - otherwise each extra reply gets a stray line going up to it.
      // Generalised from synthesis-only to all nodes: the same clutter occurs when
      // a debate node has multiple replies from the same speaker at the same level.
      {
        const roleKey = node.dialecticRole;
        if (roleKey === 'thesis' || roleKey === 'antithesis') {
          const alreadyConnected = edges.some(e =>
            e.source === parent.id &&
            e.target !== node.id &&
            graph.nodes.get(e.target)?.dialecticRole === roleKey &&
            graph.nodes.get(e.target)?.level === node.level
          );
          if (alreadyConnected) continue;
        }
      }

      // Skip direct thesis↔antithesis edges at the same level.
      // The proper dialectical flow is:
      //   question → thesis / antithesis → synthesis
      // Direct left-to-right connections (thesis→antithesis or vice versa)
      // are wiring artifacts from the API's parentHash and should not be drawn.
      // Use nodeType (not dialecticRole) because question nodes have
      // dialecticRole === 'thesis' but should not be filtered.
      const isCrossDialectic =
        (parent.nodeType !== 'question' && node.nodeType !== 'question') &&
        ((parent.dialecticRole === 'thesis' && node.dialecticRole === 'antithesis') ||
         (parent.dialecticRole === 'antithesis' && node.dialecticRole === 'thesis'));
      if (isCrossDialectic && parent.level === node.level) continue;

      // Edges are coloured by the SOURCE node, not the target. A red question's
      // outgoing edges are red; the proposer's outgoing edges are the proposer's colour.
      // Uses the same AGENT_ICON_MAP as the node renderer so edges match borders.
      const sourceSpec = AGENT_ICON_MAP[parent.nodeType];
      const effectiveRole = sourceSpec?.accentColorRole ?? roleOf(parent.nodeType);

      edges.push({
        id: `e-${parentId}-${node.id}`,
        source: parentId,
        target: node.id,
        animated: true,
        type: 'floating',
        style: {
          stroke: roleColor(effectiveRole),
          strokeWidth: 1.5,
          opacity: 0.7,
        },
      });
    }
  }

  return edges;
}

/** Final step of the pipeline: LayoutGraph -> the shape React Flow needs. */
export function toReactFlow(graph: LayoutGraph): LayoutResult {
  return {
    nodes: [...graph.nodes.values()].map(toFlowNode),
    edges: toFlowEdges(graph),
  };
}
