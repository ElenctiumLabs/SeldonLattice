import { useCallback, useRef, useState } from 'react';
import type { RefObject } from 'react';
import type { Edge, Node, useReactFlow } from 'reactflow';
import {
  GraphBuilder,
  DialecticalLayout,
  HelicalLayout,
  toReactFlow,
  classifyNodeType,
} from '../../../layout';
import type { RawApiNode } from '../../../layout';
import type { LiveTurn } from '../../../types/liveTurn';
import { api } from '../../../lib/api';
import { liveTurnsFromApiNodes, filterLatestPath } from '../../../lib/transcript';
import { getConfig } from '../../../lib/config';
import { isCompletedSynthesisNodeType } from '../../../config/agentRegistry';
import { annotateFractalEchoNodes } from '../mindMapGraphData';

type FitView = ReturnType<typeof useReactFlow>['fitView'];
type SetLiveTurns = (updater: LiveTurn[] | ((prev: LiveTurn[]) => LiveTurn[])) => void;

interface UseMindMapGraphOptions {
  layoutMode: 'dialectical' | 'helical';
  fitView: FitView;
  liveTurnsRef: RefObject<LiveTurn[]>;
  setLiveTurns: SetLiveTurns;
  nodesRef: RefObject<Node[]>;
  lastApiDataRef: RefObject<RawApiNode[]>;
  pendingQuestionsRef: RefObject<Map<number, RawApiNode>>;
  pendingDebateNodesRef: RefObject<Map<string, RawApiNode>>;
  pendingInterventionsRef: RefObject<RawApiNode[]>;
}

/** Owns graph construction, optimistic graph overlays, and node reconstruction. */
export function useMindMapGraph({
  layoutMode,
  fitView,
  liveTurnsRef,
  setLiveTurns,
  nodesRef,
  lastApiDataRef,
  pendingQuestionsRef,
  pendingDebateNodesRef,
  pendingInterventionsRef,
}: UseMindMapGraphOptions) {
  const [nodes, setNodes] = useState<Node[]>([]);
  const [edges, setEdges] = useState<Edge[]>([]);
  const graphStateRefs = useRef({
    nodesRef,
    lastApiDataRef,
    pendingQuestionsRef,
    pendingDebateNodesRef,
    pendingInterventionsRef,
  });

  const graphBuilderRef = useRef(new GraphBuilder());
  const dialecticalLayoutRef = useRef(new DialecticalLayout());
  const helicalLayoutRef = useRef(new HelicalLayout());

  const buildAndSetGraph = useCallback(
    (apiData: RawApiNode[]) => {
      const filteredApiData = filterLatestPath(apiData);
      const graphApiData = annotateFractalEchoNodes(filteredApiData);

      const coveredLevels = new Set(
        graphApiData
          .filter((item) => classifyNodeType(item.nodeType) === 'question')
          .map((item) => item.level)
      );

      const refs = graphStateRefs.current;
      refs.pendingQuestionsRef.current.forEach((_pending, level) => {
        if (coveredLevels.has(level)) refs.pendingQuestionsRef.current.delete(level);
      });

      // Keep optimistic debate turns until the persisted synthesis confirms the level.
      const confirmedSynthLevels = new Set(
        graphApiData
          .filter((item) => isCompletedSynthesisNodeType(item.nodeType))
          .map((item) => item.level)
      );
      refs.pendingDebateNodesRef.current.forEach((_value, key) => {
        const level = parseInt(key.split('-')[0], 10);
        if (confirmedSynthLevels.has(level)) refs.pendingDebateNodesRef.current.delete(key);
      });

      const merged = [
        ...graphApiData,
        ...refs.pendingQuestionsRef.current.values(),
        ...refs.pendingDebateNodesRef.current.values(),
        ...refs.pendingInterventionsRef.current,
      ];

      const graph = graphBuilderRef.current.build(merged);
      if (layoutMode === 'helical') {
        helicalLayoutRef.current.layout(graph);
      } else {
        dialecticalLayoutRef.current.layout(graph);
      }

      const { nodes: flowNodes, edges: flowEdges } = toReactFlow(graph);
      setNodes(flowNodes);
      setEdges(flowEdges);
      refs.nodesRef.current = flowNodes;

      setTimeout(() => {
        const config = getConfig();
        fitView({ padding: config.fitViewAllPadding, duration: config.fitViewAllDuration });
      }, 50);
    },
    [fitView, layoutMode]
  );

  const fetchNodes = useCallback(
    async (sessionId?: string | null) => {
      if (!sessionId) return;
      try {
        const apiData: RawApiNode[] = await api.getNodesFlat({ sessionId, pageSize: 200 });
        graphStateRefs.current.lastApiDataRef.current = apiData;
        buildAndSetGraph(apiData);

        // Preserve stream-only metrics while rebuilding turns from persisted nodes.
        const reconstructed = liveTurnsFromApiNodes(apiData, liveTurnsRef.current);
        setLiveTurns(reconstructed);
      } catch (error) {
        console.error('[MindMap] Failed to fetch nodes:', error);
      }
    },
    [buildAndSetGraph, liveTurnsRef, setLiveTurns]
  );

  const resetGraph = useCallback(() => {
    setNodes([]);
    setEdges([]);
    graphStateRefs.current.nodesRef.current = [];
  }, []);

  return {
    nodes,
    edges,
    buildAndSetGraph,
    fetchNodes,
    resetGraph,
  };
}
