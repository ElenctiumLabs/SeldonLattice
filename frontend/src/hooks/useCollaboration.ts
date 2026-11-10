import { useState, useEffect, useCallback, useRef } from 'react'

const COLORS = [
  '#11b880',
  '#FF6B6B',
  '#4ECDC4',
  '#45B7D1',
  '#96CEB4',
  '#FFEAA7',
  '#DDA0DD',
  '#87CEEB',
  '#FF7F50',
  '#98D8C8',
]

export interface Collaborator {
  id: string
  userId: string
  name: string
  color: string
  joinedAt: string
  isActive: boolean
  lastSeen: string
  cursorPosition?: { x: number; y: number }
}

interface UseCollaborationOptions {
  sessionId: string
  userId: string
  userName: string
  onCursorUpdate?: (position: { x: number; y: number }) => void
}

export function useCollaboration({
  sessionId,
  userId,
  userName: _userName,
  onCursorUpdate,
}: UseCollaborationOptions) {
  const [collaborators, setCollaborators] = useState<Collaborator[]>([])
  const [isLive, setIsLive] = useState(false)
  const [shareToken, setShareToken] = useState<string | null>(null)
  const cursorRef = useRef<number | null>(null)
  const userColorRef = useRef<string>(COLORS[0])

  useEffect(() => {
    const hash = userId.split('').reduce((a, b) => {
      const char = b.charCodeAt(0)
      return (a << 5) - a + char
    }, 0)
    userColorRef.current = COLORS[Math.abs(hash) % COLORS.length]
  }, [userId])

  useEffect(() => {
    const mockCollaborators: Collaborator[] = [
      {
        id: `${sessionId}-mock-1`,
        userId: 'mock-user-1',
        name: 'Alex R.',
        color: COLORS[1],
        joinedAt: new Date().toISOString(),
        isActive: true,
        lastSeen: new Date().toISOString(),
        cursorPosition: { x: 120, y: 80 },
      },
      {
        id: `${sessionId}-mock-2`,
        userId: 'mock-user-2',
        name: 'Sam K.',
        color: COLORS[3],
        joinedAt: new Date(Date.now() - 600000).toISOString(),
        isActive: false,
        lastSeen: new Date(Date.now() - 600000).toISOString(),
      },
    ]

    setCollaborators(mockCollaborators)
    setIsLive(true)

    return () => {
      setIsLive(false)
    }
  }, [sessionId])

  useEffect(() => {
    if (onCursorUpdate) {
      cursorRef.current = window.setInterval(() => {
        const position = {
          x: Math.random() * 800,
          y: Math.random() * 600,
        }
        onCursorUpdate(position)
      }, 1500)
    }

    return () => {
      if (cursorRef.current) clearInterval(cursorRef.current)
    }
  }, [onCursorUpdate])

  const generateShareToken = useCallback(() => {
    const token = btoa(`${sessionId}-${Date.now()}-${Math.random()}`).slice(0, 32)
    setShareToken(token)
    return token
  }, [sessionId])

  const revokeShareToken = useCallback(() => {
    setShareToken(null)
  }, [])

  const shareSession = useCallback(() => {
    const token = generateShareToken()
    const url = `${window.location.origin}/?session=${sessionId}&token=${token}&ro=true`
    return { token, url }
  }, [sessionId, generateShareToken])

  const otherCollaborators = collaborators.filter((c) => c.userId !== userId)

  return {
    collaborators: otherCollaborators,
    allCollaborators: collaborators,
    myColor: userColorRef.current,
    isLive,
    shareToken,
    generateShareToken,
    revokeShareToken,
    shareSession,
  }
}
