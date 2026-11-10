import React, { useState } from 'react';
import type {
  SettingsDto,
  AgentConfig,
  QualityGatesConfig,
  DialecticsConfig,
  ProviderDefault,
} from '../lib/api/settingsApi';
import UserNameInput from './UserNameInput';
import { SectionCard, Row, TextInput, ToggleGroup, SliderField, Toggle, ProviderSelect } from './SettingsControls';
import { ServersSection } from './SettingsServersSection';
import { PHILOSOPHY_LAYER_GROUPS } from './settingsMetadata';
import type { SectionId } from './settingsMetadata';

interface SectionContentProps {
  id: SectionId;
  dto: SettingsDto;
  agents: Record<string, AgentConfig>;
  agentList: string[];
  framework: string;
  qg: QualityGatesConfig;
  dial: DialecticsConfig;
  providerDefaults: Record<string, ProviderDefault>;
  connectionOk: boolean | null;
  onUpdate: <K extends keyof SettingsDto>(key: K, value: SettingsDto[K]) => void;
  onUpdateAgent: (agentId: string, key: keyof AgentConfig, value: boolean | number) => void;
  onUpdateLayer: (layerAgents: string[], key: keyof AgentConfig, value: boolean | number) => void;
  onUpdateQg: <K extends keyof QualityGatesConfig>(key: K, value: QualityGatesConfig[K]) => void;
  onUpdateDial: <K extends keyof DialecticsConfig>(key: K, value: DialecticsConfig[K]) => void;
  onTestConnection: () => void;
  onExportAudit: () => void;
  onExportTranscript: () => void;
  onClearData: () => void;
  onDeleteAllSessions: () => void | Promise<void>;
  onClearMerkleAndKeys: () => void | Promise<void>;
  deleteSessionsState: 'idle' | 'loading' | 'success' | 'error';
  clearDataState: 'idle' | 'loading' | 'success' | 'error';
  disabled: boolean;
  badgeColour: string;
}

export function SectionContent(p: SectionContentProps) {
  const field = (
    label: string,
    desc: string,
    children: React.ReactNode,
  ) => (
    <Row label={label} desc={desc}>{children}</Row>
  );

  switch (p.id) {
    case 'servers':
      return <ServersSection disabled={p.disabled} />;
    case 'connection':
      return (
        <SectionCard title="Connection" subtitle="LLM provider, model, and authentication">
          <form onSubmit={(e) => e.preventDefault()} className="space-y-4">
            {field('User Name', 'Your name for the agents to address you by', (
              <UserNameInput value={p.dto.userName} onUpdate={(key, val) => p.onUpdate(key as keyof SettingsDto, val)} disabled={p.disabled} />
            ))}
            {field('API Endpoint', 'Base URL for the backend', (
              <TextInput value={p.dto.apiEndpoint} onChange={(v) => p.onUpdate('apiEndpoint', v)} disabled={p.disabled} />
            ))}
            {field('Provider', 'LLM inference provider (e.g. OpenCodeZen, Ollama, OpenAI, Groq)', (
              <ProviderSelect
                value={p.dto.provider}
                providers={Object.keys(p.providerDefaults)}
                onChange={(v) => {
                  const defaults = p.providerDefaults[v];
                  if (!defaults || !defaults.model || !defaults.endpoint) {
                    throw new Error(
                      `Provider '${v}' is not fully configured in appsettings. ` +
                      'Set its Model and Endpoint before selecting it.',
                    );
                  }
                  p.onUpdate('provider', v);
                  p.onUpdate('model', defaults.model);
                  p.onUpdate('apiEndpoint', defaults.endpoint);
                }}
                disabled={p.disabled}
              />
            ))}
            {field('Model', 'Model identifier string', (
              <TextInput value={p.dto.model} onChange={(v) => p.onUpdate('model', v)} disabled={p.disabled} />
            ))}
            {field('API Key', 'Provider API key', (
              <TextInput type="password" value={p.dto.apiKey} onChange={(v) => p.onUpdate('apiKey', v)} placeholder={p.dto.hasApiKey ? 'stored (hidden) - type to replace' : 'not set'} disabled={p.disabled} />
            ))}
            {field('Auth Token', 'Session bearer token', (
              <TextInput type="password" value={p.dto.authToken} onChange={(v) => p.onUpdate('authToken', v)} placeholder={p.dto.hasAuthToken ? 'stored (hidden) - type to replace' : 'not set'} disabled={p.disabled} />
            ))}
            {field('Rate Limit', 'Requests per minute', (
              <SliderField min={10} max={500} step={10} value={p.dto.rateLimit} onChange={(v) => p.onUpdate('rateLimit', v)} suffix="/min" disabled={p.disabled} accentColour={p.badgeColour} />
            ))}
            <p style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: 'var(--pr-role-proposer)' }}>
              Provider, model, endpoint and API key are deployment settings. They are saved
              immediately and applied when the API restarts. Engine options and quality gates
              apply live.
            </p>
            <div className="pt-2">
              <button
                type="button"
                onClick={p.onTestConnection}
                disabled={p.disabled}
                className="flex items-center gap-2 px-3 py-1.5 rounded border transition-all cursor-pointer hover:bg-[#161b26]"
                style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 12, color: '#f1f5f9', borderColor: 'rgba(255,255,255,0.08)', background: 'transparent' }}
              >
                Test connection
                {p.connectionOk === true && <span style={{ color: '#10b981' }}>● online</span>}
                {p.connectionOk === false && <span style={{ color: '#b78383' }}>● offline</span>}
                {p.connectionOk === null && <span style={{ color: '#5a6a82' }}>●</span>}
              </button>
            </div>
          </form>
        </SectionCard>
      );

    case 'engine':
      return (
        <SectionCard title="Reasoning Engine" subtitle="Core dialectical parameters">
          {field('Layout Mode', 'Graph visualisation topology', (
            <ToggleGroup
              options={[{ value: 'helical', label: 'Helical' }, { value: 'dialectical', label: 'Dialectical' }]}
              value={p.dto.layoutMode}
              onChange={(v) => p.onUpdate('layoutMode', v as 'helical' | 'dialectical')}
              accentColour={p.badgeColour}
              disabled={p.disabled}
            />
          ))}
          {field('Maximum Depth', 'Dialectical recursion depth limit', (
            <SliderField min={1} max={20} value={p.dto.maxDepth} onChange={(v) => p.onUpdate('maxDepth', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Quality Threshold', 'Minimum coherence score for gate approval', (
            <SliderField min={0} max={1} step={0.05} value={p.dto.qualityThreshold} onChange={(v) => p.onUpdate('qualityThreshold', v)} format={(v) => v.toFixed(2)} disabled={p.disabled} accentColour="#10b981" />
          ))}
          {field('SSE Streaming', 'Real-time token streaming from the backend', (
            <Toggle checked={p.dto.enableStreamingSSE} onChange={(v) => p.onUpdate('enableStreamingSSE', v)} disabled={p.disabled} />
          ))}
          {field('Persist Reasoning Trace', 'Store the model\'s thinking trace per turn (~8x token cost, ~4-6x latency)', (
            <Toggle checked={p.dto.persistReasoningTrace} onChange={(v) => p.onUpdate('persistReasoningTrace', v)} disabled={p.disabled} />
          ))}
          {field('Static Questions', 'Show question fan instead of free-text input', (
            <Toggle checked={p.dto.useStaticQuestions} onChange={(v) => p.onUpdate('useStaticQuestions', v)} disabled={p.disabled} />
          ))}
        </SectionCard>
      );

    case 'quality-gates': {
      const qg = p.qg;
      return (
        <SectionCard title="Quality Gates" subtitle="Thresholds controlling when dialectical turns are forced to end or escalate">
          {field('Enabled', 'Master switch for all quality gate checks', (
            <Toggle checked={qg.enabled} onChange={(v) => p.onUpdateQg('enabled', v)} disabled={p.disabled} />
          ))}
          {field('Max Turns', 'Maximum turns before forced termination', (
            <SliderField min={4} max={40} value={qg.maxTurns} onChange={(v) => p.onUpdateQg('maxTurns', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Similarity Threshold', 'Cosine similarity above which content is considered repetitive', (
            <SliderField min={0.4} max={1} step={0.05} value={qg.similarityThreshold} onChange={(v) => p.onUpdateQg('similarityThreshold', v)} format={(v) => v.toFixed(2)} disabled={p.disabled} accentColour="#10b981" />
          ))}
          {field('Quality Exit Threshold', 'Score below which the debate is force-exited (0-100)', (
            <SliderField min={0} max={100} value={qg.qualityScoreExitThreshold} onChange={(v) => p.onUpdateQg('qualityScoreExitThreshold', v)} disabled={p.disabled} accentColour="#b78383" />
          ))}
          {field('Agreement Stagnation Turns', 'Consecutive agreement turns before stagnation exit', (
            <SliderField min={1} max={12} value={qg.agreementStagnationTurns} onChange={(v) => p.onUpdateQg('agreementStagnationTurns', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Low Quality Consecutive Turns', 'Consecutive low-quality turns before forced exit', (
            <SliderField min={1} max={10} value={qg.lowQualityConsecutiveTurns} onChange={(v) => p.onUpdateQg('lowQualityConsecutiveTurns', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Min Turns For Similarity Check', 'Skip similarity check on first N turns', (
            <SliderField min={0} max={8} value={qg.minTurnsForSimilarityCheck} onChange={(v) => p.onUpdateQg('minTurnsForSimilarityCheck', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Consecutive Farewells', 'Farewell responses that trigger forced exit', (
            <SliderField min={1} max={20} value={qg.consecutiveFarewellsThreshold} onChange={(v) => p.onUpdateQg('consecutiveFarewellsThreshold', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Verbose Word Count', 'Word count above which a response is considered padded', (
            <SliderField min={200} max={2000} step={50} value={qg.verboseWordCount} onChange={(v) => p.onUpdateQg('verboseWordCount', v)} suffix=" w" disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Circular Restatement Threshold', 'Bigram similarity to 2-turns-ago that signals a loop', (
            <SliderField min={0.3} max={0.9} step={0.05} value={qg.circularRestatementThreshold} onChange={(v) => p.onUpdateQg('circularRestatementThreshold', v)} format={(v) => v.toFixed(2)} disabled={p.disabled} accentColour="#8a6a2e" />
          ))}
          {field('Premise Fork Distance', 'Cosine distance signalling a deep premise-level fork', (
            <SliderField min={0.4} max={1} step={0.05} value={qg.premiseForkDistanceThreshold} onChange={(v) => p.onUpdateQg('premiseForkDistanceThreshold', v)} format={(v) => v.toFixed(2)} disabled={p.disabled} accentColour="#8a6a2e" />
          ))}
          {field('Premise Fork Sustained Turns', 'Turns the distance must persist before declaring a fork', (
            <SliderField min={1} max={8} value={qg.premiseForkSustainedTurns} onChange={(v) => p.onUpdateQg('premiseForkSustainedTurns', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
        </SectionCard>
      );
    }

    case 'dialectics': {
      return <DialecticsSection dial={p.dial} onUpdateDial={p.onUpdateDial} disabled={p.disabled} badgeColour={p.badgeColour} />;
    }

    case 'agents': {
      const layerGroups = PHILOSOPHY_LAYER_GROUPS;

      const activeLayerCount = layerGroups.filter((g) =>
        g.agents.some((id) => p.agents[id]?.enabled ?? true)
      ).length;

      return (
        <SectionCard
          title="Layers & Agent Pools"
          subtitle={p.framework === 'research'
            ? 'The agent pool participants carry forward from the philosophy module into this run'
            : 'Enable or disable reasoning framework layers and adjust their quality weighting'}
        >
          {activeLayerCount === 0 && (
            <div className="flex items-center gap-2 p-3 rounded-lg bg-[#3a1d1d] border border-[#b05a3a] text-[#f09a80] text-[12px] font-mono mb-2">
              <span className="font-bold">⚠</span>
              <span>At least 1 layer must remain active for the reasoning system to function.</span>
            </div>
          )}

          <div className="grid grid-cols-1 gap-3">
            {layerGroups.map((layer) => {
              const isEnabled = layer.agents.some((id) => p.agents[id]?.enabled ?? true);
              const avgWeight =
                layer.agents.reduce((sum, id) => sum + (p.agents[id]?.qualityWeight ?? 1.0), 0) /
                layer.agents.length;

              return (
                <div
                  key={layer.id}
                  className="flex flex-col md:flex-row md:items-center justify-between p-4 rounded-xl border transition-all gap-3"
                  style={{
                    borderColor: isEnabled ? `${layer.colour}50` : '#161b26',
                    background: isEnabled ? `${layer.colour}10` : '#080a0f',
                    boxShadow: isEnabled ? `0 0 16px ${layer.colour}15` : 'none',
                  }}
                >
                  <div className="flex items-start md:items-center gap-3.5 min-w-0">
                    <Toggle
                      checked={isEnabled}
                      onChange={(v) => p.onUpdateLayer(layer.agents, 'enabled', v)}
                      disabled={p.disabled}
                      colour={layer.colour}
                    />
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span
                          className="text-[13px] font-mono font-bold"
                          style={{ color: isEnabled ? '#f1f5f9' : '#5a6a82' }}
                        >
                          {layer.name}
                        </span>
                        <span
                          className="w-2.5 h-2.5 rounded-full flex-0"
                          style={{
                            background: isEnabled ? layer.colour : 'rgba(255,255,255,0.08)',
                            boxShadow: isEnabled ? `0 0 8px ${layer.colour}` : 'none',
                          }}
                        />
                      </div>
                      <p
                        className="text-[11px] font-mono mt-0.5"
                        style={{ color: isEnabled ? '#f1f5f9' : '#5a6a82' }}
                      >
                        {layer.subtitle}
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center gap-3 self-end md:self-auto flex-0 pt-2 md:pt-0 border-t md:border-t-0 border-[#161b26]">
                    <span style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: isEnabled ? '#8b9bb4' : '#5a6a82' }}>
                      layer weight
                    </span>
                    <input
                      type="range"
                      min={0.5}
                      max={2}
                      step={0.1}
                      value={avgWeight}
                      onChange={(e) =>
                        p.onUpdateLayer(layer.agents, 'qualityWeight', parseFloat(e.target.value))
                      }
                      disabled={p.disabled || !isEnabled}
                      className="w-24"
                      style={{ accentColor: layer.colour }}
                    />
                    <span
                      style={{
                        fontFamily: 'var(--pr-font-mono)',
                        fontSize: 12,
                        color: isEnabled ? '#f1f5f9' : '#5a6a82',
                        minWidth: 28,
                        textAlign: 'right',
                        fontWeight: 600,
                      }}
                    >
                      {avgWeight.toFixed(1)}
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        </SectionCard>
      );
    }

    case 'display':
      return (
        <SectionCard title="Display" subtitle="Visual preferences and UI behaviour">
          {field('Font Size', 'Base text size throughout the app', (
            <ToggleGroup
              options={[{ value: 'small', label: 'S' }, { value: 'medium', label: 'M' }, { value: 'large', label: 'L' }]}
              value={p.dto.fontSize}
              onChange={(v) => p.onUpdate('fontSize', v as 'small' | 'medium' | 'large')}
              accentColour={p.badgeColour}
              disabled={p.disabled}
            />
          ))}
          {field('Animations', 'Fade-in and transition effects', (
            <Toggle checked={p.dto.enableAnimations} onChange={(v) => p.onUpdate('enableAnimations', v)} disabled={p.disabled} />
          ))}
        </SectionCard>
      );

    case 'notifications':
      return (
        <SectionCard title="Notifications" subtitle="Alert preferences and delivery channels">
          {field('On Synthesis', 'Notify when synthesis is reached', (
            <Toggle checked={p.dto.notifyOnSynthesis} onChange={(v) => p.onUpdate('notifyOnSynthesis', v)} disabled={p.disabled} />
          ))}
          {field('On Gate Rejection', 'Notify when a quality gate rejects a path', (
            <Toggle checked={p.dto.notifyOnGateRejection} onChange={(v) => p.onUpdate('notifyOnGateRejection', v)} disabled={p.disabled} />
          ))}
          {field('On Session Stall', 'Notify when session progress stalls', (
            <Toggle checked={p.dto.notifyOnSessionStall} onChange={(v) => p.onUpdate('notifyOnSessionStall', v)} disabled={p.disabled} />
          ))}
          {field('On Max Depth', 'Notify when max recursion depth is reached', (
            <Toggle checked={p.dto.notifyOnMaxDepth} onChange={(v) => p.onUpdate('notifyOnMaxDepth', v)} disabled={p.disabled} />
          ))}
          {field('Email', 'Notification recipient address', (
            <TextInput type="email" value={p.dto.notificationEmail} onChange={(v) => p.onUpdate('notificationEmail', v)} placeholder="ops@example.com" disabled={p.disabled} />
          ))}
          {field('Webhook', 'Event-driven notification endpoint', (
            <TextInput value={p.dto.webhookUrl} onChange={(v) => p.onUpdate('webhookUrl', v)} placeholder="https://hooks.example.com/seldon" disabled={p.disabled} />
          ))}
        </SectionCard>
      );

    case 'export':
      return (
        <SectionCard title="Export" subtitle="Transcript and graph export preferences">
          {field('Default Format', 'Output format for exports', (
            <ToggleGroup
              options={[{ value: 'markdown', label: 'MD' }, { value: 'pdf', label: 'PDF' }, { value: 'json', label: 'JSON' }]}
              value={p.dto.exportFormat}
              onChange={(v) => p.onUpdate('exportFormat', v as 'markdown' | 'pdf' | 'json')}
              accentColour={p.badgeColour}
              disabled={p.disabled}
            />
          ))}
          {field('Include Merkle Proofs', 'Embed cryptographic hash chains in exports', (
            <Toggle checked={p.dto.includeMerkleProofs} onChange={(v) => p.onUpdate('includeMerkleProofs', v)} disabled={p.disabled} />
          ))}
          {field('Include Metrics', 'Embed quality scores and gate decisions', (
            <Toggle checked={p.dto.includeMetrics} onChange={(v) => p.onUpdate('includeMetrics', v)} disabled={p.disabled} />
          ))}
          {field('Compliance Report', 'Generate B2B compliance report on export', (
            <Toggle checked={p.dto.complianceReport} onChange={(v) => p.onUpdate('complianceReport', v)} disabled={p.disabled} />
          ))}
          <div className="pt-2">
            <button
              onClick={p.onExportTranscript}
              className="px-3 py-1.5 rounded border text-[12px] transition-all cursor-pointer hover:bg-[#161b26]"
              style={{ fontFamily: 'var(--pr-font-mono)', color: '#f1f5f9', borderColor: 'rgba(255,255,255,0.08)', background: 'transparent' }}
            >
              export session now
            </button>
          </div>
        </SectionCard>
      );

    case 'sessions':
      return (
        <SectionCard title="Session Defaults" subtitle="Default parameters for new reasoning sessions">
          {field('Default Max Depth', 'Recursion depth for new sessions', (
            <SliderField min={1} max={20} value={p.dto.sessionMaxDepth} onChange={(v) => p.onUpdate('sessionMaxDepth', v)} disabled={p.disabled} accentColour={p.badgeColour} />
          ))}
          {field('Auto-archive After', 'Days before auto-archiving idle sessions', (
            <SliderField min={1} max={90} value={p.dto.autoArchiveDays} onChange={(v) => p.onUpdate('autoArchiveDays', v)} suffix="d" disabled={p.disabled} accentColour="#10b981" />
          ))}
          {field('Session Prefix', 'Custom prefix for session identifiers', (
            <TextInput value={p.dto.sessionPrefix} onChange={(v) => p.onUpdate('sessionPrefix', v)} placeholder="e.g. Q1-Strategy" disabled={p.disabled} />
          ))}
        </SectionCard>
      );

    case 'audit':
      return (
        <SectionCard title="Audit & Compliance" subtitle="Cryptographic proof and chain verification">
          <Row label="Merkle DAG Proof" desc="Export as verifiable Merkle proof">
            <button onClick={p.onExportAudit} className="px-3 py-1.5 rounded border text-[12px] transition-all cursor-pointer hover:bg-[#161b26]" style={{ fontFamily: 'var(--pr-font-mono)', color: '#f1f5f9', borderColor: 'rgba(255,255,255,0.08)', background: 'transparent' }}>
              export proof
            </button>
          </Row>
          <Row label="Verify Chain" desc="Recompute and validate Merkle DAG hashes">
            <button className="px-3 py-1.5 rounded border text-[12px] transition-all cursor-pointer hover:bg-[#161b26]" style={{ fontFamily: 'var(--pr-font-mono)', color: '#10b981', borderColor: '#10b981', background: 'transparent' }}>
              verify chain
            </button>
          </Row>
        </SectionCard>
      );

    case 'data':
      return (
        <SectionCard title="Data" subtitle="Local cache and server data controls">
          <Row label="Clear Session Data" desc="Remove cached turns, selections, and session IDs from this browser">
            <button onClick={p.onClearData} className="px-3 py-1.5 rounded border text-[12px] transition-all cursor-pointer hover:bg-[#3a2020] hover:border-[#8a4a4a]" style={{ fontFamily: 'var(--pr-font-mono)', color: '#b78383', borderColor: '#5a3030', background: 'transparent' }}>
              clear data
            </button>
          </Row>
          <Row label="Delete All Sessions" desc="Hard-delete all sessions from the database for your account">
            <button
              onClick={p.onDeleteAllSessions}
              disabled={p.deleteSessionsState === 'loading'}
              className="px-3 py-1.5 rounded border text-[12px] transition-all cursor-pointer hover:bg-[#3a2020] hover:border-[#8a4a4a]"
              style={{
                fontFamily: 'var(--pr-font-mono)',
                color: p.deleteSessionsState === 'success' ? '#10b981' : p.deleteSessionsState === 'error' ? '#b78383' : '#b78383',
                borderColor: p.deleteSessionsState === 'success' ? '#10b981' : p.deleteSessionsState === 'error' ? '#b78383' : '#5a3030',
                background: 'transparent',
                opacity: p.deleteSessionsState === 'loading' ? 0.5 : 1,
              }}
            >
              {p.deleteSessionsState === 'loading' ? 'deleting...' : p.deleteSessionsState === 'success' ? 'deleted ✓' : p.deleteSessionsState === 'error' ? 'error' : 'delete all sessions'}
            </button>
          </Row>
          <Row label="Clear Merkle & Keys" desc="Erase the Merkle DAG store and ML-DSA signing keys (destroys audit trails)">
            <button
              onClick={p.onClearMerkleAndKeys}
              disabled={p.clearDataState === 'loading'}
              className="px-3 py-1.5 rounded border text-[12px] transition-all cursor-pointer hover:bg-[#3a2020] hover:border-[#8a4a4a]"
              style={{
                fontFamily: 'var(--pr-font-mono)',
                color: p.clearDataState === 'success' ? '#10b981' : p.clearDataState === 'error' ? '#b78383' : '#b78383',
                borderColor: p.clearDataState === 'success' ? '#10b981' : p.clearDataState === 'error' ? '#b78383' : '#5a3030',
                background: 'transparent',
                opacity: p.clearDataState === 'loading' ? 0.5 : 1,
              }}
            >
              {p.clearDataState === 'loading' ? 'clearing...' : p.clearDataState === 'success' ? 'cleared ✓' : p.clearDataState === 'error' ? 'error' : 'clear merkle & keys'}
            </button>
          </Row>
        </SectionCard>
      );

    default:
      return null;
  }
}

//  Dialectics Section (own state for advanced toggle)

function DialecticsSection({
  dial, onUpdateDial, disabled, badgeColour,
}: {
  dial: DialecticsConfig;
  onUpdateDial: <K extends keyof DialecticsConfig>(key: K, value: DialecticsConfig[K]) => void;
  disabled: boolean;
  badgeColour: string;
}) {
  const [showAdvanced, setShowAdvanced] = useState(false);
  const field = (label: string, desc: string, children: React.ReactNode) => (
    <Row label={label} desc={desc}>{children}</Row>
  );


  return (
    <SectionCard title="Dialectics Engine" subtitle="Core reasoning parameters for the dialectical ascent">
      {/*  Basic  */}
      <div style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: '#5a6a82', paddingBottom: 4, borderBottom: '1px solid #161b26', marginBottom: 4 }}>
        BASIC
      </div>

      {field('Max Turns', 'Maximum turns per dialectical cycle', (
        <SliderField min={2} max={30} value={dial.maxTurns} onChange={(v) => onUpdateDial('maxTurns', v)} disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Max Ascent Levels', 'Maximum synthesis ladder levels', (
        <SliderField min={1} max={10} value={dial.maxAscentLevels} onChange={(v) => onUpdateDial('maxAscentLevels', v)} disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Default Intensity', 'Starting rhetorical intensity (0-10)', (
        <SliderField min={0} max={10} value={dial.defaultIntensity} onChange={(v) => onUpdateDial('defaultIntensity', v)} disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Max Interjections', 'Maximum moderator interjections per cycle', (
        <SliderField min={0} max={8} value={dial.maxInterjections} onChange={(v) => onUpdateDial('maxInterjections', v)} disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Ascent Pressure', 'Enable automatic push toward higher synthesis levels', (
        <Toggle checked={dial.ascentPressureEnabled} onChange={(v) => onUpdateDial('ascentPressureEnabled', v)} disabled={disabled} />
      ))}
      {field('Stability Threshold', 'Topic drift below this delta is considered stable', (
        <SliderField min={0.01} max={0.5} step={0.01} value={dial.stabilityThreshold} onChange={(v) => onUpdateDial('stabilityThreshold', v)} format={(v) => v.toFixed(2)} disabled={disabled} accentColour="#10b981" />
      ))}
      {field('Stability Window', 'Consecutive stable turns before ascent', (
        <SliderField min={1} max={8} value={dial.stabilityWindow} onChange={(v) => onUpdateDial('stabilityWindow', v)} disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Max Context Turns', 'Turns of history included in each agent prompt', (
        <SliderField min={1} max={10} value={dial.maxContextTurns} onChange={(v) => onUpdateDial('maxContextTurns', v)} disabled={disabled} accentColour={badgeColour} />
      ))}

      {/*  Word counts  */}
      <div style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: '#5a6a82', paddingBottom: 4, borderBottom: '1px solid #161b26', marginTop: 8, marginBottom: 4 }}>
        WORD COUNTS
      </div>

      {field('Dialectician Target', 'Target word count per dialectician turn', (
        <SliderField min={20} max={800} step={10} value={dial.dialecticianTargetWordCount} onChange={(v) => onUpdateDial('dialecticianTargetWordCount', v)} suffix=" w" disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Dialectician Max', 'Hard cap on dialectician turn word count', (
        <SliderField min={50} max={1000} step={10} value={dial.dialecticianMaxWordCount} onChange={(v) => onUpdateDial('dialecticianMaxWordCount', v)} suffix=" w" disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Moderator Target', 'Target word count per moderator turn', (
        <SliderField min={20} max={800} step={10} value={dial.moderatorTargetWordCount} onChange={(v) => onUpdateDial('moderatorTargetWordCount', v)} suffix=" w" disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Moderator Max', 'Hard cap on moderator turn word count', (
        <SliderField min={50} max={1000} step={10} value={dial.moderatorMaxWordCount} onChange={(v) => onUpdateDial('moderatorMaxWordCount', v)} suffix=" w" disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Mediator Target', 'Target word count per mediator turn', (
        <SliderField min={20} max={800} step={10} value={dial.mediatorTargetWordCount} onChange={(v) => onUpdateDial('mediatorTargetWordCount', v)} suffix=" w" disabled={disabled} accentColour={badgeColour} />
      ))}
      {field('Mediator Max', 'Hard cap on mediator turn word count', (
        <SliderField min={50} max={1000} step={10} value={dial.mediatorMaxWordCount} onChange={(v) => onUpdateDial('mediatorMaxWordCount', v)} suffix=" w" disabled={disabled} accentColour={badgeColour} />
      ))}

      {/*  Advanced toggle  */}
      <button
        onClick={() => setShowAdvanced((s) => !s)}
        disabled={disabled}
        className="flex items-center gap-2 mt-3 text-[11px] font-mono transition-colors cursor-pointer"
        style={{ color: showAdvanced ? badgeColour : '#5a6a82', background: 'transparent', border: 'none', padding: 0 }}
      >
        <span style={{ fontSize: 10 }}>{showAdvanced ? '▼' : '▶'}</span>
        {showAdvanced ? 'hide advanced' : 'show advanced settings'}
      </button>

      {showAdvanced && (
        <>
          <div style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: '#5a6a82', paddingBottom: 4, borderBottom: '1px solid #161b26', marginTop: 8, marginBottom: 4 }}>
            ADVANCED
          </div>
          {field('Novelty Threshold', 'Minimum novelty score for content to pass quality filter', (
            <SliderField min={0} max={5} step={0.5} value={dial.noveltyThreshold} onChange={(v) => onUpdateDial('noveltyThreshold', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Max Ascension Gate Retries', 'Retries when synthesis fails quality check', (
            <SliderField min={0} max={5} value={dial.maxAscensionGateRetries} onChange={(v) => onUpdateDial('maxAscensionGateRetries', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Max Synthesis Retries', 'Maximum attempts to generate a valid synthesis', (
            <SliderField min={0} max={5} value={dial.maxSynthesisRetries} onChange={(v) => onUpdateDial('maxSynthesisRetries', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Synthesis Resonance Threshold', 'Minimum resonance for synthesis acceptance', (
            <SliderField min={0.1} max={0.9} step={0.05} value={dial.synthesisResonanceThreshold} onChange={(v) => onUpdateDial('synthesisResonanceThreshold', v)} format={(v) => v.toFixed(2)} disabled={disabled} accentColour="#10b981" />
          ))}
          {field('Synthesis Memory Window', 'Turns of synthesis history to retain in context', (
            <SliderField min={2} max={20} value={dial.synthesisMemoryWindow} onChange={(v) => onUpdateDial('synthesisMemoryWindow', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Variability Minimum', 'Minimum topic variance before crisis mode', (
            <SliderField min={0} max={0.2} step={0.005} value={dial.variabilityMinimum} onChange={(v) => onUpdateDial('variabilityMinimum', v)} format={(v) => v.toFixed(3)} disabled={disabled} accentColour="#8a6a2e" />
          ))}
          {field('Saturation Boost Intensity', 'Intensity boost applied during saturation', (
            <SliderField min={0} max={20} value={dial.saturationBoostIntensity} onChange={(v) => onUpdateDial('saturationBoostIntensity', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Citation Top N', 'Maximum citations included per moderator turn', (
            <SliderField min={1} max={10} value={dial.citationTopN} onChange={(v) => onUpdateDial('citationTopN', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Variability Crisis Threshold', 'Below this variance, crisis mode activates', (
            <SliderField min={0.01} max={0.5} step={0.01} value={dial.variabilityCrisisThreshold} onChange={(v) => onUpdateDial('variabilityCrisisThreshold', v)} format={(v) => v.toFixed(2)} disabled={disabled} accentColour="#b78383" />
          ))}
          {field('Crisis Min Turns', 'Minimum turns before crisis mode can exit', (
            <SliderField min={1} max={12} value={dial.crisisMinTurns} onChange={(v) => onUpdateDial('crisisMinTurns', v)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Interjection Snippet Max Length', 'Max characters of context passed to mediator', (
            <SliderField min={200} max={2000} step={50} value={dial.interjectionSnippetMaxLength} onChange={(v) => onUpdateDial('interjectionSnippetMaxLength', v)} suffix=" ch" disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Default Tension Intensity', 'Starting tension level between agents', (
            <SliderField min={0} max={1} step={0.1} value={dial.defaultTensionIntensity} onChange={(v) => onUpdateDial('defaultTensionIntensity', v)} format={(v) => v.toFixed(1)} disabled={disabled} accentColour={badgeColour} />
          ))}
          {field('Sustained Confidence Threshold', 'Confidence level to trigger early synthesis', (
            <SliderField min={0.1} max={0.9} step={0.05} value={dial.sustainedConfidenceThreshold} onChange={(v) => onUpdateDial('sustainedConfidenceThreshold', v)} format={(v) => v.toFixed(2)} disabled={disabled} accentColour="#10b981" />
          ))}
        </>
      )}
    </SectionCard>
  );
}

//  Sub-components
