import { describe, it, expect } from 'vitest';
import { GraphBuilder, classifyDialecticRole, classifyNodeType } from '../GraphBuilder';
import type { RawApiNode } from '../types';

function q(level: number, cid: string, content = 'question'): RawApiNode {
  return { cid, nodeType: 'question', level, content };
}

function reply(level: number, cid: string, nodeType: string, parentHash?: string, parentHashes?: string[]): RawApiNode {
  return { cid, nodeType, level, content: 'reply', parentHash, parentHashes };
}

describe('GraphBuilder', () => {
  it('wires parentHash to establish ancestry', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      reply(0, 'p0', 'proposer_reply', 'q0'),
      reply(0, 'a0', 'skeptic_reply', 'q0'),
    ];
    const graph = new GraphBuilder().build(data);
    expect(graph.nodes.get('p0')!.parentIds).toEqual(['q0']);
    expect(graph.nodes.get('a0')!.parentIds).toEqual(['q0']);
    expect(graph.nodes.get('q0')!.children).toContain('p0');
    expect(graph.nodes.get('q0')!.children).toContain('a0');
  });

  it('wires multi-parent via parentHashes', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      reply(0, 'p0', 'proposer_reply', 'q0'),
      reply(0, 'a0', 'skeptic_reply', 'q0'),
      reply(0, 'synth', 'mediator_synthesis', undefined, ['p0', 'a0']),
    ];
    const graph = new GraphBuilder().build(data);
    const synth = graph.nodes.get('synth')!;
    expect(synth.parentIds).toEqual(['p0', 'a0']);
    expect(graph.nodes.get('p0')!.children).toContain('synth');
    expect(graph.nodes.get('a0')!.children).toContain('synth');
  });

  it('wires heuristic parent for debate node with no parentHash', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      reply(0, 'p0', 'proposer_reply'),
    ];
    const graph = new GraphBuilder().build(data);
    expect(graph.nodes.get('p0')!.parentIds).toContain('q0');
  });

  it('wires heuristic parent for level>0 question to prior synthesis', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      reply(0, 'p0', 'proposer_reply', 'q0'),
      reply(0, 'a0', 'skeptic_reply', 'q0'),
      reply(0, 'synth', 'mediator_synthesis', undefined, ['p0', 'a0']),
      q(1, 'q1'),
    ];
    const graph = new GraphBuilder().build(data);
    expect(graph.nodes.get('q1')!.parentIds).toContain('synth');
  });

  it('synthesis node reachable via two paths is visited once', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      reply(0, 'p0', 'proposer_reply', 'q0'),
      reply(0, 'a0', 'skeptic_reply', 'q0'),
      reply(0, 'synth', 'mediator_synthesis', undefined, ['p0', 'a0']),
    ];
    const graph = new GraphBuilder().build(data);
    const synth = graph.nodes.get('synth')!;
    expect(synth.children).toHaveLength(0);
    expect(graph.nodes.get('p0')!.children).toContain('synth');
    expect(graph.nodes.get('a0')!.children).toContain('synth');
  });

  it('classifies node types correctly', () => {
    expect(classifyNodeType('proposer_reply')).toBe('proposer');
    expect(classifyNodeType('skeptic_reply')).toBe('skeptic');
    expect(classifyNodeType('mediator_synthesis')).toBe('mediator');
    expect(classifyNodeType('mediator_interjection')).toBe('mediator');
    expect(classifyNodeType('moderator_synthesis')).toBe('mediator');
    expect(classifyNodeType('fractal_echo')).toBe('question');
    expect(classifyNodeType('unknown')).toBe('question');
  });

  it('classifies interjections as mediation, not synthesis', () => {
    expect(classifyDialecticRole({ cid: 'm', nodeType: 'mediator_interjection', level: 0, content: 'x' })).toBe('mediation');
    expect(classifyDialecticRole({ cid: 's', nodeType: 'mediator_synthesis', level: 0, content: 'x' })).toBe('synthesis');
  });

  it('marks a fractal_echo node as an echo question', () => {
    const data: RawApiNode[] = [
      q(0, 'q0'),
      reply(0, 'p0', 'proposer_reply', 'q0'),
      reply(0, 'a0', 'skeptic_reply', 'q0'),
      reply(0, 'synth', 'mediator_synthesis', undefined, ['p0', 'a0']),
      { ...reply(1, 'echo1', 'fractal_echo', 'synth'), content: 'Root question?' },
    ];

    const graph = new GraphBuilder().build(data);
    const echo = graph.nodes.get('echo1')!;

    expect(echo.nodeType).toBe('question');
    expect(echo.isEcho).toBe(true);
    expect(echo.parentIds).toContain('synth');
  });
});