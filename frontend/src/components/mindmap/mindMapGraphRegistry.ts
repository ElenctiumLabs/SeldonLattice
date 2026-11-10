import { DialecticalNode, FloatingEdge } from './MindMapGraphPrimitives';

// Keep these registries at module scope so React Flow receives stable references.
export const NODE_TYPES = { dialectical: DialecticalNode };
export const EDGE_TYPES = { floating: FloatingEdge };
