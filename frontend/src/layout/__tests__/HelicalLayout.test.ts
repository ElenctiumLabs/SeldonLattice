import { describe, it, expect } from 'vitest';
import { GraphBuilder } from '../GraphBuilder';
import { HelicalLayout } from '../HelicalLayout';
import type { RawApiNode } from '../types';

function q(level: number, cid: string): RawApiNode {
  return { cid, nodeType: 'question', level, content: 'question' };
}

function r(level: number, cid: string, nodeType: string, parentHash: string): RawApiNode {
  return { cid, nodeType, level, content: 'reply', parentHash };
}

describe('HelicalLayout', () => {
  it('level 0 root question at bottom (y=baseRadius)', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
    ];
    const graph = new GraphBuilder().build(data);
    new HelicalLayout().layout(graph);

    const q0 = graph.nodes.get('q0')!;
    expect(q0.position.y).toBeGreaterThan(0);
  });

  it('ascending levels offset by 2*radius + 140', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      r(0, 'p0', 'proposer_reply', 'q0'),
      r(0, 'a0', 'skeptic_reply', 'q0'),
      q(1, 'q1'),
    ];
    const graph = new GraphBuilder().build(data);
    new HelicalLayout().layout(graph);

    const q0 = graph.nodes.get('q0')!;
    const q1 = graph.nodes.get('q1')!;
    const gap = q0.position.y - q1.position.y;
    // Each level is (2 * 120 + 150) = 390 px apart
    expect(gap).toBeCloseTo(390, -1);
  });

  it('synthesis positioned above question within same level', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      r(0, 'p0', 'proposer_reply', 'q0'),
      r(0, 'a0', 'skeptic_reply', 'q0'),
    ];
    const graph = new GraphBuilder().build(data);
    new HelicalLayout().layout(graph);

    const q0 = graph.nodes.get('q0')!;
    const p0 = graph.nodes.get('p0')!;
    const a0 = graph.nodes.get('a0')!;
    // question sits at bottom, thesis/antithesis at centerY (above question)
    expect(p0.position.y).toBeLessThan(q0.position.y);
    expect(a0.position.y).toBeLessThan(q0.position.y);
  });
});