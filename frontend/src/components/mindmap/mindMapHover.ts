export interface HoverInfo {
  nodeId: string;
  screenX: number;
  screenY: number;
  data: any;
  accentColor: string;
}

export const mindMapHoverRef: { current: ((info: HoverInfo | null) => void) | null } = { current: null };
