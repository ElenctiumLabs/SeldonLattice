import React from 'react';

export interface IconProps {
  className?: string;
  size?: number;
  color?: string;
}

/** Shared component type for all icons in this set. */
export type IconComponentType = React.FC<IconProps>;

/** Circular return arrow for fractal-echo nodes (the inquiry returning to its root). */
export const ReturnIcon: React.FC<IconProps> = ({ className = '', size = 20, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <path d="M20 12a8 8 0 1 1-2.34-5.66" />
    <polyline points="20 4 20 10 14 10" />
    <circle cx="12" cy="12" r="1.6" fill={color} stroke="none" />
  </svg>
);

/** 4-pointed Sparkle / Star Icon for System & Opening Question nodes */
export const SparkleIcon: React.FC<IconProps> = ({ className = '', size = 20, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <path d="M12 2v20M2 12h20M7 7l10 10M17 7L7 17" opacity="0.25" />
    <path d="M12 3L14.5 9.5L21 12L14.5 14.5L12 21L9.5 14.5L3 12L9.5 9.5L12 3Z" fill={color} fillOpacity="0.8" stroke={color} />
  </svg>
);

export const TriangleIcon: React.FC<IconProps> = ({ className = '', size = 18, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <polygon points="12,4 21,20 3,20" fill="none" stroke={color} strokeWidth="2.2" />
  </svg>
);

/** Custom Dial / Keyhole Icon for synthesis nodes */
export const DialIcon: React.FC<IconProps> = ({ className = '', size = 20, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <circle cx="12" cy="9" r="4.5" stroke={color} strokeWidth="2" />
    <circle cx="12" cy="9" r="1.5" fill={color} />
    <path d="M12 13.5V20" stroke={color} strokeWidth="2" />
    <circle cx="12" cy="20" r="1.2" fill={color} />
  </svg>
);

/** Gate Alert / Quality Validation Icon */
export const GateIcon: React.FC<IconProps> = ({ className = '', size = 18, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <circle cx="12" cy="12" r="9" stroke={color} strokeWidth="2" />
    <line x1="12" y1="8" x2="12" y2="12" stroke={color} strokeWidth="2" />
    <circle cx="12" cy="16" r="1" fill={color} />
  </svg>
);

/** Mindmap Zoom In Icon */
export const ZoomInIcon: React.FC<IconProps> = ({ size = 14, color = 'currentColor' }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2">
    <circle cx="11" cy="11" r="7" />
    <line x1="11" y1="8" x2="11" y2="14" />
    <line x1="8" y1="11" x2="14" y2="11" />
    <line x1="16" y1="16" x2="21" y2="21" />
  </svg>
);

/** Mindmap Zoom Out Icon */
export const ZoomOutIcon: React.FC<IconProps> = ({ size = 14, color = 'currentColor' }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2">
    <circle cx="11" cy="11" r="7" />
    <line x1="8" y1="11" x2="14" y2="11" />
    <line x1="16" y1="16" x2="21" y2="21" />
  </svg>
);

/** Mindmap Fit View / Target Icon */
export const FitViewIcon: React.FC<IconProps> = ({ size = 14, color = 'currentColor' }) => (
  <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke={color} strokeWidth="2">
    <circle cx="12" cy="12" r="3" />
    <path d="M3 12h4M17 12h4M12 3v4M12 17v4" />
  </svg>
);

export const ScaleIcon: React.FC<IconProps> = ({ className = '', size = 20, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <path d="M12 3v18" />
    <path d="M8 7l4-4 4 4" />
    <path d="M4 7l4 8h0a5 5 0 008 0h0l4-8" />
    <circle cx="8" cy="15" r="1.5" fill={color} fillOpacity="0.3" />
    <circle cx="16" cy="15" r="1.5" fill={color} fillOpacity="0.3" />
  </svg>
);

/** Trending Up Icon - growth, ascent */
export const TrendingUpIcon: React.FC<IconProps> = ({ className = '', size = 20, color = 'currentColor' }) => (
  <svg
    width={size}
    height={size}
    viewBox="0 0 24 24"
    fill="none"
    stroke={color}
    strokeWidth="1.8"
    strokeLinecap="round"
    strokeLinejoin="round"
    className={className}
  >
    <polyline points="22 7 13.5 15.5 8.5 10.5 2 17" />
    <polyline points="16 7 22 7 22 13" />
  </svg>
);
