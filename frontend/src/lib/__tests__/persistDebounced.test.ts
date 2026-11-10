import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { createDebouncedPersist } from '../persistDebounced';

describe('createDebouncedPersist', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('coalesces rapid schedule calls into a single trailing write', () => {
    const write = vi.fn();
    const persist = createDebouncedPersist(write, 300);

    persist.schedule();
    persist.schedule();
    persist.schedule();

    expect(write).not.toHaveBeenCalled();
    vi.advanceTimersByTime(299);
    expect(write).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(write).toHaveBeenCalledTimes(1);
  });

  it('flush persists immediately and cancels any pending debounce', () => {
    const write = vi.fn();
    const persist = createDebouncedPersist(write, 300);

    persist.schedule();
    persist.flush();

    expect(write).toHaveBeenCalledTimes(1);
    vi.advanceTimersByTime(300);
    expect(write).toHaveBeenCalledTimes(1);
  });

  it('dispose cancels a pending write', () => {
    const write = vi.fn();
    const persist = createDebouncedPersist(write, 300);

    persist.schedule();
    persist.dispose();
    vi.advanceTimersByTime(300);
    expect(write).not.toHaveBeenCalled();
  });
});
