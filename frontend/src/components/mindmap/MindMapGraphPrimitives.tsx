import React, { useRef } from 'react';
import {
  Handle,
  Position,
  getStraightPath,
  useStore,
  type EdgeProps,
  type ReactFlowState,
  type XYPosition,
} from 'reactflow';
import type { NodeProps } from 'reactflow';
import { roleOf, roleColor, type DialecticalRole } from '../../theme/prime-radiant-tokens';
import { AGENT_ICON_MAP } from '../../config/agentRegistry';
import { mindMapHoverRef } from './mindMapHover';
import {
  SparkleIcon,
  TriangleIcon,
  DialIcon,
  GateIcon,
  TrendingUpIcon,
  ScaleIcon,
  ReturnIcon,
} from '../IconSet';

export function MarkerDefinitions() {
  const roles: Array<DialecticalRole> = [
    'question', 'proposer', 'skeptic', 'judge',
    'mediation',
  ];
  return (
    <svg style={{ position: 'absolute', width: 0, height: 0 }}>
      <defs>
        {roles.map((role) => (
          <marker
            key={role}
            id={`${role}-arrow`}
            markerWidth="10"
            markerHeight="10"
            viewBox="0 0 10 10"
            refX="8"
            refY="5"
            orient="auto"
            markerUnits="strokeWidth"
          >
            <path d="M 0 0 L 10 5 L 0 10 z" fill={roleColor(role)} />
          </marker>
        ))}
      </defs>
    </svg>
  );
}

const hiddenHandleStyle: React.CSSProperties = {
  opacity: 0,
  visibility: 'hidden',
  width: 0,
  height: 0,
  minWidth: 0,
  minHeight: 0,
  border: 0,
  pointerEvents: 'none',
};

export function DialecticalNode({ data }: NodeProps) {
  const nodeRef = useRef<HTMLDivElement>(null);
  const role = (data.role as DialecticalRole) ?? roleOf(data.agent);
  const dialecticRole = data.dialecticRole ?? 'antithesis';
  const speaker = (data.agent || data.label || '').toLowerCase();
  const isEcho = data.isEcho === true;

  let accentColor = roleColor('proposer');
  let IconComponent = TriangleIcon;
  const agentSpec = AGENT_ICON_MAP[speaker];

  if (isEcho) {
    accentColor = roleColor('question');
    IconComponent = ReturnIcon;
  } else if (speaker.includes('system') || role === 'question' || data.nodeType === 'question') {
    accentColor = roleColor('question');
    IconComponent = SparkleIcon;
  } else if (speaker.includes('gate') || data.role === 'gate' || data.nodeType === 'gate') {
    accentColor = '#e74c3c';
    IconComponent = GateIcon;
  } else if (agentSpec) {
    accentColor = roleColor(agentSpec.accentColorRole);
    IconComponent = agentSpec.icon;
  } else if (dialecticRole === 'synthesis') {
    accentColor = roleColor('proposer');
    IconComponent = DialIcon;
  } else if (dialecticRole === 'thesis') {
    accentColor = roleColor('skeptic');
    IconComponent = TrendingUpIcon;
  } else if (dialecticRole === 'antithesis') {
    accentColor = roleColor('judge');
    IconComponent = ScaleIcon;
  }

  const handleHover = (on: boolean) => {
    const fn = mindMapHoverRef.current;
    if (on && fn && nodeRef.current) {
      const rect = nodeRef.current.getBoundingClientRect();
      fn({
        nodeId: data.id ?? '',
        screenX: rect.left + rect.width / 2,
        screenY: rect.top - 8,
        data,
        accentColor,
      });
    } else if (!on && fn) {
      fn(null);
    }
  };

  return (
    <div
      ref={nodeRef}
      className="flex flex-col items-center justify-center transition-transform duration-200 hover:scale-110 shadow-lg relative group cursor-pointer rounded-full select-none"
      style={{
        width: 96,
        height: 96,
        background: 'var(--pr-surface)',
        border: `2px ${data.isPending || isEcho ? 'dashed' : 'solid'} ${accentColor}`,
        boxShadow: `0 0 18px ${accentColor}35, inset 0 0 20px ${accentColor}15`,
        color: accentColor,
        opacity: data.isPending ? 0.55 : 1,
      }}
      onMouseEnter={() => handleHover(true)}
      onMouseLeave={() => handleHover(false)}
    >
      {[Position.Top, Position.Bottom, Position.Left, Position.Right].map((position) => (
        <Handle key={`target-${position}`} type="target" position={position} className="opacity-0! w-0! h-0!" style={hiddenHandleStyle} />
      ))}
      <IconComponent size={26} color={accentColor} />
      {[Position.Top, Position.Bottom, Position.Left, Position.Right].map((position) => (
        <Handle key={`source-${position}`} type="source" position={position} className="opacity-0! w-0! h-0!" style={hiddenHandleStyle} />
      ))}
    </div>
  );
}

function getNodeRadius(node: any): { halfW: number; halfH: number } {
  const isQuestion = node.data?.role === 'question' || node.data?.agent === 'question';
  const size = isQuestion ? 120 : 96;
  return { halfW: (node.width || size) / 2, halfH: (node.height || size) / 2 };
}

function getNodeCenter(node: any): XYPosition {
  const pos = node.positionAbsolute ?? node.position;
  const { halfW, halfH } = getNodeRadius(node);
  return { x: pos.x + halfW, y: pos.y + halfH };
}

function getIntersectionPoint(
  sourceCenter: XYPosition,
  targetCenter: XYPosition,
  halfW: number,
  halfH: number,
): XYPosition {
  const dx = targetCenter.x - sourceCenter.x;
  const dy = targetCenter.y - sourceCenter.y;
  if (dx === 0 && dy === 0) return sourceCenter;

  const angle = Math.atan2(dy, dx);
  const cos = Math.cos(angle);
  const sin = Math.sin(angle);
  const scale = (halfW * halfH) / Math.sqrt((halfH * cos) ** 2 + (halfW * sin) ** 2);
  return { x: sourceCenter.x + scale * cos, y: sourceCenter.y + scale * sin };
}

export function FloatingEdge({ id, source, target, style }: EdgeProps) {
  const sourceNode = useStore((s: ReactFlowState) => s.nodeInternals.get(source));
  const targetNode = useStore((s: ReactFlowState) => s.nodeInternals.get(target));
  if (!sourceNode || !targetNode) return null;

  const sCenter = getNodeCenter(sourceNode);
  const tCenter = getNodeCenter(targetNode);
  const { halfW: sHalfW, halfH: sHalfH } = getNodeRadius(sourceNode);
  const { halfW: tHalfW, halfH: tHalfH } = getNodeRadius(targetNode);
  const sourcePoint = getIntersectionPoint(sCenter, tCenter, sHalfW, sHalfH);
  const targetPoint = getIntersectionPoint(tCenter, sCenter, tHalfW, tHalfH);
  const [edgePath] = getStraightPath({
    sourceX: sourcePoint.x,
    sourceY: sourcePoint.y,
    targetX: targetPoint.x,
    targetY: targetPoint.y,
  });

  const isChain = id.startsWith('e-chain-');
  const customMarkerEnd = isChain ? undefined : (() => {
    const sourceAgent = (sourceNode.data?.agent as string) ?? '';
    const sourceSpec = AGENT_ICON_MAP[sourceAgent];
    const sourceRole = sourceSpec?.accentColorRole ?? (sourceNode.data?.role as DialecticalRole) ?? 'proposer';
    return `url(#${sourceRole}-arrow)`;
  })();

  return <path id={id} className="react-flow__edge-path" d={edgePath} markerEnd={customMarkerEnd} style={style} />;
}
