import { describe, it, expect } from 'vitest';
import { GraphBuilder } from '../GraphBuilder';
import { DialecticalLayout } from '../DialecticalLayout';
import type { RawApiNode } from '../types';

function q(level: number, cid: string): RawApiNode {
  return { cid, nodeType: 'question', level, content: 'question' };
}

function r(level: number, cid: string, nodeType: string, parentHash: string): RawApiNode {
  return { cid, nodeType, level, content: 'reply', parentHash };
}

function syn(level: number, cid: string, parentHashes: string[]): RawApiNode {
  return { cid, nodeType: 'mediator_synthesis', level, content: 'synthesis', parentHashes };
}

describe('DialecticalLayout', () => {
  it('two same-type siblings never overlap (fan-out by DUPLICATE_SPACING)', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      r(0, 'p0', 'proposer_reply', 'q0'),
      r(0, 'p1', 'proposer_reply', 'q0'),
      r(0, 'a0', 'skeptic_reply', 'q0'),
    ];
    const graph = new GraphBuilder().build(data);
    new DialecticalLayout().layout(graph);

    const p0 = graph.nodes.get('p0')!;
    const p1 = graph.nodes.get('p1')!;
    const dist = Math.hypot(p1.position.x - p0.position.x, p1.position.y - p0.position.y);
    expect(dist).toBeGreaterThanOrEqual(44);
  });

  it('synthesis grandchild of question (not direct child) is positioned', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      r(0, 'p0', 'proposer_reply', 'q0'),
      r(0, 'a0', 'skeptic_reply', 'q0'),
      syn(0, 'synth', ['p0', 'a0']),
    ];
    const graph = new GraphBuilder().build(data);
    new DialecticalLayout().layout(graph);

    const synth = graph.nodes.get('synth')!;
    const isAtOrigin = synth.position.x === 0 && synth.position.y === 0;
    expect(isAtOrigin).toBe(false);
  });

  it('orphan rescue moves (0,0) nodes onto a ring', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      r(0, 'p0', 'proposer_reply', 'q0'),
      r(0, 'a0', 'skeptic_reply', 'q0'),
    ];
    const graph = new GraphBuilder().build(data);
    // Remove parents so everyone is an orphan
    graph.nodes.get('p0')!.parentIds = [];
    graph.nodes.get('p0')!.position = { x: 0, y: 0 };
    graph.nodes.get('a0')!.parentIds = [];
    graph.nodes.get('a0')!.position = { x: 0, y: 0 };

    new DialecticalLayout().layout(graph);

    const moved = [...graph.nodes.values()].filter(n => n.position.x !== 0 || n.position.y !== 0);
    expect(moved.length).toBeGreaterThan(0);
  });

  it('question at level 0 root sits at origin', () => {
    const data: RawApiNode[] = [q(0, 'q0')];
    const graph = new GraphBuilder().build(data);
    new DialecticalLayout().layout(graph);
    expect(graph.nodes.get('q0')!.position).toEqual({ x: 0, y: 0 });
  });
});