import { useEffect, useCallback } from 'react'

export interface Shortcut {
  key: string
  ctrl?: boolean
  shift?: boolean
  alt?: boolean
  description: string
  action: () => void
  /** Prevent default browser behavior (e.g. Ctrl+S saving page) */
  preventDefault?: boolean
}

export interface ShortcutGroup {
  label: string
  shortcuts: Shortcut[]
}

/**
 * Custom hook that registers global keyboard shortcuts.
 * Returns a reactive list of registered shortcuts for the help dialog.
 */
export function useKeyboardShortcuts(groups: ShortcutGroup[]) {
  const handler = useCallback(
    (e: KeyboardEvent) => {
      // Ignore if user is typing in an input/textarea
      const tag = (e.target as HTMLElement)?.tagName
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return

      for (const group of groups) {
        for (const s of group.shortcuts) {
          const keyMatch = e.key.toLowerCase() === s.key.toLowerCase()
          const ctrlMatch = s.ctrl ? (e.ctrlKey || e.metaKey) : !(e.ctrlKey || e.metaKey)
          const shiftMatch = s.shift ? e.shiftKey : !e.shiftKey
          const altMatch = s.alt ? e.altKey : !e.altKey

          if (keyMatch && ctrlMatch && shiftMatch && altMatch) {
            if (s.preventDefault !== false) e.preventDefault()
            s.action()
            return
          }
        }
      }
    },
    [groups]
  )

  useEffect(() => {
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [handler])

  return groups
}
