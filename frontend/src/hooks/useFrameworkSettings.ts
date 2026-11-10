import { useState, useEffect, useCallback, useRef } from 'react';
import {
  type Framework,
  type SettingsDto,
  type AgentConfig,
  type QualityGatesConfig,
  type DialecticsConfig,
  getSettings,
  saveSettings,
  normalizeConfigKeys,
} from '../lib/api/settingsApi';
import { getConfig } from '../lib/config';

function requiredAgents(framework: string): string[] {
  const agents = getConfig().agents;
  if (!agents || agents.length === 0) {
    throw new Error(`FrontendConfig has no agent roster for '${framework}'.`);
  }
  return agents;
}

export function parseAgentsJson(json: string | undefined, framework: string): Record<string, AgentConfig> {
  const agents = requiredAgents(framework);
  const parsed = JSON.parse(json || '{}') as Record<string, AgentConfig>;
  // Keep every stored agent (including the shadow subnode) so a save cannot silently
  // drop settings that the UI does not currently render.
  const result: Record<string, AgentConfig> = { ...parsed };

  for (const id of agents) {
    const value = result[id];
    if (!value || typeof value.enabled !== 'boolean' || typeof value.qualityWeight !== 'number') {
      throw new Error(
        `agentsJson for '${framework}' is missing or invalid for agent '${id}'. ` +
        'Re-seed or re-save the settings row.',
      );
    }
  }

  return result;
}

function parseEngineConfig<T extends object>(json: string, field: string): T {
  const parsed = JSON.parse(json) as Record<string, unknown>;
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error(`${field} for the active framework must be a JSON object.`);
  }
  return normalizeConfigKeys(parsed) as T;
}

interface SettingsDraft {
  dto: SettingsDto;
  agents: Record<string, AgentConfig>;
  qg: QualityGatesConfig;
  dial: DialecticsConfig;
}

// In-memory module cache so components instantly share the freshest fetched settings without flash.
const settingsCache = new Map<string, SettingsDto>();

// In-flight request deduplication: if multiple hook instances mount simultaneously
// for the same framework, they share a single fetch rather than each firing their own.
const settingsInflight = new Map<string, Promise<SettingsDto>>();

export function useFrameworkSettings(frameworkKey: Framework | string | null | undefined) {
  const framework: Framework = (frameworkKey || 'philosophy') as Framework;

  const [settings, setSettings] = useState<SettingsDto | null>(() => settingsCache.get(framework) ?? null);
  const [agents, setAgents] = useState<Record<string, AgentConfig>>({});
  const [qg, setQg] = useState<QualityGatesConfig | null>(null);
  const [dial, setDial] = useState<DialecticsConfig | null>(null);
  const [loading, setLoading] = useState<boolean>(!settingsCache.has(framework));
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [saveError, setSaveError] = useState<string | null>(null);
  const [reloadToken, setReloadToken] = useState(0);

  const draftRef = useRef<SettingsDraft | null>(null);
  const pendingRef = useRef<SettingsDraft | null>(null);
  const saveTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const saveChainRef = useRef<Promise<void>>(Promise.resolve());
  const sourceRef = useRef(`settings-hook-${Math.random().toString(36).slice(2)}`);

  const applySettingsDto = useCallback((dto: SettingsDto) => {
    const nextAgents = parseAgentsJson(dto.agentsJson, framework);
    const nextQg = parseEngineConfig<QualityGatesConfig>(dto.qualityGatesJson, 'qualityGatesJson');
    const nextDial = parseEngineConfig<DialecticsConfig>(dto.dialecticsJson, 'dialecticsJson');

    draftRef.current = { dto, agents: nextAgents, qg: nextQg, dial: nextDial };
    settingsCache.set(framework, dto);
    setSettings(dto);
    setAgents(nextAgents);
    setQg(nextQg);
    setDial(nextDial);
  }, [framework]);

  // Load from backend on mount or framework change. There is no default DTO: a failed
  // load sets loadError, and useRequiredFrameworkSettings throws it into the boundary.
  useEffect(() => {
    let active = true;
    const cached = settingsCache.get(framework);

    if (cached) {
      try {
        applySettingsDto(cached);
        setLoading(false);
      } catch {
        settingsCache.delete(framework);
      }
    }

    setLoading(true);
    setLoadError(null);

    let inflight = settingsInflight.get(framework);
    if (!inflight) {
      inflight = getSettings(framework).finally(() => settingsInflight.delete(framework));
      settingsInflight.set(framework, inflight);
    }

    inflight
      .then((data) => {
        if (!active) return;
        applySettingsDto(data);
        setLoadError(null);
        setLoading(false);
      })
      .catch((error) => {
        if (!active) return;
        setLoadError(error instanceof Error ? error.message : String(error));
        setLoading(false);
      });

    const onUpdated = (event: Event) => {
      const detail = (event as CustomEvent<{ framework: Framework; settings: SettingsDto; source?: string }>).detail;
      if (detail?.framework !== framework || !detail.settings) return;
      // Ignore this hook's own save echo; otherwise a debounced save can overwrite a
      // newer in-flight edit with the older server snapshot.
      if (detail.source && detail.source === sourceRef.current) return;

      try {
        applySettingsDto(detail.settings);
        setLoadError(null);
      } catch (error) {
        setLoadError(error instanceof Error ? error.message : String(error));
      }
    };

    window.addEventListener('seldonSettingsUpdated', onUpdated);
    return () => {
      active = false;
      window.removeEventListener('seldonSettingsUpdated', onUpdated);
    };
  }, [framework, applySettingsDto, reloadToken]);

  const buildPayload = useCallback((draft: SettingsDraft): SettingsDto => ({
    ...draft.dto,
    // Serialize the complete roster, not only the agents the UI renders.
    agentsJson: JSON.stringify(draft.agents),
    qualityGatesJson: JSON.stringify(draft.qg),
    dialecticsJson: JSON.stringify(draft.dial),
  }), []);

  const enqueueSave = useCallback((draft: SettingsDraft, applyResponse: boolean): Promise<SettingsDto> => {
    const payload = buildPayload(draft);

    const run = saveChainRef.current.then(async () => {
      setSaveState('saving');
      setSaveError(null);

      const saved = await saveSettings(framework, payload, sourceRef.current);

      // The cache always holds the last successful server state. When this was an
      // explicit Save, also replace the UI state; debounced saves keep in-flight edits.
      settingsCache.set(framework, saved);
      if (applyResponse) applySettingsDto(saved);

      setSaveState('saved');
      setTimeout(() => setSaveState((state) => (state === 'saved' ? 'idle' : state)), 2000);
      return saved;
    });

    // Keep the queue alive after a failure while still returning the failing promise to
    // the caller (saveNow) so it can show the error.
    saveChainRef.current = run.then(() => undefined, () => undefined);

    run.catch((error) => {
      setSaveState('error');
      setSaveError(error instanceof Error ? error.message : String(error));
      setTimeout(() => setSaveState((state) => (state === 'error' ? 'idle' : state)), 5000);
    });

    return run;
  }, [applySettingsDto, buildPayload, framework]);

  const scheduleSave = useCallback((draft: SettingsDraft) => {
    pendingRef.current = draft;
    if (saveTimer.current) clearTimeout(saveTimer.current);

    setSaveState('saving');
    saveTimer.current = setTimeout(() => {
      const pending = pendingRef.current;
      pendingRef.current = null;
      if (pending) void enqueueSave(pending, false);
    }, 600);
  }, [enqueueSave]);

  const saveNow = useCallback(async (): Promise<SettingsDto> => {
    if (saveTimer.current) clearTimeout(saveTimer.current);
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot save.');

    pendingRef.current = null;
    return enqueueSave(draft, true);
  }, [enqueueSave]);

  const updateSetting = useCallback(<K extends keyof SettingsDto>(key: K, value: SettingsDto[K]) => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot update.');

    const next: SettingsDraft = { ...draft, dto: { ...draft.dto, [key]: value } };
    draftRef.current = next;
    settingsCache.set(framework, next.dto);
    setSettings(next.dto);
    scheduleSave(next);
  }, [framework, scheduleSave]);

  const updateAgent = useCallback((agentId: string, key: keyof AgentConfig, value: boolean | number) => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot update.');

    const agents = {
      ...draft.agents,
      [agentId]: { ...draft.agents[agentId], [key]: value },
    };
    const next: SettingsDraft = { ...draft, agents };
    draftRef.current = next;
    setAgents(agents);
    scheduleSave(next);
  }, [scheduleSave]);

  const toggleAgent = useCallback((agentId: string) => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot update.');

    const current = draft.agents[agentId];
    if (!current) throw new Error(`Agent '${agentId}' is missing from agentsJson.`);

    const agents = {
      ...draft.agents,
      [agentId]: { ...current, enabled: !current.enabled },
    };
    const next: SettingsDraft = { ...draft, agents };
    draftRef.current = next;
    setAgents(agents);
    scheduleSave(next);
  }, [scheduleSave]);

  const updateLayer = useCallback((layerAgents: string[], key: keyof AgentConfig, value: boolean | number) => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot update.');

    const agents = { ...draft.agents };
    for (const id of layerAgents) {
      const current = agents[id];
      if (!current) throw new Error(`Agent '${id}' is missing from agentsJson.`);
      agents[id] = { ...current, [key]: value };
    }
    const next: SettingsDraft = { ...draft, agents };
    draftRef.current = next;
    setAgents(agents);
    scheduleSave(next);
  }, [scheduleSave]);

  const updateQg = useCallback(<K extends keyof QualityGatesConfig>(key: K, value: QualityGatesConfig[K]) => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot update.');

    const qg = { ...draft.qg, [key]: value };
    const next: SettingsDraft = { ...draft, qg };
    draftRef.current = next;
    setQg(qg);
    scheduleSave(next);
  }, [scheduleSave]);

  const updateDial = useCallback(<K extends keyof DialecticsConfig>(key: K, value: DialecticsConfig[K]) => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot update.');

    const dial = { ...draft.dial, [key]: value };
    const next: SettingsDraft = { ...draft, dial };
    draftRef.current = next;
    setDial(dial);
    scheduleSave(next);
  }, [scheduleSave]);

  const resetAgents = useCallback(() => {
    const draft = draftRef.current;
    if (!draft) throw new Error('Settings are not loaded; cannot reset.');

    const defaults: Record<string, AgentConfig> = { ...draft.agents };
    for (const id of requiredAgents(framework)) {
      defaults[id] = { enabled: true, qualityWeight: 1.0 };
    }
    const next: SettingsDraft = { ...draft, agents: defaults };
    draftRef.current = next;
    setAgents(defaults);
    scheduleSave(next);
  }, [framework, scheduleSave]);

  const reload = useCallback(() => {
    settingsCache.delete(framework);
    settingsInflight.delete(framework);
    setReloadToken((token) => token + 1);
  }, [framework]);

  return {
    framework,
    settings,
    agents,
    qg,
    dial,
    loading,
    loadError,
    saveState,
    saveError,
    useStaticQuestions: settings?.useStaticQuestions,
    layoutMode: settings?.layoutMode,
    updateSetting,
    updateAgent,
    toggleAgent,
    updateLayer,
    resetAgents,
    updateQg,
    updateDial,
    saveNow,
    reload,
  };
}

/**
 * Strict consumer: throws a detailed error into the nearest ErrorBoundary when settings
 * are missing/incomplete. Use this in components that cannot operate without settings.
 */
export function useRequiredFrameworkSettings(frameworkKey: Framework | string | null | undefined) {
  const value = useFrameworkSettings(frameworkKey);

  if (value.loadError) {
    throw new Error(value.loadError);
  }
  if (!value.loading && !value.settings) {
    throw new Error(`Settings for '${value.framework}' are unavailable.`);
  }

  return value;
}
