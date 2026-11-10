import { useState, useEffect, useCallback } from 'react';
import type { ModuleType } from '../App';

export interface SessionTemplate {
  key: string;
  name: string;
  description: string;
  module: ModuleType;
  rootQuestion: string;
  prePrompt?: string;
  agents?: string[];
  qualityGateSettings?: Record<string, unknown>;
  ascentDepth?: number;
  moderatorStrategy?: string;
}

const BUILT_IN_TEMPLATES: SessionTemplate[] = [
  {
    key: 'ethical-analysis',
    name: 'Ethical Analysis',
    description: 'Pre-configured for philosophy module ethics debates',
    module: 'philosophy',
    rootQuestion: 'What is the right course of action in this ethical dilemma?',
    prePrompt: 'Consider this from the perspectives of moral philosophy, virtue ethics, and dialectical reasoning.',
    agents: ['proposer', 'skeptic', 'mediator'],
    qualityGateSettings: { maxTurns: 16, similarityThreshold: 0.85 },
    ascentDepth: 5,
    moderatorStrategy: 'round-robin',
  },
  {
    key: 'strategic-planning',
    name: 'Strategic Planning',
    description: 'Pre-configured for philosophy module strategic analysis',
    module: 'philosophy',
    rootQuestion: 'What is the optimal strategy for achieving our long-term objectives?',
    prePrompt: 'Analyze from both principled-abstraction and practical-wisdom perspectives.',
    agents: ['proposer', 'skeptic', 'mediator'],
    qualityGateSettings: { maxTurns: 12, similarityThreshold: 0.8 },
    ascentDepth: 4,
    moderatorStrategy: 'round-robin',
  },
  {
    key: 'conceptual-analysis',
    name: 'Conceptual Analysis',
    description: 'Pre-configured for philosophy module definition debates',
    module: 'philosophy',
    rootQuestion: 'What is the precise definition of the concept at issue?',
    prePrompt: 'Propose a definition, test it against concrete counterexamples, and refine toward the strongest formulation.',
    agents: ['proposer', 'skeptic', 'mediator'],
    qualityGateSettings: { maxTurns: 16, similarityThreshold: 0.8 },
    ascentDepth: 6,
    moderatorStrategy: 'round-robin',
  },
  {
    key: 'ai-safety',
    name: 'AI Safety Review',
    description: 'Pre-configured for AI safety alignment review',
    module: 'philosophy',
    rootQuestion: 'What are the potential failure modes and alignment risks of this AI system?',
    prePrompt: 'Consider orthogonal alignment, instrumental convergence, and corrigibility.',
    agents: ['proposer', 'skeptic', 'mediator'],
    qualityGateSettings: { maxTurns: 16, similarityThreshold: 0.75 },
    ascentDepth: 6,
    moderatorStrategy: 'round-robin',
  },
];

const CUSTOM_TEMPLATES_KEY = 'seldon_custom_templates';

function loadCustomTemplates(): SessionTemplate[] {
  try {
    const raw = localStorage.getItem(CUSTOM_TEMPLATES_KEY);
    return raw ? (JSON.parse(raw) as SessionTemplate[]) : [];
  } catch {
    return [];
  }
}

function saveCustomTemplates(templates: SessionTemplate[]): void {
  localStorage.setItem(CUSTOM_TEMPLATES_KEY, JSON.stringify(templates));
}

interface Props {
  templates?: SessionTemplate[];
  onSelect: (template: SessionTemplate) => void;
  onClose: () => void;
}

export default function TemplateSelector({
  templates,
  onSelect,
  onClose,
}: Props) {
  const [customTemplates, setCustomTemplates] = useState<SessionTemplate[]>([]);
  const [showCustomForm, setShowCustomForm] = useState(false);
  const [newTemplate, setNewTemplate] = useState<Partial<SessionTemplate>>({
    key: '',
    name: '',
    description: '',
    module: 'philosophy',
    rootQuestion: '',
    prePrompt: '',
  });

  useEffect(() => {
    setCustomTemplates(loadCustomTemplates());
  }, []);

  const displayBuiltIn = (templates ?? BUILT_IN_TEMPLATES).filter(t => !customTemplates.some(ct => ct.key === t.key));
  const handleSaveCustom = useCallback(() => {
    if (!newTemplate.key || !newTemplate.name || !newTemplate.rootQuestion) return;
    const template: SessionTemplate = {
      key: newTemplate.key!,
      name: newTemplate.name!,
      description: newTemplate.description ?? '',
      module: newTemplate.module ?? 'philosophy',
      rootQuestion: newTemplate.rootQuestion!,
      prePrompt: newTemplate.prePrompt,
      agents: newTemplate.agents,
      qualityGateSettings: newTemplate.qualityGateSettings,
      ascentDepth: newTemplate.ascentDepth,
      moderatorStrategy: newTemplate.moderatorStrategy,
    };
    const updated = [...customTemplates, template];
    setCustomTemplates(updated);
    saveCustomTemplates(updated);
    setShowCustomForm(false);
    setNewTemplate({
      key: '',
      name: '',
      description: '',
      module: 'philosophy',
      rootQuestion: '',
      prePrompt: '',
    });
  }, [newTemplate, customTemplates]);

  const handleDeleteCustom = useCallback(
    (key: string) => {
      const updated = customTemplates.filter((t) => t.key !== key);
      setCustomTemplates(updated);
      saveCustomTemplates(updated);
    },
    [customTemplates],
  );

  return (
    <div
      className="fixed inset-0 z-400 flex items-center justify-center p-4"
      style={{ backgroundColor: 'rgba(15, 12, 8, 0.45)', backdropFilter: 'blur(6px)' }}
      onClick={(e) => {
        if (e.target === e.currentTarget) {
          onClose();
        }
      }}
    >
      <div
        className="w-full max-w-2xl max-h-[85vh] overflow-hidden animate-fade-in-up rounded-xl pr-glass-surface flex flex-col"
        style={{
          backgroundColor: 'var(--pr-surface)',
          border: '1px solid var(--pr-border)',
          boxShadow: '0 24px 60px rgba(0,0,0,0.25)',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div
          className="px-6 py-4 border-b flex-0"
          style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}
        >
          <div className="flex items-center justify-between">
            <div>
              <h2 className="text-xl font-semibold" style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)' }}>
                Session Templates
              </h2>
              <p className="text-xs font-mono mt-0.5" style={{ color: 'var(--pr-text-muted)' }}>
                Choose a pre-configured session template
              </p>
            </div>
            <div className="flex items-center gap-2">
              <button
                onClick={() => setShowCustomForm(!showCustomForm)}
                className="px-3 py-1.5 rounded text-[12px] font-mono font-semibold cursor-pointer transition-all"
                style={{
                  backgroundColor: 'var(--pr-role-judge)',
                  color: '#ffffff',
                  border: 'none',
                }}
              >
                {showCustomForm ? 'Close Form' : '+ Custom'}
              </button>
              <button
                onClick={onClose}
                className="w-8 h-8 rounded flex items-center justify-center cursor-pointer text-lg font-mono"
                style={{ backgroundColor: 'var(--pr-surface)', border: '1px solid var(--pr-border)', color: 'var(--pr-text-dim)' }}
              >
                ×
              </button>
            </div>
          </div>
        </div>

        {showCustomForm && (
          <div
            className="px-6 py-4 border-b flex-0"
            style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface)' }}
          >
            <h3 className="text-base font-semibold mb-3" style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)' }}>
              Create Custom Template
            </h3>
            <div className="grid grid-cols-2 gap-3 mb-3">
              <input
                type="text"
                placeholder="Template key (e.g. my-template)"
                value={newTemplate.key ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, key: e.target.value }))}
                className="px-3 py-2 rounded border text-[13px] font-mono col-span-2"
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              />
              <input
                type="text"
                placeholder="Template name"
                value={newTemplate.name ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, name: e.target.value }))}
                className="px-3 py-2 rounded border text-[13px] font-mono"
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              />
              <select
                value={newTemplate.module ?? 'philosophy'}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, module: e.target.value as ModuleType }))}
                className="px-3 py-2 rounded border text-[13px] font-mono"
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              >
                <option value="philosophy">Philosophical Reasoning</option>
                <option value="research">Research Orchestration</option>
                <option value="custom">Custom</option>
              </select>
              <input
                type="number"
                placeholder="Ascent depth"
                value={newTemplate.ascentDepth ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, ascentDepth: parseInt(e.target.value) || 0 }))}
                className="px-3 py-2 rounded border text-[13px] font-mono"
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              />
              <input
                type="text"
                placeholder="Moderator strategy (e.g. mediator, round-robin)"
                value={newTemplate.moderatorStrategy ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, moderatorStrategy: e.target.value }))}
                className="px-3 py-2 rounded border text-[13px] font-mono col-span-2"
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              />
              <input
                type="text"
                placeholder="Root question"
                value={newTemplate.rootQuestion ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, rootQuestion: e.target.value }))}
                className="px-3 py-2 rounded border text-[13px] font-mono col-span-2"
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)' }}
              />
              <textarea
                placeholder="Description"
                value={newTemplate.description ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, description: e.target.value }))}
                className="px-3 py-2 rounded border text-[13px] font-mono col-span-2"
                rows={2}
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)', resize: 'vertical' }}
              />
              <textarea
                placeholder="Pre-prompt (optional)"
                value={newTemplate.prePrompt ?? ''}
                onChange={(e) => setNewTemplate((prev) => ({ ...prev, prePrompt: e.target.value }))}
                className="px-3 py-2 rounded border text-[13px] font-mono col-span-2"
                rows={2}
                style={{ backgroundColor: 'var(--pr-surface-raised)', borderColor: 'var(--pr-border)', color: 'var(--pr-text)', resize: 'vertical' }}
              />
            </div>
            <button
              onClick={handleSaveCustom}
              className="px-4 py-2 rounded text-[13px] font-mono font-semibold cursor-pointer transition-all"
              style={{
                backgroundColor: 'var(--pr-role-proposer)',
                color: 'var(--pr-bg)',
                border: 'none',
              }}
            >
              Save Template
            </button>
          </div>
        )}

        <div className="p-6 overflow-y-auto max-h-[60vh] flex-1" style={{ backgroundColor: 'var(--pr-bg)' }}>
          {customTemplates.length > 0 && (
            <div className="mb-4">
              <h3 className="text-xs font-mono uppercase mb-2 font-semibold" style={{ color: 'var(--pr-text-dim)' }}>
                Custom Templates
              </h3>
              <div className="grid gap-3 mb-4">
                {customTemplates.map((t) => (
                  <div
                    key={t.key}
                    className="flex items-start justify-between p-3.5 rounded-lg border transition-all"
                    style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}
                  >
                    <button
                      onClick={() => onSelect(t)}
                      className="flex-1 text-left cursor-pointer"
                    >
                      <div className="flex items-center gap-2">
                        <span className="text-[13px] font-mono font-bold uppercase" style={{ color: 'var(--pr-text)' }}>
                          {t.name}
                        </span>
                        <span className="text-[10px] font-mono uppercase px-1.5 py-0.5 rounded" style={{ backgroundColor: 'rgba(120,89,26,0.15)', color: 'var(--pr-role-proposer)' }}>
                          custom
                        </span>
                      </div>
                      <p className="text-[11px] mt-1 italic" style={{ color: 'var(--pr-text-dim)' }}>
                        "{t.rootQuestion}"
                      </p>
                    </button>
                    <button
                      onClick={() => handleDeleteCustom(t.key)}
                      className="text-[14px] font-mono cursor-pointer opacity-50 hover:opacity-100 ml-2"
                      style={{ color: 'var(--pr-role-question)' }}
                    >
                      ×
                    </button>
                  </div>
                ))}
              </div>
            </div>
          )}

          <div className="mb-4">
            <h3 className="text-xs font-mono uppercase mb-2 font-semibold" style={{ color: 'var(--pr-text-dim)' }}>
              Built-in Templates
            </h3>
            <div className="grid gap-3">
              {displayBuiltIn.map((t) => (
                <button
                  key={t.key}
                  onClick={() => onSelect(t)}
                  className="p-4 rounded-lg border text-left cursor-pointer transition-all hover:border-(--pr-role-proposer)"
                  style={{ backgroundColor: 'var(--pr-surface)', borderColor: 'var(--pr-border)' }}
                >
                  <div className="flex items-start justify-between mb-2">
                    <h3 className="text-[14px] font-mono font-bold uppercase" style={{ color: 'var(--pr-text)' }}>
                      {t.name}
                    </h3>
                    <span
                      className="text-[10px] font-mono uppercase px-2 py-0.5 rounded"
                      style={{
                        backgroundColor: 'rgba(120,89,26,0.12)',
                        color: 'var(--pr-role-proposer)',
                        border: '1px solid var(--pr-border)',
                      }}
                    >
                      {t.module}
                    </span>
                  </div>
                  <p className="text-[12px]" style={{ color: 'var(--pr-text-muted)' }}>
                    {t.description}
                  </p>
                  <p className="text-[11px] mt-2 italic" style={{ color: 'var(--pr-text-dim)' }}>
                    "{t.rootQuestion}"
                  </p>
                  {t.agents && (
                    <div className="flex flex-wrap gap-1 mt-2.5">
                      {t.agents.map((agent) => (
                        <span
                          key={agent}
                          className="text-[9px] font-mono px-1.5 py-0.5 rounded"
                          style={{ backgroundColor: 'var(--pr-surface-raised)', color: 'var(--pr-text-muted)' }}
                        >
                          {agent}
                        </span>
                      ))}
                    </div>
                  )}
                </button>
              ))}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}