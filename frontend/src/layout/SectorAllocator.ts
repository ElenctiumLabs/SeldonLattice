import type { LayoutGraph, LayoutNode } from './types';

/**
 * Assigns each node an angular sector of the circle, recursively splitting
 * a parent's sector evenly among its children. This keeps a subtree's
 * angular position consistent with its ancestry - useful if a future
 * layout wants child rings to stay near their parent's angle instead of
 * being spread independently per level, the way DialecticalLayout does
 * today.
 *
 * Not currently wired into DialecticalLayout - kept as a self-contained,
 * working utility for that future hierarchical mode rather than the dead
 * stub it used to be.
 */
export class SectorAllocator {
  allocate(graph: LayoutGraph, root: LayoutNode): void {
    this.allocateRecursive(graph, root, 0, Math.PI * 2);
  }

  private allocateRecursive(graph: LayoutGraph, node: LayoutNode, start: number, end: number): void {
    node.sectorStart = start;
    node.sectorEnd = end;
    node.angle = (start + end) * 0.5;

    if (node.children.length === 0) return;

    const slice = (end - start) / node.children.length;

    node.children.forEach((childId, index) => {
      const child = graph.nodes.get(childId);
      if (!child) return;

      const childStart = start + index * slice;
      const childEnd = childStart + slice;
      this.allocateRecursive(graph, child, childStart, childEnd);
    });
  }
}
