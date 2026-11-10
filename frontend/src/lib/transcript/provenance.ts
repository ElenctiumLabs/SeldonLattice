/** True when a string is a Merkle content address (`algorithm-hash`). */
export function isMerkleCid(value: string | null | undefined): value is string {
  return typeof value === 'string' && /^sha(?:3-256|256)-[A-Za-z0-9_-]+$/.test(value.trim());
}

/** Recovers the Merkle CID from a reconstructed turn id (`node-<cid>`). */
export function merkleCidFromTurnId(id: string | null | undefined): string | undefined {
  if (!id || !id.startsWith('node-')) return undefined;
  const candidate = id.slice('node-'.length);
  return isMerkleCid(candidate) ? candidate : undefined;
}

/**
 * Flattens the node's explicit parent references into one de-duplicated lineage
 * list. `parentHash` is the primary edge; `parentHashes` carries the additional
 * ancestors (for example a synthesis's dual thesis/antithesis parents).
 */
export function collectParentHashes(
  node: { parentHash?: string | null; parentHashes?: string[] | null },
): string[] {
  const seen = new Set<string>();
  const parents: string[] = [];
  const add = (value: string | null | undefined) => {
    const parent = value?.trim();
    if (!parent || seen.has(parent)) return;
    seen.add(parent);
    parents.push(parent);
  };
  add(node.parentHash);
  for (const parent of node.parentHashes ?? []) add(parent);
  return parents;
}
