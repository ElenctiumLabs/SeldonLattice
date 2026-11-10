import type { LayoutGraph, LayoutNode } from './types';
import { SYNTHESIS_GROUP_AGENTS, LEFT_WING_AGENTS } from '../config/agentRegistry';
import { getConfig } from '../lib/config';

/**
 * HelicalLayout - rhombus chain stacking vertically, flipped 180° so that
 * level 0 (root) sits at the bottom and higher levels ascend upward.
 *
 * Each level forms a diamond/rhombus with:
 *   - question at the bottom vertex
 *   - synthesis at the top vertex
 *   - thesis (left wing) at the left vertex
 *   - antithesis (right wing) at the right vertex
 *
 * Consecutive levels touch tip-to-tip: the synthesis of level N shares its
 * vertical position with the question of level N+1, producing a continuous
 * chain of diamonds flowing upward - the visual metaphor of dialectical ascent.
 *
 * Mediator interventions are placed horizontally outward from the turn where
 * they occurred, fanning out if multiple interventions happen at the same turn.
 */
export class HelicalLayout {
  private get baseRadius() { return getConfig().layoutHelicalBaseRadius; }
  private get interjectionSpacing() { return getConfig().layoutHelicalInterjectionSpacing; }

  layout(graph: LayoutGraph): void {
    // Group nodes by level
    const nodesByLevel = new Map<number, LayoutNode[]>();
    for (const node of graph.nodes.values()) {
      const lvl = node.level ?? 0;
      if (!nodesByLevel.has(lvl)) {
        nodesByLevel.set(lvl, []);
      }
      nodesByLevel.get(lvl)!.push(node);
    }
    const sortedLevels = Array.from(nodesByLevel.keys()).sort((a, b) => a - b);

    for (const level of sortedLevels) {
      const nodes = nodesByLevel.get(level)!;

      // Each level's center sits above the previous one with enough gap for visible connecting edges.
      // Level 0 center at y=0 (bottom); higher levels above (negative y).
      const centerY = -level * (2 * this.baseRadius + getConfig().layoutHelicalLevelGap);

      // Question at bottom vertex of the diamond, synthesis at top.
      // Gap between levels: synthesis of N connects to question of N+1 with visible edge.
      const questionY = centerY + this.baseRadius;
      const synthesisY = centerY - this.baseRadius;

      let questionCount = 0;
      let leftCount = 0;
      let rightCount = 0;
      let synthesisCount = 0;
      let mediationCount = 0;

      // Collect interjections and group by turn for fan-out placement
      const interjectionsByTurn = new Map<number, LayoutNode[]>();
      const otherNodes: LayoutNode[] = [];

      for (const node of nodes) {
        if (node.dialecticRole === 'interjection') {
          const turn = node.turn ?? 0;
          if (!interjectionsByTurn.has(turn)) {
            interjectionsByTurn.set(turn, []);
          }
          interjectionsByTurn.get(turn)!.push(node);
        } else {
          otherNodes.push(node);
        }
      }

      // First pass: position non-interjection nodes
      for (const node of otherNodes) {
        const type = node.nodeType.toLowerCase();
        const stackOffset = getConfig().layoutHelicalStackOffset;

        if (type === 'question') {
          node.position = {
            x: (questionCount % 2 === 0 ? 1 : -1) * Math.floor(questionCount / 2) * stackOffset,
            y: questionY,
          };
          questionCount++;
        } else if (
          node.dialecticRole === 'synthesis' ||
          (node.dialecticRole !== 'thesis' && node.dialecticRole !== 'antithesis' && node.dialecticRole !== 'mediation' && SYNTHESIS_GROUP_AGENTS.has(type))
        ) {
          node.position = {
            x: (synthesisCount % 2 === 0 ? 1 : -1) * Math.floor(synthesisCount / 2) * getConfig().layoutHelicalStackOffset,
            y: synthesisY,
          };
          synthesisCount++;
        } else if (node.dialecticRole === 'mediation') {
          // Mediators sit near the centre of the diamond, between thesis and antithesis
          node.position = {
            x: (mediationCount % 2 === 0 ? 1 : -1) * Math.floor(mediationCount / 2) * getConfig().layoutHelicalStackOffset,
            y: centerY,
          };
          mediationCount++;
        } else if (node.dialecticRole === 'thesis' || LEFT_WING_AGENTS.has(type)) {
          // Left wing (thesis side) at the left vertex of the diamond
          node.position = {
            x: -this.baseRadius - leftCount * getConfig().layoutHelicalStackOffset,
            y: centerY,
          };
          leftCount++;
        } else {
          // Right wing (antithesis side) at the right vertex of the diamond
          node.position = {
            x: this.baseRadius + rightCount * getConfig().layoutHelicalStackOffset,
            y: centerY,
          };
          rightCount++;
        }
      }

      // Second pass: position interjections horizontally outward from their turn
      // Each turn's interjections fan out horizontally, with the turn's Y position
      // calculated based on the turn number within the level
      for (const [turn, interjections] of interjectionsByTurn) {
        // Calculate Y position based on turn number
        // Turn 0 is near the question (bottom), turn N is near synthesis (top)
        // The Y position interpolates between questionY and synthesisY based on turn
        const maxTurn = Math.max(...interjectionsByTurn.keys());
        const turnY = maxTurn > 0
          ? questionY - (turn / maxTurn) * (questionY - synthesisY)
          : centerY;

        // Fan out horizontally from the right side of the diamond
        const startX = this.baseRadius + 30;
        const fanDirection = 1; // Fan to the right

        interjections.forEach((node, index) => {
          node.position = {
            x: startX + index * this.interjectionSpacing * fanDirection,
            y: turnY,
          };
        });
      }
    }
  }
}
