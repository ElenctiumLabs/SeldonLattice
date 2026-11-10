import type { RawApiNode } from '../../layout';

/**
 * Filters the flat raw API nodes list to remove abandoned/older runs of dialectical ascent.
 * If a level has been executed multiple times (e.g. stopped and resumed), this keeps only
 * the lineage of the latest active question node at each level, discarding duplicate nodes.
 */
export function filterLatestPath(apiData: RawApiNode[]): RawApiNode[] {
  // Session-aware: levels collide across sessions (every session starts at L0),
  // so the active-question selection must happen per session. Without this,
  // any cross-session fetch wipes all but the newest session's tree.
  const bySession = new Map<string, RawApiNode[]>();
  for (const node of apiData) {
    const key = node.sessionId ?? '';
    if (!bySession.has(key)) bySession.set(key, []);
    bySession.get(key)!.push(node);
  }

  const result: RawApiNode[] = [];
  for (const nodes of bySession.values()) {
    result.push(...filterLatestPathForSession(nodes));
  }
  return result;
}

function filterLatestPathForSession(apiData: RawApiNode[]): RawApiNode[] {
  // 1. Group questions by level
  const questionsByLevel = new Map<number, RawApiNode[]>();
  for (const node of apiData) {
    if ((node.nodeType ?? '').toLowerCase() === 'question') {
      const lvl = node.level ?? 0;
      if (!questionsByLevel.has(lvl)) {
        questionsByLevel.set(lvl, []);
      }
      questionsByLevel.get(lvl)!.push(node);
    }
  }

  // 2. Identify the active question CID for each level (the one with the latest timestamp/last in array)
  const activeQuestionCidByLevel = new Map<number, string>();
  const inactiveQuestionCids = new Set<string>();

  for (const [lvl, qNodes] of questionsByLevel.entries()) {
    const sorted = [...qNodes].sort((a, b) => {
      const timeA = a.timestamp ? Date.parse(a.timestamp) : 0;
      const timeB = b.timestamp ? Date.parse(b.timestamp) : 0;
      return timeA - timeB;
    });

    const activeQ = sorted[sorted.length - 1];
    activeQuestionCidByLevel.set(lvl, activeQ.cid);

    for (let i = 0; i < sorted.length - 1; i++) {
      inactiveQuestionCids.add(sorted[i].cid);
    }
  }

  // 3. Build a parent-child map for traversal
  const parentToChildren = new Map<string, string[]>();
  for (const node of apiData) {
    const parentHash = node.parentHash;
    if (parentHash) {
      if (!parentToChildren.has(parentHash)) parentToChildren.set(parentHash, []);
      parentToChildren.get(parentHash)!.push(node.cid);
    }
    if (node.parentHashes) {
      for (const p of node.parentHashes) {
        if (!parentToChildren.has(p)) parentToChildren.set(p, []);
        parentToChildren.get(p)!.push(node.cid);
      }
    }
  }

  // 4. Traverse down from each active question to find all valid descendant CIDs
  const activeCids = new Set<string>();
  const nodeMap = new Map<string, RawApiNode>();
  for (const node of apiData) {
    nodeMap.set(node.cid, node);
  }

  for (const qCid of activeQuestionCidByLevel.values()) {
    const queue = [qCid];
    const visited = new Set<string>();

    while (queue.length > 0) {
      const curr = queue.shift()!;
      if (visited.has(curr)) continue;
      visited.add(curr);

      if (nodeMap.has(curr)) {
        activeCids.add(curr);
      }

      const children = parentToChildren.get(curr) || [];
      queue.push(...children);
    }
  }

  // 5. Filter the nodes: keep if it's explicitly part of the active path, OR if it's an orphan
  // that was created after the active question of its level (so it belongs to the latest run).
  return apiData.filter(node => {
    if (activeCids.has(node.cid)) return true;
    if (inactiveQuestionCids.has(node.cid)) return false;

    const activeQCid = activeQuestionCidByLevel.get(node.level);
    if (!activeQCid) return true; // Gap in deletion - keep nodes at this level

    const activeQ = nodeMap.get(activeQCid);
    if (activeQ && activeQ.timestamp && node.timestamp) {
      const nodeTime = Date.parse(node.timestamp);
      const activeQTime = Date.parse(activeQ.timestamp);
      if (!isNaN(nodeTime) && !isNaN(activeQTime) && nodeTime < activeQTime) {
        return false;
      }
    }

    return true;
  });
}

function sortByTimestamp<T extends { timestamp?: string }>(nodes: T[]): T[] {
  const parse = (t?: string): number => (t ? Date.parse(t) : 0);
  return [...nodes].sort((a, b) => parse(a.timestamp) - parse(b.timestamp));
}

/**
 * Returns the ROOT thesis - the content of the level-0 'question' node that
 * seeded the session. The session's root question is stored only once (the
 * earliest level-0 question), never the latest level synthesis.
 */
export function findRootQuestion(apiData: RawApiNode[]): string | null {
  const root = sortByTimestamp(
    apiData.filter(
      (n) =>
        (n.nodeType ?? '').toLowerCase() === 'question' &&
        (n.level ?? 0) === 0 &&
        n.content?.trim().length > 0,
    ),
  )[0];
  return root?.content?.trim() ? root.content : null;
}

/**
 * Returns the active-path question content for the given level. This is the
 * "latest thesis" that seeds debate at that rung of ascent - distinct from the
 * root thesis (see findRootQuestion).
 */
export function findQuestionForLevel(apiData: RawApiNode[], level: number): string | null {
  const candidates = sortByTimestamp(
    apiData.filter((n) => (n.nodeType ?? '').toLowerCase() === 'question' && (n.level ?? 0) === level),
  );
  if (candidates.length === 0) return null;
  const active = candidates[candidates.length - 1];
  return active?.content?.trim() ? active.content : null;
}

/**
 * Decides whether a reopened session should resume in the handover state.
 *
 * A sustained rung that returned to the root continued the ladder; a sustained rung that
 * did not stopped there, and reopening the session must not silently resume past it.
 * Mirrors the backend rule in FractalEcho.ResolveNextTheme: the root is the next rung
 * only when the fractal echo actually fired, which the persisted nodes record as a
 * question at the next level whose content is the root.
 */
export function reachedSustainedPause(
  syntheses: { level?: number; isSustained?: boolean }[],
  apiData: RawApiNode[],
  rootQuestion: string | undefined,
): boolean {
  const sorted = [...syntheses].sort((a, b) => (a.level ?? 0) - (b.level ?? 0));
  const last = sorted[sorted.length - 1];
  if (!last || last.isSustained !== true) return false;

  const rootKey = (rootQuestion ?? '').trim().toLowerCase();
  if (rootKey === '') return true;

  const returnedToRoot =
    (findQuestionForLevel(apiData, (last.level ?? 0) + 1) ?? '').trim().toLowerCase() === rootKey;
  return !returnedToRoot;
}
