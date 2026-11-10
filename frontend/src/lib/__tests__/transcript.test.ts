import { describe, expect, it } from 'vitest';
import type { RawApiNode } from '../../layout';
import {
  collectParentHashes,
  findRootQuestion,
  findQuestionForLevel,
  isMerkleCid,
  liveTurnsFromApiNodes,
  merkleCidFromTurnId,
  reachedSustainedPause,
} from '../transcript';

function question(cid: string, level: number, content: string, timestamp: string): RawApiNode {
  return { cid, nodeType: 'question', level, content, timestamp };
}

function thesis(cid: string, nodeType: string, level: number, timestamp: string): RawApiNode {
  return { cid, nodeType, level, content: `${cid} content`, speakerId: nodeType, timestamp };
}

function synthesis(cid: string, nodeType: string, level: number, timestamp: string): RawApiNode {
  return { cid, nodeType, level, content: `${cid} content`, speakerId: nodeType, timestamp };
}

describe('findRootQuestion', () => {
  it('returns the level-0 question (the ROOT thesis), not the latest level thesis', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is the nature of justice?', '2026-01-01T00:00:00Z'),
      thesis('t0', 'proposer_reply', 0, '2026-01-01T00:01:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('q1', 1, 'Is justice a matter of law or of right reason?', '2026-01-01T00:03:00Z'),
      thesis('t1', 'proposer_reply', 1, '2026-01-01T00:04:00Z'),
      synthesis('s1', 'mediator_synthesis', 1, '2026-01-01T00:05:00Z'),
      question('q2', 2, 'Can institutions embody justice without individuals being just?', '2026-01-01T00:06:00Z'),
    ];

    expect(findRootQuestion(apiData)).toBe('What is the nature of justice?');
  });

  it('returns null when no level-0 question exists', () => {
    const apiData: RawApiNode[] = [question('q3', 3, 'Deep thesis', '2026-01-01T00:00:00Z')];
    expect(findRootQuestion(apiData)).toBeNull();
  });

  it('ignores empty content question nodes', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, '   ', '2026-01-01T00:00:00Z'),
      question('q0b', 0, 'Real root question', '2026-01-01T00:01:00Z'),
    ];
    expect(findRootQuestion(apiData)).toBe('Real root question');
  });
});

describe('findQuestionForLevel', () => {
  it('returns the latest active question for the given level', () => {
    const apiData: RawApiNode[] = [
      question('q1a', 1, 'First run question', '2026-01-01T00:03:00Z'),
      question('q1b', 1, 'Active question', '2026-01-01T00:06:00Z'),
      question('q2', 2, 'Next level question', '2026-01-01T00:07:00Z'),
    ];

    expect(findQuestionForLevel(apiData, 1)).toBe('Active question');
    expect(findQuestionForLevel(apiData, 2)).toBe('Next level question');
  });

  it('returns null for a level without a question node', () => {
    const apiData: RawApiNode[] = [synthesis('s1', 'mediator_synthesis', 1, '2026-01-01T00:01:00Z')];
    expect(findQuestionForLevel(apiData, 2)).toBeNull();
  });

  it('deep-session scenario: root stays the level-0 question while each level keeps its own seed', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, 'ROOT', '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('q1', 1, 'LATEST-L1', '2026-01-01T00:03:00Z'),
      synthesis('s1', 'mediator_synthesis', 1, '2026-01-01T00:05:00Z'),
      question('q2', 2, 'LATEST-L2', '2026-01-01T00:06:00Z'),
    ];

    expect(findRootQuestion(apiData)).toBe('ROOT');
    expect(findQuestionForLevel(apiData, 0)).toBe('ROOT');
    expect(findQuestionForLevel(apiData, 1)).toBe('LATEST-L1');
    expect(findQuestionForLevel(apiData, 2)).toBe('LATEST-L2');
  });
});
describe('liveTurnsFromApiNodes - negative-dialectic markers', () => {
  // The Inspector renders these markers, but they were dropped when turns were
  // reconstructed from stored nodes: the fields were read off the API node and simply
  // not copied onto the turn. A page reload therefore lost markers the store had.
  it('carries remainingDissensus and repeatsPremise from the synthesis node', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is the nature of justice?', '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
    ];
    // Markers live on the synthesis node, exactly as NodeEndpoints returns them.
    (apiData[1] as RawApiNode).remainingDissensus = 'Whether law can be just without right reason.';
    (apiData[1] as RawApiNode).repeatsPremise = 0.42;

    const turns = liveTurnsFromApiNodes(apiData);
    const synth = turns.find((t) => t.dialecticRole === 'synthesis');

    expect(synth).toBeDefined();
    expect(synth!.remainingDissensus).toBe('Whether law can be just without right reason.');
    expect(synth!.repeatsPremise).toBe(0.42);
  });

  it('leaves the markers null when the node has none', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is the nature of justice?', '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
    ];

    const turns = liveTurnsFromApiNodes(apiData);
    const synth = turns.find((t) => t.dialecticRole === 'synthesis');

    expect(synth!.remainingDissensus ?? null).toBeNull();
    expect(synth!.repeatsPremise ?? null).toBeNull();
  });
});

describe('liveTurnsFromApiNodes - persisted ascent scores survive a reload', () => {
  // The ascent scorer runs only for a synthesis, and its scores are persisted on
  // the node (DialecticalNode keys 25/28). Nothing copied them onto the turn, so a
  // cold load - where no stream-only metrics exist - showed "Metrics unavailable"
  // for every turn even though the store held the values.
  it('carries ascentScore and irreducibleTension off the synthesis node', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
    ];
    (apiData[1] as RawApiNode).ascentScore = 0.374;
    (apiData[1] as RawApiNode).irreducibleTension = 0.198;

    const synth = liveTurnsFromApiNodes(apiData).find((t) => t.dialecticRole === 'synthesis');

    expect(synth!.metrics?.ascentScore).toBeCloseTo(0.374, 5);
    expect(synth!.metrics?.irreducibleTension).toBeCloseTo(0.198, 5);
  });

  it('does not attribute a zero-initialised ascentScore to a debate turn', () => {
    // Non-synthesis nodes carry 0f in that slot because the scorer never ran. It
    // must not be surfaced as a genuine score of 0 - that presents unmeasured as
    // measured, and the Inspector would draw a 0.0% bar for it.
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
      { cid: 'w0', nodeType: 'skeptic_reply', level: 0, content: 'Skeptic responds', speakerId: 'skeptic' } as RawApiNode,
    ];
    (apiData[1] as RawApiNode).ascentScore = 0;

    const debate = liveTurnsFromApiNodes(apiData).find((t) => t.dialecticRole === 'antithesis');

    expect(debate!.metrics?.ascentScore).toBeUndefined();
  });

  it('reads per-turn quality metrics off the node with no prior turns at all', () => {
    // The point of persisting these: a cold load must be able to show them without any
    // streamed copy surviving in localStorage. Passing no previousTurns is exactly the
    // session-manager path that used to come back metric-less.
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
      {
        cid: 'w0',
        nodeType: 'skeptic_reply',
        level: 0,
        content: 'Skeptic responds',
        speakerId: 'skeptic',
        qualityScore: 78.5,
        ceremonyScore: 1.25,
        similarityToPrevious: 0.31,
        newArgumentCount: 4,
        wordCount: 210,
        hasFarewell: false,
        hasAgreement: true,
        hasDisagreement: true,
        selfSimilarity: 0.12,
      } as RawApiNode,
    ];

    const debate = liveTurnsFromApiNodes(apiData, []).find((t) => t.dialecticRole === 'antithesis');

    expect(debate!.metrics?.qualityScore).toBe(78.5);
    expect(debate!.metrics?.ceremonyScore).toBe(1.25);
    expect(debate!.metrics?.similarityScore).toBe(0.31);
    expect(debate!.metrics?.newArgumentCount).toBe(4);
    expect(debate!.metrics?.wordCount).toBe(210);
    expect(debate!.metrics?.hasAgreement).toBe(true);
    expect(debate!.metrics?.hasDisagreement).toBe(true);
    expect(debate!.metrics?.selfSimilarity).toBe(0.12);
  });

  it('preserves a measured zero quality score instead of treating it as absent', () => {
    // A reply can legitimately score 0 (maximally repetitive). Null means "not scored"
    // and 0 means "scored at zero"; conflating them would erase a real observation and
    // make downstream analytics unable to distinguish the two.
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
      {
        cid: 'w0',
        nodeType: 'skeptic_reply',
        level: 0,
        content: 'Skeptic restates the question verbatim',
        speakerId: 'skeptic',
        qualityScore: 0,
        newArgumentCount: 0,
      } as RawApiNode,
    ];

    const debate = liveTurnsFromApiNodes(apiData, []).find((t) => t.dialecticRole === 'antithesis');

    expect(debate!.metrics?.qualityScore).toBe(0);
    expect(debate!.metrics?.newArgumentCount).toBe(0);
  });

  it('leaves metrics undefined when the node was never scored', () => {
    // Question nodes are not run through the quality gates, so their metric slots are
    // null. Nothing should be invented for them.
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
    ];

    const q = liveTurnsFromApiNodes(apiData, []).find((t) => t.speaker === 'Question');

    expect(q!.metrics).toBeUndefined();
  });

  it('does not duplicate a completed round when reloading over persisted stream turns', () => {
    // SSE turns carry `turn-*` ids and reconstructed ones `node-<cid>`, so the two
    // never collide by id. Merging them naively duplicates the whole round on page
    // load - the store's copy plus the persisted stream copy. The completed round
    // must be defined by the store's nodes alone.
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
      { cid: 'p0', nodeType: 'proposer_reply', level: 0, content: 'Proposer responds', speakerId: 'proposer' } as RawApiNode,
      { cid: 'a0', nodeType: 'skeptic_reply', level: 0, content: 'Skeptic responds', speakerId: 'skeptic' } as RawApiNode,
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:03:00Z'),
    ];
    const previousTurns = [
      { id: 'turn-Question-0-0', speaker: 'Question', content: 'What is courage?', dialecticRole: 'thesis' as const, level: 0, timestamp: 1 },
      { id: 'turn-proposer-0-0', speaker: 'Proposer', content: 'Proposer responds', dialecticRole: 'thesis' as const, level: 0, timestamp: 2, metrics: { qualityScore: 88.4 } },
      { id: 'turn-skeptic-0-1', speaker: 'Skeptic', content: 'Skeptic responds', dialecticRole: 'antithesis' as const, level: 0, timestamp: 3, metrics: { qualityScore: 74.9 } },
      { id: 'turn-mediator-0-2', speaker: 'Mediator', content: 'Synthesis metaphor', dialecticRole: 'synthesis' as const, level: 0, timestamp: 4, metrics: { qualityScore: 91.0 } },
    ];

    const turns = liveTurnsFromApiNodes(apiData, previousTurns);

    // Exactly the store's nodes - no streamed copies added alongside them.
    expect(turns.map((t) => t.id).sort()).toEqual(['node-a0', 'node-p0', 'node-q0', 'node-s0']);
    expect(turns.filter((t) => t.speaker === 'Proposer')).toHaveLength(1);
    // The store's copy wins, and it still inherited the stream-only quality score.
    expect(turns.find((t) => t.speaker === 'Proposer')!.metrics?.qualityScore).toBe(88.4);
  });

  it('preserves streamed turns for a round that has not completed yet', () => {
    // The in-progress path is what keeps a live round from collapsing. For a round
    // with no synthesis yet, the streamed turns are the record: the branch keeps
    // them and takes only the level's question from the store (adding the store's
    // debate replies too would double every turn the stream already delivered).
    const apiData: RawApiNode[] = [
      question('q0', 0, 'What is courage?', '2026-01-01T00:00:00Z'),
      { cid: 'p0', nodeType: 'proposer_reply', level: 0, content: 'Proposer responds', speakerId: 'proposer' } as RawApiNode,
    ];
    const previousTurns = [
      { id: 'turn-skeptic-0-1', speaker: 'Skeptic', content: 'Skeptic responds', dialecticRole: 'antithesis' as const, level: 0, timestamp: 3 },
    ];

    const turns = liveTurnsFromApiNodes(apiData, previousTurns);
    const ids = turns.map((t) => t.id);

    // The streamed turn survives, and the level's question is carried from the store.
    expect(ids).toContain('turn-skeptic-0-1');
    expect(ids).toContain('node-q0');
    expect(turns.find((t) => t.id === 'node-q0')!.speaker).toBe('Question');
  });
});

describe('liveTurnsFromApiNodes - fractal echo', () => {
  // The echo event is not persisted. The boundary is derived from a sustained synthesis
  // followed by a question that asks the session root again.
  it('marks the next-level root question as an echo', () => {
    const apiData: RawApiNode[] = [
      question('sha3-256-root', 0, 'What is justice?', '2026-01-01T00:00:00Z'),
      synthesis('sha3-256-s1', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('sha3-256-root-again', 1, 'What is justice?', '2026-01-01T00:03:00Z'),
    ];
    (apiData[1] as RawApiNode).isSustained = true;

    const turns = liveTurnsFromApiNodes(apiData);

    expect(turns.find((t) => (t.level ?? 0) === 1)!.isEcho).toBe(true);
    // The root question itself is not a return; it is the origin.
    expect(turns.find((t) => (t.level ?? 0) === 0)!.isEcho).toBeUndefined();
  });

  it('does not mark a normal ascending question', () => {
    const apiData: RawApiNode[] = [
      question('sha3-256-root', 0, 'What is justice?', '2026-01-01T00:00:00Z'),
      synthesis('sha3-256-s1', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('sha3-256-next', 1, 'Is justice a matter of law?', '2026-01-01T00:03:00Z'),
    ];
    (apiData[1] as RawApiNode).isSustained = false;

    const turns = liveTurnsFromApiNodes(apiData);

    expect(turns.every((t) => t.isEcho !== true)).toBe(true);
  });
});

describe('liveTurnsFromApiNodes - Merkle provenance', () => {
  // The Inspector's provenance strip reads the node's content address and its
  // parent chain. Reconstructing turns dropped both, so a persisted node could
  // only ever be identified by the generated `node-<cid>` id.
  it('carries the CID and flattens parentHash/parentHashes into one lineage', () => {
    const root = question('sha3-256-root', 0, 'What is justice?', '2026-01-01T00:00:00Z');
    const synth = synthesis('sha3-256-synth', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z');
    synth.parentHash = 'sha3-256-root';
    synth.parentHashes = ['sha3-256-thesis', 'sha3-256-root', ''];

    const turn = liveTurnsFromApiNodes([root, synth]).find((t) => t.dialecticRole === 'synthesis');

    expect(turn!.cid).toBe('sha3-256-synth');
    // Primary parent first, additional ancestors after it, duplicates and blanks removed.
    expect(turn!.parentHashes).toEqual(['sha3-256-root', 'sha3-256-thesis']);
  });

  it('does not present a synthetic turn id as a Merkle CID', () => {
    expect(isMerkleCid('pending-question-0')).toBe(false);
    expect(isMerkleCid('turn-proposer-0-1')).toBe(false);
    expect(isMerkleCid('sha3-256-abc_123')).toBe(true);
    expect(merkleCidFromTurnId('node-sha3-256-abc')).toBe('sha3-256-abc');
    expect(merkleCidFromTurnId('node-turn-proposer-0-1')).toBeUndefined();
  });

  it('collectParentHashes tolerates absent and duplicate references', () => {
    expect(collectParentHashes({})).toEqual([]);
    expect(collectParentHashes({ parentHash: null, parentHashes: null })).toEqual([]);
    expect(
      collectParentHashes({ parentHash: 'sha3-256-a', parentHashes: ['sha3-256-b', 'sha3-256-a'] }),
    ).toEqual(['sha3-256-a', 'sha3-256-b']);
  });
});

describe('reachedSustainedPause', () => {
  const root = 'What is the nature of justice?';

  function sustained(level: number) {
    return { level, isSustained: true as const };
  }

  it('is false when the sustained rung returned to the root (the ladder continued)', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, root, '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('q1', 1, root, '2026-01-01T00:03:00Z'),
    ];
    expect(reachedSustainedPause([sustained(0)], apiData, root)).toBe(false);
  });

  it('is true when the sustained rung has no next level', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, root, '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
    ];
    expect(reachedSustainedPause([sustained(0)], apiData, root)).toBe(true);
  });

  it('is true when the next level is not the root', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, root, '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('q1', 1, 'Is justice a matter of law or of right reason?', '2026-01-01T00:03:00Z'),
    ];
    expect(reachedSustainedPause([sustained(0)], apiData, root)).toBe(true);
  });

  it('is false when the last synthesis was not sustained', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, root, '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
    ];
    expect(reachedSustainedPause([{ level: 0, isSustained: false }], apiData, root)).toBe(false);
  });

  it('is false when there are no syntheses', () => {
    expect(reachedSustainedPause([], [], root)).toBe(false);
  });

  it('is true when the root question is unknown', () => {
    const apiData: RawApiNode[] = [synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z')];
    expect(reachedSustainedPause([sustained(0)], apiData, undefined)).toBe(true);
  });

  it('matches the root case-insensitively', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, root, '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('q1', 1, root.toUpperCase(), '2026-01-01T00:03:00Z'),
    ];
    expect(reachedSustainedPause([sustained(0)], apiData, root)).toBe(false);
  });

  it('uses the highest-level synthesis, not the first', () => {
    const apiData: RawApiNode[] = [
      question('q0', 0, root, '2026-01-01T00:00:00Z'),
      synthesis('s0', 'mediator_synthesis', 0, '2026-01-01T00:02:00Z'),
      question('q1', 1, 'Is justice a matter of law or of right reason?', '2026-01-01T00:03:00Z'),
      synthesis('s1', 'mediator_synthesis', 1, '2026-01-01T00:05:00Z'),
    ];
    // Level 0 returned to the root; level 1 is the last and did not.
    expect(reachedSustainedPause([sustained(1), sustained(0)], apiData, root)).toBe(true);
  });
});
