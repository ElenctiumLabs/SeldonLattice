import type { XYPosition } from 'reactflow';
import { ClusterLayout } from './ClusterLayout';
import { RingAllocator } from './RingAllocator';
import type { AgentType, LayoutGraph, LayoutNode } from './types';
import { getConfig } from '../lib/config';

/** How far consecutive same-agent nodes (e.g. a second Proposer turn in
 *  the same debate) are pushed apart so they never land on top of each other. */
const DUPLICATE_SPACING = getConfig().layoutDuplicateSpacing;

/**
 * Places every "question" node on a ring per level (so rings never
 * collide regardless of how many questions share a level), then expands
 * each question into a debate cluster for its proposer/skeptic/mediator
 * replies. Only questions ever occupy a ring - debate nodes are always
 * positioned relative to their question, never independently. That's the
 * core fix over the old layout, where all four node types fought for the
 * same ring.
 */
export class DialecticalLayout {
  private readonly rings = new RingAllocator();
  private readonly clusters = new ClusterLayout();

  /** Per-level angular offset so rings don't align radially and tangle edges together. */
  private readonly levelRotation = getConfig().layoutLevelRotation;

  layout(graph: LayoutGraph): void {    const levels = this.groupQuestionsByLevel(graph);

    for (const [level, nodes] of levels) {
      this.layoutRing(level, nodes);
    }

    for (const node of graph.nodes.values()) {
      if (node.nodeType !== 'question') continue;
      this.layoutDebateSubtree(graph, node);
    }

    this.layoutOrphansByLevel(graph);
  }

  private layoutRing(level: number, nodes: LayoutNode[]): void {
    // The single root question sits dead centre rather than on a ring of one.
    if (level === 0 && nodes.length === 1) {
      const [root] = nodes;
      root.radius = 0;
      root.angle = 0;
      root.position = { x: 0, y: 0 };
      return;
    }

    const radius = this.rings.allocate(nodes.length, level);
    const step = (Math.PI * 2) / nodes.length;
    const baseAngle = level * this.levelRotation;

    nodes.forEach((node, index) => {
      node.radius = radius;
      node.angle = baseAngle + index * step;
      node.position = {
        x: Math.cos(node.angle) * radius,
        y: Math.sin(node.angle) * radius,
      };
    });
  }

  private groupQuestionsByLevel(graph: LayoutGraph): Map<number, LayoutNode[]> {
    const levels = new Map<number, LayoutNode[]>();

    for (const node of graph.nodes.values()) {
      if (node.nodeType !== 'question') continue;
      if (!levels.has(node.level)) levels.set(node.level, []);
      levels.get(node.level)!.push(node);
    }

    return levels;
  }

  /**
   * Positions every proposer/skeptic/mediator node that descends from this
   * question - however many hops away, and however many share a type -
   * so nothing is silently left at the default (0,0) and nothing lands
   * exactly on top of a sibling.
   *
   * A flat "only direct children" pass isn't enough here: a synthesis
   * node's parentHashes point at the proposer AND skeptic replies that fed
   * it, not at the question, so it's a grandchild of the question, not a
   * child. This does a breadth-first walk instead, guarded by `visited`
   * because that same two-parent shape means a synthesis node is
   * reachable via two different paths and must only be positioned once.
   */
  private layoutDebateSubtree(graph: LayoutGraph, question: LayoutNode): void {
    const cluster = this.clusters.positionQuestion(question.position.x, question.position.y);
    const counters: Record<AgentType, number> = {
      proposer: 0,
      skeptic: 0,
      mediator: 0,
    };

    const visited = new Set<string>();
    const queue: string[] = [...question.children];

    while (queue.length > 0) {
      const id = queue.shift()!;
      if (visited.has(id)) continue;
      visited.add(id);

      const node = graph.nodes.get(id);
      if (!node) continue;

      // Next-level questions are positioned by their own ring, and their
      // own reply subtree is handled by their own call to this method -
      // stop here rather than reaching into another question's cluster.
      if (node.nodeType === 'question') continue;

      const anchor = cluster[node.nodeType];
      const direction = this.normalizedDirection(question.position, anchor);
      const index = counters[node.nodeType]++;

      // The first node of a given type lands exactly on its usual spot;
      // any further same-type nodes are pushed further out along the
      // same line, so a long back-and-forth fans out instead of stacking.
      node.position = {
        x: anchor.x + direction.x * index * DUPLICATE_SPACING,
        y: anchor.y + direction.y * index * DUPLICATE_SPACING,
      };

      queue.push(...node.children);
    }
  }

  private normalizedDirection(from: XYPosition, to: XYPosition): XYPosition {
    const dx = to.x - from.x;
    const dy = to.y - from.y;
    const length = Math.hypot(dx, dy) || 1;
    return { x: dx / length, y: dy / length };
  }

  /** Spread any nodes still at the origin - covers legacy API data with no question anchors. */
  private layoutOrphansByLevel(graph: LayoutGraph): void {
    const orphans = [...graph.nodes.values()].filter(
      (node) => node.position.x === 0 && node.position.y === 0
    );
    if (orphans.length === 0) return;

    const byLevel = new Map<number, LayoutNode[]>();
    for (const node of orphans) {
      if (!byLevel.has(node.level)) byLevel.set(node.level, []);
      byLevel.get(node.level)!.push(node);
    }

    for (const [level, nodes] of byLevel) {
      if (level === 0 && nodes.length === 1) {
        nodes[0].position = { x: 0, y: 0 };
        continue;
      }

      const radius = this.rings.allocate(nodes.length, level);
      const step = (Math.PI * 2) / nodes.length;
      const baseAngle = level * this.levelRotation;

      nodes.forEach((node, index) => {
        const angle = baseAngle + index * step;
        node.position = {
          x: Math.cos(angle) * radius,
          y: Math.sin(angle) * radius,
        };
      });
    }
  }
}