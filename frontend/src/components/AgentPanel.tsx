import { useState, useEffect } from 'react';
import type { ModuleType } from '../App';
import { useRequiredFrameworkSettings } from '../hooks/useFrameworkSettings';
import { getConfig } from '../lib/config';
import { getAgentRole, getAgentColor, getAgentIcon } from '../hooks/useAgentConfig';

interface AgentPanelProps {
  module: ModuleType;
  onClose: () => void;
}

export default function AgentPanel({ module, onClose }: AgentPanelProps) {
  const { agents, updateAgent, toggleAgent, resetAgents } = useRequiredFrameworkSettings(module);
  const [searchQuery, setSearchQuery] = useState('');
  const [weightUpdating, setWeightUpdating] = useState<string | null>(null);

  const filteredAgents = Object.entries(agents).filter(([id]) =>
    getConfig().agents.includes(id) &&
    id.toLowerCase().includes(searchQuery.toLowerCase())
  );

  const getRoleLabel = (role: string): string => {
    const roleLabels: Record<string, string> = {
      thesis: 'Thesis',
      antithesis: 'Antithesis',
      mediation: 'Mediator',
      moderator: 'Moderator',
      synthesis: 'Synthesis',
      gate: 'Gate',
      system: 'System',
      mediator: 'Mediator',
      question: 'Question',
    };
    return roleLabels[role] || role;
  };

  const handleWeightChange = async (agentId: string, value: number) => {
    setWeightUpdating(agentId);
    updateAgent(agentId, 'qualityWeight', value);
    setWeightUpdating(null);
  };

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-300 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(15, 12, 8, 0.45)', backdropFilter: 'blur(6px)' }}
      onClick={(e) => {
        if (e.target === e.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        className="w-full max-w-lg max-h-[90vh] overflow-hidden animate-fade-in-up rounded-xl pr-glass-surface"
        style={{
          backgroundColor: 'var(--pr-surface)',
          border: '1px solid var(--pr-border)',
          boxShadow: '0 24px 60px rgba(0,0,0,0.25)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        {/* Header */}
        <div
          className="px-5 py-4 border-b"
          style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}
        >
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-xl font-semibold" style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)' }}>
                Agent Configuration
              </h2>
              <p className="text-xs font-mono mt-0.5" style={{ color: 'var(--pr-text-muted)' }}>
                {module} module • {Object.keys(agents).length} agents
              </p>
            </div>
            <button
              onClick={onClose}
              className="w-8 h-8 rounded flex items-center justify-center cursor-pointer text-lg font-mono"
              style={{ backgroundColor: 'var(--pr-surface)', border: '1px solid var(--pr-border)', color: 'var(--pr-text-dim)' }}
            >
              ×
            </button>
          </div>
        </div>

        {/* Search */}
        <div className="px-5 py-3 border-b" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)' }}>
          <input
            type="text"
            placeholder="Search agents..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full px-3 py-2 rounded border text-[13px] font-mono transition-colors"
            style={{
              backgroundColor: 'var(--pr-surface-raised)',
              borderColor: 'var(--pr-border)',
              color: 'var(--pr-text)',
            }}
          />
        </div>

        {/* Agent List */}
        <div className="px-5 py-3 max-h-[55vh] overflow-y-auto" style={{ backgroundColor: 'var(--pr-bg)' }}>
          <div className="grid gap-2">
            {filteredAgents.map(([agentId, config]) => {
              const role = getAgentRole(agentId, module);
              const color = getAgentColor(agentId, module);
              const icon = getAgentIcon(agentId);
              const isUpdating = weightUpdating === agentId;

              return (
                <div
                  key={agentId}
                  className="p-3 rounded-lg border transition-all"
                  style={{
                    backgroundColor: config.enabled ? 'var(--pr-surface)' : 'var(--pr-surface-raised)',
                    borderColor: config.enabled ? `${color}50` : 'var(--pr-border)',
                    opacity: config.enabled ? 1 : 0.6,
                  }}
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center gap-2.5">
                      <div
                        className="w-8 h-8 rounded flex items-center justify-center text-[11px] font-mono font-bold"
                        style={{
                          backgroundColor: `${color}18`,
                          border: `2px solid ${color}60`,
                          color: color,
                        }}
                      >
                        {icon}
                      </div>
                      <div>
                        <h3 className="text-[13px] font-mono font-bold uppercase" style={{ color: 'var(--pr-text)' }}>
                          {agentId}
                        </h3>
                        <p className="text-[10px] font-mono uppercase tracking-wider" style={{ color: 'var(--pr-text-muted)' }}>
                          {getRoleLabel(role)}
                        </p>
                      </div>
                    </div>
                    <div className="flex items-center gap-2.5 flex-0">
                      <button
                        onClick={() => toggleAgent(agentId)}
                        className="w-11 h-6 rounded-full transition-colors cursor-pointer flex-0 relative overflow-hidden"
                        style={{
                          backgroundColor: config.enabled ? color : 'var(--pr-surface-raised)',
                          border: `1px solid ${config.enabled ? color : 'var(--pr-border)'}`,
                        }}
                      >
                        <div
                          className="w-4 h-4 rounded-full transition-all shadow-sm"
                          style={{
                            backgroundColor: '#ffffff',
                            position: 'absolute',
                            top: '2px',
                            left: config.enabled ? '22px' : '2px',
                          }}
                        />
                      </button>
                      <input
                        type="range"
                        min="0.5"
                        max="2.0"
                        step="0.1"
                        value={config.qualityWeight}
                        onChange={(e) => handleWeightChange(agentId, parseFloat(e.target.value))}
                        disabled={isUpdating}
                        className="w-16"
                        style={{ accentColor: color }}
                      />
                      <span className="w-8 text-right text-[11px] font-mono" style={{ color: 'var(--pr-text)' }}>
                        {isUpdating ? '...' : config.qualityWeight.toFixed(1)}
                      </span>
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Footer */}
        <div
          className="px-5 py-3.5 border-t"
          style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}
        >
          <div className="flex items-center justify-between">
            <button
              onClick={resetAgents}
              className="px-3 py-1.5 rounded border text-[12px] font-mono uppercase cursor-pointer transition-colors"
              style={{
                borderColor: 'var(--pr-role-question)',
                color: 'var(--pr-role-question)',
                backgroundColor: 'transparent',
              }}
            >
              Reset to Defaults
            </button>
            <button
              onClick={onClose}
              className="px-5 py-1.5 rounded text-[12px] font-mono uppercase font-semibold cursor-pointer transition-all"
              style={{
                backgroundColor: 'var(--pr-role-proposer)',
                color: 'var(--pr-bg)',
                border: 'none',
              }}
            >
              Done
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
