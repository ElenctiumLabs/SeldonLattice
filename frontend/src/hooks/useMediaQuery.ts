import { useState, useEffect } from 'react';

/**
 * React hook to listen for media queries (e.g. `(min-width: 768px)`).
 * SSR-safe, responds dynamically to window resize events.
 */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false;
    return window.matchMedia(query).matches;
  });

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const mediaQueryList = window.matchMedia(query);
    const listener = (event: MediaQueryListEvent) => {
      setMatches(event.matches);
    };

    setMatches(mediaQueryList.matches);

    if (mediaQueryList.addEventListener) {
      mediaQueryList.addEventListener('change', listener);
      return () => mediaQueryList.removeEventListener('change', listener);
    } else {
      // Fallback for older browsers
      mediaQueryList.addListener(listener);
      return () => mediaQueryList.removeListener(listener);
    }
  }, [query]);

  return matches;
}
