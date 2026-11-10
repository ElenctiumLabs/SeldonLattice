import { describe, it, expect, vi, afterEach } from 'vitest';
import { SeldonClient } from '../client';

/**
 * GET /api/nodes clamps pageSize to 200 and defaults to 50. A session deeper than
 * one page therefore came back truncated: the graph showed ~5-6 of 10 rounds and
 * the cut landed mid-round, because the backend orders by timestamp. getNodesFlat
 * must collect every page.
 */
describe('getNodesFlat pages to completion', () => {
  const makeNode = (i: number) => ({
    cid: `cid-${i}`,
    nodeType: 'skeptic_reply',
    level: Math.floor(i / 10),
    parentHash: null,
    parentHashes: null,
    content: `turn ${i}`,
    cyclePhase: 'growth',
    variabilityScore: 0,
    resonanceScore: 0,
    isSecondaryCenter: false,
    centerShiftScore: 0,
    sessionId: 's1',
    speakerId: 'skeptic',
    moderatorSelectionConfidence: 0,
    timestamp: `2026-01-01T00:00:${String(i % 60).padStart(2, '0')}Z`,
    parentPatternHash: null,
    ascentScore: 0,
    irreducibleTension: 0,
    reasoningTrace: null,
    remainingDissensus: null,
    repeatsPremise: null,
    unresolvedTensions: null,
    tensionIntensity: null,
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('collects all pages and reports every round', async () => {
    const all = Array.from({ length: 250 }, (_, i) => makeNode(i));
    const requestedPages: number[] = [];

    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      const u = new URL(url);
      const page = Number(u.searchParams.get('page') ?? '1');
      const pageSize = Number(u.searchParams.get('pageSize') ?? '50');
      requestedPages.push(page);
      const start = (page - 1) * pageSize;
      const items = all.slice(start, start + pageSize);
      const payload = JSON.stringify({
        items,
        totalCount: all.length,
        page,
        pageSize,
        totalPages: Math.ceil(all.length / pageSize),
      });
      return {
        ok: true,
        status: 200,
        headers: new Headers({ 'content-type': 'application/json' }),
        text: async () => payload,
      } as unknown as Response;
    }));

    const client = new SeldonClient({ baseUrl: 'http://localhost:5050' });
    const nodes = await client.getNodesFlat({ sessionId: 's1', pageSize: 200 });

    expect(nodes).toHaveLength(250);
    expect(requestedPages).toEqual([1, 2]);
    // 250 nodes at 10 per level => 25 levels, so nothing was cut mid-session.
    const levels = [...new Set(nodes.map((n) => n.level))];
    expect(levels[levels.length - 1]).toBe(24);
  });

  it('caps the requested page size at the backend maximum of 200', async () => {
    let seenPageSize = '';
    vi.stubGlobal('fetch', vi.fn(async (url: string) => {
      const u = new URL(url);
      seenPageSize = u.searchParams.get('pageSize') ?? '';
      return {
        ok: true,
        status: 200,
        headers: new Headers({ 'content-type': 'application/json' }),
        text: async () => JSON.stringify({ items: [], totalCount: 0, page: 1, pageSize: 200, totalPages: 1 }),
      } as unknown as Response;
    }));

    const client = new SeldonClient({ baseUrl: 'http://localhost:5050' });
    await client.getNodesFlat({ sessionId: 's1', pageSize: 5000 });

    expect(seenPageSize).toBe('200');
  });
});
