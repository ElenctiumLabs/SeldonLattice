import { useState, useCallback, useEffect, useRef } from 'react';
import { getConfig } from '../lib/config';

export type NotificationType = 'info' | 'success' | 'warning' | 'error';

export interface Notification {
  id: string;
  type: NotificationType;
  title: string;
  message: string;
  timestamp: number;
  read: boolean;
}

const STORAGE_KEY = 'seldon_notifications';

function loadNotifications(): Notification[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function persistNotifications(notifications: Notification[]) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(notifications));
}

let nextId = 0;
function genId(): string {
  return `n-${Date.now()}-${nextId++}`;
}

export function useNotifications() {
  const [notifications, setNotifications] = useState<Notification[]>(loadNotifications);
  const [toastQueue, setToastQueue] = useState<Notification[]>([]);
  const timersRef = useRef<Map<string, ReturnType<typeof setTimeout>>>(new Map());

  useEffect(() => {
    persistNotifications(notifications);
  }, [notifications]);

  const addNotification = useCallback(
    (type: NotificationType, title: string, message: string, showToast = true) => {
      const notification: Notification = {
        id: genId(),
        type,
        title,
        message,
        timestamp: Date.now(),
        read: false,
      };

      setNotifications((prev) => {
        const next = [notification, ...prev].slice(0, getConfig().maxNotifications);
        return next;
      });

      if (showToast) {
        setToastQueue((prev) => [...prev, notification]);
        const timer = setTimeout(() => {
          setToastQueue((prev) => prev.filter((n) => n.id !== notification.id));
          timersRef.current.delete(notification.id);
        }, getConfig().toastDismissMs);
        timersRef.current.set(notification.id, timer);
      }

      return notification.id;
    },
    [],
  );

  const dismissToast = useCallback((id: string) => {
    const timer = timersRef.current.get(id);
    if (timer) {
      clearTimeout(timer);
      timersRef.current.delete(id);
    }
    setToastQueue((prev) => prev.filter((n) => n.id !== id));
  }, []);

  const markAsRead = useCallback((id: string) => {
    setNotifications((prev) =>
      prev.map((n) => (n.id === id ? { ...n, read: true } : n)),
    );
  }, []);

  const markAllRead = useCallback(() => {
    setNotifications((prev) => prev.map((n) => ({ ...n, read: true })));
  }, []);

  const clearAll = useCallback(() => {
    setNotifications([]);
    setToastQueue([]);
    timersRef.current.forEach((t) => clearTimeout(t));
    timersRef.current.clear();
  }, []);

  const unreadCount = notifications.filter((n) => !n.read).length;

  useEffect(() => {
    return () => {
      timersRef.current.forEach((t) => clearTimeout(t));
    };
  }, []);

  return {
    notifications,
    toastQueue,
    unreadCount,
    addNotification,
    dismissToast,
    markAsRead,
    markAllRead,
    clearAll,
  };
}
