import { useState, useEffect, useCallback } from 'react';

export type Theme = 'dark' | 'light';

export const THEME_STORAGE_KEY = 'seldon-theme';
export const THEME_EVENT = 'seldon-theme-change';

function getSystemPreference(): Theme {
  if (typeof window === 'undefined') return 'dark';
  return window.matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

function getStoredTheme(): Theme | null {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    if (stored === 'dark' || stored === 'light') return stored;
  } catch {}
  return null;
}

export function getCurrentTheme(): Theme {
  if (typeof document !== 'undefined') {
    const docTheme = document.documentElement.getAttribute('data-theme');
    if (docTheme === 'light' || docTheme === 'sol' || docTheme === 'albedo') return 'light';
    if (docTheme === 'dark' || docTheme === 'prime-radiant' || docTheme === 'the-great-work' || docTheme === 'nigredo') return 'dark';
  }
  return getStoredTheme() || getSystemPreference();
}

export function applyTheme(theme: Theme) {
  if (typeof document !== 'undefined') {
    document.documentElement.setAttribute('data-theme', theme);
  }
}

export function useTheme() {
  const [theme, setThemeState] = useState<Theme>(() => getCurrentTheme());

  useEffect(() => {
    // Sync with DOM on mount
    const current = getCurrentTheme();
    setThemeState(current);
    applyTheme(current);

    // Listen for custom theme change events across components
    const handleThemeChange = (e: Event) => {
      const customEvent = e as CustomEvent<Theme>;
      if (customEvent.detail) {
        setThemeState(customEvent.detail);
      } else {
        setThemeState(getCurrentTheme());
      }
    };

    // Listen for storage events (e.g. across tabs)
    const handleStorage = (e: StorageEvent) => {
      if (e.key === THEME_STORAGE_KEY && (e.newValue === 'light' || e.newValue === 'dark')) {
        setThemeState(e.newValue);
        applyTheme(e.newValue);
      }
    };

    // Listen for system preference changes
    const mq = window.matchMedia('(prefers-color-scheme: light)');
    const handleMq = (e: MediaQueryListEvent) => {
      if (getStoredTheme() === null) {
        const newTheme = e.matches ? 'light' : 'dark';
        setThemeState(newTheme);
        applyTheme(newTheme);
      }
    };

    window.addEventListener(THEME_EVENT, handleThemeChange);
    window.addEventListener('storage', handleStorage);
    mq.addEventListener('change', handleMq);

    return () => {
      window.removeEventListener(THEME_EVENT, handleThemeChange);
      window.removeEventListener('storage', handleStorage);
      mq.removeEventListener('change', handleMq);
    };
  }, []);

  const toggleTheme = useCallback(() => {
    const current = getCurrentTheme();
    const next: Theme = current === 'dark' ? 'light' : 'dark';
    try {
      localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {}
    applyTheme(next);
    setThemeState(next);
    // Dispatch global event so all components update immediately
    window.dispatchEvent(new CustomEvent<Theme>(THEME_EVENT, { detail: next }));
  }, []);

  const setTheme = useCallback((newTheme: Theme) => {
    try {
      localStorage.setItem(THEME_STORAGE_KEY, newTheme);
    } catch {}
    applyTheme(newTheme);
    setThemeState(newTheme);
    window.dispatchEvent(new CustomEvent<Theme>(THEME_EVENT, { detail: newTheme }));
  }, []);

  return { theme, toggleTheme, setTheme };
}
