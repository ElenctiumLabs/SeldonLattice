import React from 'react';
import { createPortal } from 'react-dom';
import ReactFlow from 'reactflow';
import type { Edge, Node } from 'reactflow';
import CosmicBackdropCanvas from '../CosmicBackdropCanvas';
import { MarkerDefinitions } from './MindMapGraphPrimitives';
import { NODE_TYPES, EDGE_TYPES } from './mindMapGraphRegistry';
import type { HoverInfo } from './mindMapHover';

const DEFAULT_EDGE_OPTIONS = { type: 'floating' };
const FIT_VIEW_OPTIONS = { padding: 0.4 };
const PRO_OPTIONS = { hideAttribution: true };

interface MindMapCanvasProps {
  layoutMode: 'dialectical' | 'helical';
  nodes: Node[];
  edges: Edge[];
  onNodeClick: (event: React.MouseEvent, node: Node) => void;
  hoverInfo: HoverInfo | null;
}

export default function MindMapCanvas({
  layoutMode,
  nodes,
  edges,
  onNodeClick,
  hoverInfo,
}: MindMapCanvasProps) {
  return (
    <>
      {layoutMode === 'helical' && <CosmicBackdropCanvas />}

      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={NODE_TYPES}
        edgeTypes={EDGE_TYPES}
        className={`pr-graph-canvas relative z-10 ${layoutMode === 'helical' ? 'bg-transparent! bg-none!' : ''}`}
        onNodeClick={onNodeClick}
        fitView
        fitViewOptions={FIT_VIEW_OPTIONS}
        proOptions={PRO_OPTIONS}
        defaultEdgeOptions={DEFAULT_EDGE_OPTIONS}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        zoomOnScroll={false}
        panOnScroll={false}
      >
        <MarkerDefinitions />
      </ReactFlow>

      {hoverInfo?.data.content && createPortal(
        <HoverCard hoverInfo={hoverInfo} />,
        document.body,
      )}
    </>
  );
}

function HoverCard({ hoverInfo }: { hoverInfo: HoverInfo }) {
  const dialecticRole = hoverInfo.data.dialecticRole ?? 'antithesis';
  const content = String(hoverInfo.data.content);

  return (
    <div
      className="fixed z-9999 w-48 p-2.5 rounded-lg text-left pointer-events-none pr-glass-surface"
      style={{
        left: hoverInfo.screenX,
        top: hoverInfo.screenY,
        transform: 'translate(-50%, -100%)',
        background: 'var(--pr-surface)',
        border: `1px solid ${hoverInfo.accentColor}70`,
        boxShadow: `0 8px 24px rgba(0,0,0,0.25), 0 0 12px ${hoverInfo.accentColor}20`,
        maxHeight: 120,
        overflow: 'hidden',
      }}
    >
      <div className="text-[11px] font-mono font-bold tracking-wider mb-0.5" style={{ color: hoverInfo.accentColor }}>
        {hoverInfo.data.label ?? hoverInfo.data.agent}
        {hoverInfo.data.level != null && <span className="ml-1 opacity-100">R{hoverInfo.data.level + 1}</span>}
        {dialecticRole && <span className="ml-1 opacity-100 capitalize">{dialecticRole}</span>}
      </div>
      <div className="text-[12px] leading-snug line-clamp-4" style={{ color: 'var(--pr-text)' }}>
        {content.slice(0, 120)}{content.length > 120 ? 'ª' : ''}
      </div>
      {hoverInfo.data.phase && (
        <div className="text-[10px] mt-1 opacity-100 font-mono" style={{ color: hoverInfo.accentColor }}>
          {hoverInfo.data.phase}
        </div>
      )}
    </div>
  );
}
