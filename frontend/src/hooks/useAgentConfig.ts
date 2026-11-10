import { useState, useEffect, useCallback } from 'react';
import { AGENT_ICON_MAP, classifyAgentRole } from '../config/agentRegistry';
import { roleColor } from '../theme/prime-radiant-tokens';
import { getConfig } from '../lib/config';
import type { ModuleType } from '../App';

export type { ModuleType };
export type AgentRole = 'thesis' | 'antithesis' | 'mediation' | 'synthesis' | 'gate' | 'system' | 'question' | 'moderator';

interface AgentConfig {
  enabled: boolean;
  qualityWeight: number;
}

function buildDefaultAgents(): Record<string, AgentConfig> {
  const agents = getConfig().agents;
  const map: Record<string, AgentConfig> = {};
  for (const id of agents) {
    map[id] = { enabled: true, qualityWeight: 1.0 };
  }
  return map;
}

const STORAGE_KEY = 'seldon-agent-config';

function loadAgentConfig(module: ModuleType): Record<string, AgentConfig> {
  try {
    const defaults = buildDefaultAgents();
    const raw = localStorage.getItem(`${STORAGE_KEY}-${module}`);
    const userConfig = raw ? JSON.parse(raw) : {};
    return { ...defaults, ...userConfig };
  } catch {
    return buildDefaultAgents();
  }
}

function persistAgentConfig(module: ModuleType, config: Record<string, AgentConfig>) {
  localStorage.setItem(`${STORAGE_KEY}-${module}`, JSON.stringify(config));
}

export function useAgentConfig(initialModule: ModuleType) {
  const [module, setModule] = useState<ModuleType>(initialModule);
  const [agents, setAgents] = useState<Record<string, AgentConfig>>(() => loadAgentConfig(initialModule));

  useEffect(() => {
    setAgents(loadAgentConfig(module));
  }, [module]);

  const updateAgent = useCallback((agentId: string, updates: Partial<AgentConfig>) => {
    setAgents((prev) => {
      const next = { ...prev, [agentId]: { ...prev[agentId], ...updates } };
      persistAgentConfig(module, next);
      return next;
    });
  }, [module]);

  const toggleAgent = useCallback((agentId: string) => {
    setAgents((prev) => {
      const defaults = buildDefaultAgents();
      const current = prev[agentId] || defaults[agentId];
      if (!current) return prev;
      const next = { ...prev, [agentId]: { ...current, enabled: !current.enabled } };
      persistAgentConfig(module, next);
      return next;
    });
  }, [module]);

  const resetAgents = useCallback(() => {
    const defaults = buildDefaultAgents();
    setAgents(defaults);
    localStorage.removeItem(`${STORAGE_KEY}-${module}`);
  }, [module]);

  return {
    module,
    setModule,
    agents,
    updateAgent,
    toggleAgent,
    resetAgents,
    defaultAgent: buildDefaultAgents(),
  };
}

export function getAgentRole(agentId: string, _module: ModuleType): AgentRole {
  return classifyAgentRole(agentId);
}

export function getAgentColor(agentId: string, _module: ModuleType = 'philosophy'): string {
  const spec = AGENT_ICON_MAP[agentId.toLowerCase()];
  if (spec) {
    return roleColor(spec.accentColorRole);
  }
  const role = getAgentRole(agentId, _module);
  const roleColors: Record<AgentRole, string> = {
    thesis: '#b8924a',
    antithesis: '#8a7d68',
    mediation: '#4a8a6b',
    moderator: '#b8924a',
    synthesis: '#6e8c74',
    gate: '#b05a3a',
    system: '#6b6153',
    question: '#d9cbb0',
  };
  return roleColors[role] || '#d9cbb0';
}

export function getAgentIcon(agentId: string): string {
  return agentId.charAt(0).toUpperCase();
}
