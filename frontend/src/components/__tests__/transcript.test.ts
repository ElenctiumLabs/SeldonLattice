import { describe, it, expect } from 'vitest';
import { displayContent, liveTurnsFromApiNodes } from '../../lib/transcript';
import type { RawApiNode } from '../../layout/types';

describe('displayContent', () => {
  it('strips code fences', () => {
    expect(displayContent('```json\n{"response": "hello"}\n```')).toBe('hello');
  });

  it('extracts response field via JSON parse', () => {
    expect(displayContent('{"response": "hello world"}')).toBe('hello world');
  });

  it('suppresses reasoning-only blobs', () => {
    expect(displayContent('{"reasoning": "thinking..."}')).toBe('');
  });

  it('extracts response via regex when JSON.parse fails', () => {
    const raw = '{"response": "unescaped "quote" } {"reasoning": "thinking"}';
    const result = displayContent(raw);
    expect(result.length).toBeGreaterThan(0);
  });

  it('handles unescaped-quote JSON failure gracefully', () => {
    // JSON with unescaped quotes inside string that would cause parse failure
    const raw = '{"response": "He said "hello""}';
    const result = displayContent(raw);
    expect(typeof result).toBe('string');
  });

  it('returns empty string for null/undefined', () => {
    expect(displayContent('')).toBe('');
  });

  it('returns plain text unchanged', () => {
    expect(displayContent('Just some plain text')).toBe('Just some plain text');
  });

  it('strips leading colon from plain text', () => {
    expect(displayContent(': Courage is the mean')).toBe('Courage is the mean');
  });

  it('strips leading colon with spaces from plain text', () => {
    expect(displayContent('  :   The doctrine requires consistency.')).toBe('The doctrine requires consistency.');
  });

  it('strips leading colon from JSON response field', () => {
    expect(displayContent('{"response": ": Stare decisis binds the court."}')).toBe('Stare decisis binds the court.');
  });

  it('strips leading colon from embedded JSON response', () => {
    const raw = 'Here is my analysis:\n{"response": ": The rule of law prevails."}';
    expect(displayContent(raw)).toBe('The rule of law prevails.');
  });

  it('strips reasoning-only JSON block after plain text', () => {
    const raw = '{"reasoning": "thinking..."}This is the actual response.';
    expect(displayContent(raw)).toBe('This is the actual response.');
  });
});

describe('liveTurnsFromApiNodes', () => {
  it('maps question nodes to thesis role', () => {
    const data: RawApiNode[] = [
      { cid: 'q1', nodeType: 'question', level: 0, content: 'What is courage?' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns).toHaveLength(1);
    expect(turns[0].speaker).toBe('Question');
    expect(turns[0].dialecticRole).toBe('thesis');
  });

  it('maps synthesis nodes to synthesis role', () => {
    const data: RawApiNode[] = [
      { cid: 's1', nodeType: 'mediator_synthesis', level: 0, content: 'Synthesis text' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns[0].dialecticRole).toBe('synthesis');
  });

  it('keeps system nodes out of the synthesis role', () => {
    const data: RawApiNode[] = [
      { cid: 'sys', nodeType: 'system', level: 0, content: 'Session ended' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns[0].dialecticRole).toBe('system');
    expect(turns[0].speaker).toBe('System');
  });

  it('maps proposer replies to thesis, skeptic to antithesis, and mediator to mediation', () => {
    const data: RawApiNode[] = [
      { cid: 'p1', nodeType: 'proposer_reply', level: 0, content: 'Proposer says', speakerId: 'proposer' },
      { cid: 'p2', nodeType: 'proposer_reply', level: 0, content: 'Proposer says more', speakerId: 'proposer' },
      { cid: 'a1', nodeType: 'skeptic_reply', level: 0, content: 'Skeptic says', speakerId: 'skeptic' },
      { cid: 'm1', nodeType: 'mediator_interjection', level: 0, content: 'Mediator says', speakerId: 'mediator' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns.find(t => t.id === 'node-p1')!.dialecticRole).toBe('thesis');
    expect(turns.find(t => t.id === 'node-p2')!.dialecticRole).toBe('thesis');
    expect(turns.find(t => t.id === 'node-a1')!.dialecticRole).toBe('antithesis');
    expect(turns.find(t => t.id === 'node-m1')!.dialecticRole).toBe('mediation');
  });

  it('preserves all distinct speakers at the same level', () => {
    const data: RawApiNode[] = [
      { cid: 'q0', nodeType: 'question', level: 0, content: 'What is virtue?' },
      { cid: 'p0', nodeType: 'proposer_reply', level: 0, content: 'Proposer responds', speakerId: 'proposer' },
      { cid: 'a0', nodeType: 'skeptic_reply', level: 0, content: 'Skeptic responds', speakerId: 'skeptic' },
      { cid: 's0', nodeType: 'mediator_interjection', level: 0, content: 'Mediator interjects', speakerId: 'mediator' },
      { cid: 'syn0', nodeType: 'mediator_synthesis', level: 0, content: 'Mediator synthesizes', speakerId: 'mediator' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    // All five turns should survive - the two mediation-role turns have different cids
    expect(turns).toHaveLength(5);
    expect(turns.find(t => t.id === 'node-syn0')!.speaker).toBe('Moderator');
    expect(turns.find(t => t.id === 'node-syn0')!.dialecticRole).toBe('synthesis');
    expect(turns.find(t => t.id === 'node-s0')!.speaker).toBe('Mediator');
    expect(turns.find(t => t.id === 'node-s0')!.dialecticRole).toBe('mediation');
  });

  it('deduplicates only when the same node ID (cid) appears twice', () => {
    const data: RawApiNode[] = [
      { cid: 'p1', nodeType: 'proposer_reply', level: 0, content: 'First thesis', speakerId: 'proposer' },
      { cid: 'p1', nodeType: 'proposer_reply', level: 0, content: 'Second thesis', speakerId: 'proposer' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns).toHaveLength(1);
    expect(turns[0].content).toBe('Second thesis');
  });

  it('filters session_metadata and empty content', () => {
    const data: RawApiNode[] = [
      { cid: 'm1', nodeType: 'session_metadata', level: 0, content: 'meta' },
      { cid: 'e1', nodeType: 'proposer_reply', level: 0, content: '', speakerId: 'proposer' },
      { cid: 'q1', nodeType: 'question', level: 0, content: 'Real question' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns).toHaveLength(1);
    expect(turns[0].id).toBe('node-q1');
  });

  it('preserves quality metrics from SSE-style prior turns across reconstruction', () => {
    const data: RawApiNode[] = [
      { cid: 'q0', nodeType: 'question', level: 0, content: 'What is courage?' },
      { cid: 'p0', nodeType: 'proposer_reply', level: 0, content: 'Proposer responds', speakerId: 'proposer' },
      { cid: 'a0', nodeType: 'skeptic_reply', level: 0, content: 'Skeptic responds', speakerId: 'skeptic' },
    ];
    const previousTurns = [
      {
        id: 'turn-proposer-123',
        speaker: 'Proposer',
        content: 'Proposer responds',
        dialecticRole: 'thesis',
        level: 0,
        timestamp: 1,
        metrics: { qualityScore: 88.4, ceremonyScore: 1.2, similarityScore: 0.3 },
        phase: 'growth',
      },
      {
        id: 'turn-skeptic-456',
        speaker: 'Skeptic',
        content: 'Skeptic responds',
        dialecticRole: 'antithesis',
        level: 0,
        timestamp: 2,
        metrics: { qualityScore: 74.9, ceremonyScore: 0.7 },
      },
    ];
    const turns = liveTurnsFromApiNodes(data, previousTurns);
    const proposerTurn = turns.find((t) => t.speaker === 'Proposer');
    const skepticTurn = turns.find((t) => t.speaker === 'Skeptic');
    expect(proposerTurn?.metrics).toEqual({ qualityScore: 88.4, ceremonyScore: 1.2, similarityScore: 0.3 });
    expect(proposerTurn?.phase).toBe('growth');
    expect(skepticTurn?.metrics).toEqual({ qualityScore: 74.9, ceremonyScore: 0.7 });
  });

  it('preserves metrics from node-id prior turns on refresh', () => {
    const data: RawApiNode[] = [
      { cid: 'p0', nodeType: 'proposer_reply', level: 0, content: 'Proposer responds', speakerId: 'proposer' },
    ];
    const previousTurns = [
      {
        id: 'node-p0',
        speaker: 'Proposer',
        content: 'Proposer responds',
        dialecticRole: 'thesis',
        level: 0,
        timestamp: 0,
        metrics: { qualityScore: 91.0, similarityScore: 0.4 },
      },
    ];
    const turns = liveTurnsFromApiNodes(data, previousTurns);
    expect(turns[0].metrics).toEqual({ qualityScore: 91.0, similarityScore: 0.4 });
  });

  it('does not cross-wire metrics between distinct speakers at the same level', () => {
    const data: RawApiNode[] = [
      { cid: 'p0', nodeType: 'proposer_reply', level: 0, content: 'Proposer responds', speakerId: 'proposer' },
      { cid: 'a0', nodeType: 'skeptic_reply', level: 0, content: 'Skeptic responds', speakerId: 'skeptic' },
    ];
    const previousTurns = [
      {
        id: 'turn-skeptic-1',
        speaker: 'Skeptic',
        content: 'Skeptic responds',
        dialecticRole: 'antithesis',
        level: 0,
        timestamp: 1,
        metrics: { qualityScore: 60.0 },
      },
    ];
    const turns = liveTurnsFromApiNodes(data, previousTurns);
    expect(turns.find((t) => t.speaker === 'Proposer')?.metrics).toBeUndefined();
    expect(turns.find((t) => t.speaker === 'Skeptic')?.metrics).toEqual({ qualityScore: 60.0 });
  });

  it('sorts results by level ascending', () => {
    const data: RawApiNode[] = [
      { cid: 'q1', nodeType: 'question', level: 1, content: 'Level 1 question' },
      { cid: 'q0', nodeType: 'question', level: 0, content: 'Level 0 question' },
    ];
    const turns = liveTurnsFromApiNodes(data);
    expect(turns[0].level).toBe(0);
    expect(turns[1].level).toBe(1);
  });
});