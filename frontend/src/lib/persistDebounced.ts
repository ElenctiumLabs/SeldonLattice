import { getConfig } from './config';

/**
 * Trailing-debounced persist helper for high-frequency state (e.g. SSE live turns).
 * `schedule()` coalesces rapid writes into a single trailing call; `flush()` forces
 * an immediate write (used on stream stop/unmount so the last state is never lost);
 * `dispose()` cancels any pending write.
 */
export function createDebouncedPersist(
  write: () => void,
  delayMs?: number
): { schedule: () => void; flush: () => void; dispose: () => void } {
  const delay = delayMs ?? getConfig().debouncePersistMs;
  let timer: ReturnType<typeof setTimeout> | null = null;

  const clear = () => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
  };

  return {
    schedule() {
      clear();
      timer = setTimeout(() => {
        timer = null;
        write();
      }, delay);
    },
    flush() {
      clear();
      write();
    },
    dispose() {
      clear();
    },
  };
}
