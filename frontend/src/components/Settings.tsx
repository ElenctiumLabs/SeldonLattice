import React, { useState, useCallback, useEffect, useRef } from 'react';
import type { ModuleType } from '../App';
import {
  type Framework,
  type ProviderDefault,
  getProviderDefaults,
} from '../lib/api/settingsApi';
import { useRequiredFrameworkSettings } from '../hooks/useFrameworkSettings';
import { getConfig } from '../lib/config';
import { api } from '../lib/api';
import { ConfirmDialog } from './SettingsControls';
import { SectionContent as SettingsSectionContent } from './SettingsSections';
import type { SectionId } from './settingsMetadata';
export { PHILOSOPHY_LAYER_GROUPS } from './settingsMetadata';
export type { LayerGroupSpec } from './settingsMetadata';

export { TextInput } from './SettingsControls';

//  Props 

interface SettingsProps {
  onBack: () => void;
  activeModule: ModuleType;
  /** When true, all inputs are locked and a banner is shown. */
  sessionActive: boolean;
  /** Called when "Save & Begin" is clicked in gate mode. */
  onSaveAndBegin?: () => void;
  /** Whether we're in first-time gate mode (full-screen, with Save & Begin CTA). */
  gateMode?: boolean;
}

//  Nav sections 

interface Section {
  id: SectionId;
  icon: string;
  label: string;
  desc: string;
}

const SECTIONS: Section[] = [
  { id: 'servers', icon: '🖥', label: 'Servers', desc: 'Launch local llama.cpp servers' },
  { id: 'connection', icon: '⚡', label: 'Connection', desc: 'LLM provider & model' },
  { id: 'engine', icon: '⚙', label: 'Engine', desc: 'Dialectical parameters' },
  { id: 'quality-gates', icon: '⊛', label: 'Quality Gates', desc: 'Gate thresholds' },
  { id: 'dialectics', icon: '⧗', label: 'Dialectics', desc: 'Reasoning engine' },
  { id: 'agents', icon: '◈', label: 'Agents', desc: 'Enable & weight agents' },
  { id: 'display', icon: '◻', label: 'Display', desc: 'Visual preferences' },
  { id: 'notifications', icon: '◎', label: 'Alerts', desc: 'Notification config' },
  { id: 'export', icon: '↗', label: 'Export', desc: 'Transcript & graph' },
  { id: 'sessions', icon: '◷', label: 'Sessions', desc: 'Default parameters' },
  { id: 'audit', icon: '◈', label: 'Audit', desc: 'Cryptographic proof' },
  { id: 'data', icon: '◻', label: 'Data', desc: 'Cache controls' },
];

//  Agent list by framework 

const FRAMEWORK_BADGE: Record<string, { label: string; colour: string }> = {
  philosophy: { label: 'Philosophical Reasoning', colour: '#11b880' },
  research: { label: 'Research Orchestration', colour: '#4c89ff' },
};

//  Main component 

export default function Settings({
  onBack,
  activeModule,
  sessionActive,
  onSaveAndBegin,
  gateMode = false,
}: SettingsProps) {
  const framework = (activeModule || 'philosophy') as Framework;

  const {
    settings: dto,
    agents,
    qg,
    dial,
    loading,
    saveState,
    saveError,
    updateSetting: update,
    updateAgent,
    updateLayer,
    updateQg,
    updateDial,
    saveNow,
  } = useRequiredFrameworkSettings(framework);

  const [activeSection, setActiveSection] = useState<SectionId>('servers');
  const [providerDefaults, setProviderDefaults] = useState<Record<string, ProviderDefault> | null>(null);
  const [providerError, setProviderError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    getProviderDefaults()
      .then((catalog) => {
        if (active) setProviderDefaults(catalog);
      })
      .catch((error) => {
        if (active) setProviderError(error instanceof Error ? error.message : String(error));
      });
    return () => { active = false; };
  }, []);
  const [connectionOk, setConnectionOk] = useState<boolean | null>(null);
  const testTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [deleteSessionsState, setDeleteSessionsState] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [clearDataState, setClearDataState] = useState<'idle' | 'loading' | 'success' | 'error'>('idle');
  const [confirmDialog, setConfirmDialog] = useState<{ open: boolean; title: string; message: string; variant: 'danger' | 'warning'; onConfirm: () => void }>({
    open: false, title: '', message: '', variant: 'danger', onConfirm: () => {},
  });

  //  Save & Begin (gate mode) 

  const handleSaveAndBegin = async () => {
    try {
      await saveNow();
      onSaveAndBegin?.();
    } catch {
      // error handled in hook
    }
  };

  //  Test connection 

  const handleTestConnection = useCallback(() => {
    setConnectionOk(null);
    if (testTimer.current) clearTimeout(testTimer.current);
    testTimer.current = setTimeout(async () => {
      try {
        const res = await fetch('/api/health');
        setConnectionOk(res.ok);
      } catch {
        setConnectionOk(false);
      }
    }, 200);
  }, []);

  //  Export helpers 

  const handleExportAudit = () => {
    if (!dto) throw new Error('Settings are not loaded.');
    const data = { timestamp: new Date().toISOString(), merkleRoot: 'pending - connect to backend', settings: dto };
    downloadBlob(JSON.stringify(data, null, 2), 'application/json', `seldon-audit-${Date.now()}.json`);
  };

  const handleExportTranscript = () => {
    if (!dto) throw new Error('Settings are not loaded.');
    const data = { timestamp: new Date().toISOString(), format: dto.exportFormat, includeMerkleProofs: dto.includeMerkleProofs, includeMetrics: dto.includeMetrics };
    downloadBlob(JSON.stringify(data, null, 2), 'application/json', `seldon-export-${Date.now()}.${dto.exportFormat}`);
  };

  const handleClearData = () => {
    setConfirmDialog({
      open: true,
      title: 'Clear Session Data',
      message: 'This will clear all cached session data from this browser. Are you sure?',
      variant: 'warning',
      onConfirm: () => {
        ['seldon_live_turns', 'seldon_selected_id', 'seldon_selected_round',
          'seldon_selected_round_module', 'seldon_active_session_id', 'seldon_path_input'].forEach((k) => localStorage.removeItem(k));
        window.dispatchEvent(new CustomEvent('seldonSessionCleared'));
      },
    });
  };

  const handleDeleteAllSessions = () => {
    setConfirmDialog({
      open: true,
      title: 'Delete All Sessions',
      message: 'This will permanently delete ALL sessions from the database. This cannot be undone. Are you sure?',
      variant: 'danger',
      onConfirm: async () => {
        setDeleteSessionsState('loading');
        try {
          await api.deleteAllSessions();
          ['seldon_live_turns', 'seldon_selected_id', 'seldon_selected_round',
            'seldon_active_session_id', 'seldon_path_input', 'seldon_session_id'].forEach((k) => localStorage.removeItem(k));
          window.dispatchEvent(new CustomEvent('seldonSessionCleared'));
          setDeleteSessionsState('success');
          setTimeout(() => setDeleteSessionsState('idle'), 3000);
        } catch {
          setDeleteSessionsState('error');
          setTimeout(() => setDeleteSessionsState('idle'), 3000);
        }
      },
    });
  };

  const handleClearMerkleAndKeys = () => {
    setConfirmDialog({
      open: true,
      title: 'Clear Merkle & Keys',
      message: 'This will erase the Merkle DAG store and all ML-DSA signing keys. Audit trails will be permanently lost. Are you sure?',
      variant: 'danger',
      onConfirm: async () => {
        setClearDataState('loading');
        try {
          await api.clearDataDir();
          setClearDataState('success');
          setTimeout(() => setClearDataState('idle'), 3000);
        } catch {
          setClearDataState('error');
          setTimeout(() => setClearDataState('idle'), 3000);
        }
      },
    });
  };

  //  Framework badge 

  const badge = FRAMEWORK_BADGE[framework] ?? { label: framework, colour: '#fbbf24' };

  //  Save indicator text 

  const saveLabel = saveState === 'saving' ? 'saving…' : saveState === 'saved' ? 'saved ✓' : saveState === 'error' ? 'error!' : null;
  const saveColour = saveState === 'error' ? '#b78383' : '#10b981';

  const agentList = getConfig().agents;

  if (loading || (!providerDefaults && !providerError)) {
    return (
      <SettingsShell gateMode={gateMode} onBack={onBack}>
        <div className="flex items-center justify-center h-full" style={{ color: '#8b9bb4', fontFamily: 'var(--pr-font-mono)', fontSize: 13 }}>
          loading settings...
        </div>
      </SettingsShell>
    );
  }

  // Loud failures: the ErrorBoundary renders the message. Never fall back to a default
  // provider list or an empty settings object.
  if (providerError) throw new Error(providerError);
  if (!providerDefaults) throw new Error('Provider catalog is unavailable.');
  if (!dto) throw new Error(`Settings for '${framework}' are unavailable.`);
  if (!qg || !dial) throw new Error(`Engine settings for '${framework}' are unavailable.`);

  return (
    <>
    <SettingsShell gateMode={gateMode} onBack={onBack}>
      {/*  Outer layout  */}
      <div className="flex flex-col h-full" style={{ background: 'var(--pr-bg)', color: 'var(--pr-text)' }}>

        {/*  Header  */}
        <div
          className="flex items-center justify-between px-6 py-3 shrink-0 border-b"
          style={{ borderColor: 'var(--pr-border)', background: 'var(--pr-surface)' }}
        >
          <div className="flex items-center gap-4">
            {!gateMode && (
              <button
                onClick={onBack}
                className="flex items-center gap-1.5 px-2.5 py-1 rounded border transition-colors cursor-pointer hover:bg-(--pr-surface-raised)"
                style={{ borderColor: 'var(--pr-border)', color: 'var(--pr-text-muted)', fontFamily: 'var(--pr-font-mono)', fontSize: 12 }}
              >
                <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                  <path d="M19 12H5M12 19l-7-7 7-7" />
                </svg>
                back
              </button>
            )}
            <div>
              <div className="flex items-center gap-3">
                <h1 style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)', fontSize: 18, lineHeight: 1 }}>
                  {gateMode ? 'Configure Framework' : 'Settings'}
                </h1>
                <span
                  className="px-2 py-0.5 rounded text-[10px] font-mono"
                  style={{ background: `${badge.colour}18`, border: `1px solid ${badge.colour}40`, color: badge.colour }}
                >
                  {badge.label}
                </span>
              </div>
              {gateMode && (
                <p style={{ fontFamily: 'var(--pr-font-mono)', color: 'var(--pr-text-dim)', fontSize: 11, marginTop: 2 }}>
                  Set your preferences before beginning the session
                </p>
              )}
            </div>
          </div>

          <div className="flex items-center gap-3">
            {saveLabel && (
              <span
                className="text-[11px] px-2 py-0.5 rounded border transition-all"
                style={{ fontFamily: 'var(--pr-font-mono)', color: saveColour, borderColor: saveColour, background: `${saveColour}18` }}
              >
                {saveLabel}
              </span>
            )}
          </div>
        </div>

        {saveError && (
          <div
            className="flex items-start gap-2 px-6 py-2 shrink-0"
            style={{ background: 'rgba(185,90,58,0.12)', borderBottom: '1px solid rgba(185,90,58,0.3)' }}
          >
            <span style={{ color: '#e07a5f', fontSize: 12 }}>⚠</span>
            <p style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: '#e07a5f', whiteSpace: 'pre-wrap' }}>
              {saveError}
            </p>
          </div>
        )}

        {/*  Session lock banner  */}
        {sessionActive && (
          <div
            className="flex items-center gap-2 px-6 py-2 shrink-0"
            style={{ background: 'rgba(184,131,74,0.12)', borderBottom: '1px solid rgba(184,131,74,0.25)' }}
          >
            <span style={{ color: 'var(--pr-role-proposer)', fontSize: 12 }}>⚠  </span>
            <p style={{ fontFamily: 'var(--pr-font-mono)', fontSize: 11, color: 'var(--pr-role-proposer)' }}>
              Settings are locked during an active session. End the session to make changes.
            </p>
          </div>
        )}

        {/*  Two-column body  */}
        <div className="flex flex-1 overflow-hidden">

          {/* Sidebar nav */}
          <nav
            className="flex flex-col gap-1 p-3 shrink-0 overflow-y-auto border-r"
            style={{ width: 210, borderColor: 'var(--pr-border)', background: 'var(--pr-surface)' }}
          >
            {SECTIONS.map((s) => {
              const active = activeSection === s.id;
              return (
                <button
                  key={s.id}
                  onClick={() => setActiveSection(s.id)}
                  className="flex flex-col items-start px-3.5 py-2.5 rounded-lg transition-all cursor-pointer text-left group"
                  style={{
                    background: active ? 'var(--pr-surface-raised)' : 'transparent',
                    border: `1px solid ${active ? 'var(--pr-border-strong)' : 'transparent'}`,
                  }}
                >
                  <div className="flex items-center gap-2.5 mb-1">
                    <span style={{ fontSize: 15, color: active ? badge.colour : 'var(--pr-text-muted)' }}>{s.icon}</span>
                    <span
                      style={{
                        fontFamily: 'var(--pr-font-mono)',
                        fontSize: 13,
                        color: active ? 'var(--pr-text)' : 'var(--pr-text-muted)',
                        fontWeight: active ? 700 : 500,
                      }}
                    >
                      {s.label}
                    </span>
                  </div>
                  <span
                    style={{
                      fontFamily: 'var(--pr-font-mono)',
                      fontSize: 11,
                      color: active ? 'var(--pr-text)' : 'var(--pr-text-dim)',
                      paddingLeft: 26,
                      lineHeight: 1.2,
                    }}
                  >
                    {s.desc}
                  </span>
                </button>
              );
            })}
          </nav>

          {/* Content area */}
          <div className="flex-1 overflow-y-auto p-6 relative" style={{ background: 'var(--pr-bg)' }}>

            {/* Locked overlay */}
            {sessionActive && (
              <div
                className="absolute inset-0 z-10"
                style={{ background: 'rgba(15,12,8,0.3)', backdropFilter: 'blur(2px)', cursor: 'not-allowed' }}
              />
            )}

            <SettingsSectionContent
              id={activeSection}
              dto={dto}
              agents={agents}
              agentList={agentList}
              framework={framework}
              qg={qg}
              dial={dial}
              providerDefaults={providerDefaults}
              connectionOk={connectionOk}
              onUpdate={update}
              onUpdateAgent={updateAgent}
              onUpdateLayer={updateLayer}
              onUpdateQg={updateQg}
              onUpdateDial={updateDial}
              onTestConnection={handleTestConnection}
              onExportAudit={handleExportAudit}
              onExportTranscript={handleExportTranscript}
              onClearData={handleClearData}
              onDeleteAllSessions={handleDeleteAllSessions}
              onClearMerkleAndKeys={handleClearMerkleAndKeys}
              deleteSessionsState={deleteSessionsState}
              clearDataState={clearDataState}
              disabled={sessionActive}
              badgeColour={badge.colour}
            />
          </div>
        </div>

        {/*  Gate mode footer  */}
        {gateMode && (
          <div
            className="flex items-center justify-between px-6 py-4 border-t flex-0"
            style={{ borderColor: 'var(--pr-border)', background: 'var(--pr-surface)' }}
          >
            <button
              onClick={onBack}
              className="px-4 py-2 rounded border cursor-pointer transition-colors hover:bg-(--pr-surface-raised)"
              style={{ borderColor: 'var(--pr-border)', color: 'var(--pr-text-muted)', fontFamily: 'var(--pr-font-mono)', fontSize: 12 }}
            >
              ← back to frameworks
            </button>
            <button
              onClick={handleSaveAndBegin}
              disabled={saveState === 'saving'}
              className="px-6 py-2.5 rounded cursor-pointer transition-all"
              style={{
                fontFamily: 'var(--pr-font-display)',
                fontSize: 14,
                color: '#ffffff',
                background: saveState === 'saving'
                  ? 'var(--pr-text-dim)'
                  : `linear-gradient(135deg, ${badge.colour}, ${badge.colour}cc)`,
                boxShadow: saveState !== 'saving' ? `0 0 20px ${badge.colour}40` : 'none',
                border: 'none',
              }}
            >
              {saveState === 'saving' ? 'Saving...' : 'Save & Begin  →'}
            </button>
          </div>
        )}

        {/*  Normal mode footer  */}
        {!gateMode && (
          <div
            className="flex items-center justify-between px-6 py-3 border-t flex-0"
            style={{ borderColor: 'var(--pr-border)', background: 'var(--pr-surface)' }}
          >
            <span style={{ fontFamily: 'var(--pr-font-mono)', color: 'var(--pr-text-dim)', fontSize: 11 }}>
              Changes save automatically
            </span>
            <button
              onClick={onBack}
              className="px-4 py-1.5 rounded cursor-pointer transition-all font-mono text-[12px]"
              style={{ background: 'var(--pr-surface-raised)', border: '1px solid var(--pr-border)', color: 'var(--pr-text)' }}
            >
              Done
            </button>
          </div>
        )}
      </div>
    </SettingsShell>

    <ConfirmDialog
      open={confirmDialog.open}
      title={confirmDialog.title}
      message={confirmDialog.message}
      variant={confirmDialog.variant}
      onConfirm={async () => {
        confirmDialog.onConfirm();
        setConfirmDialog((d) => ({ ...d, open: false }));
      }}
      onCancel={() => setConfirmDialog((d) => ({ ...d, open: false }))}
    />
    </>
  );
}

//  Shell wrapper 

function SettingsShell({ children, gateMode, onBack }: { children: React.ReactNode; gateMode: boolean; onBack: () => void }) {
  if (gateMode) {
    return (
      <div className="fixed inset-0 z-400 flex flex-col" style={{ background: 'var(--pr-bg)' }}>
        {children}
      </div>
    );
  }
  return (
    <>
      <div
        className="fixed inset-0 z-300 flex items-center justify-center p-4"
        style={{ backgroundColor: 'rgba(15, 12, 8, 0.55)', backdropFilter: 'blur(6px)' }}
        onClick={onBack}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => e.key === 'Escape' && onBack()}
      >
        <div
          className="w-full max-h-[92vh] flex flex-col rounded-xl animate-fade-in-up overflow-hidden pr-glass-surface"
          style={{
            maxWidth: 860,
            height: '92vh',
            background: 'var(--pr-surface)',
            border: '1px solid var(--pr-border)',
            boxShadow: '0 24px 60px rgba(0,0,0,0.25)',
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {children}
        </div>
      </div>
    </>
  );
}

//  Section content dispatcher 

//  Utility 

function downloadBlob(content: string, mimeType: string, filename: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  a.click();
  URL.revokeObjectURL(url);
}
