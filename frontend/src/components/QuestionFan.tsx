import React, { useState, useRef, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { staticQuestions, type StaticQuestion } from '../data/staticQuestions';
import type { ModuleType } from '../App';

interface QuestionFanProps {
  module: ModuleType;
  onSelect: (question: string) => void;
  disabled?: boolean;
  selectedText?: string;
}

export function QuestionFan({ module, onSelect, disabled, selectedText }: QuestionFanProps) {
  const [isOpen, setIsOpen] = useState(false);
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null);
  const [panelMetrics, setPanelMetrics] = useState<{ bottom: number } | null>(null);
  const fanRef = useRef<HTMLDivElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const questions = staticQuestions[module as keyof typeof staticQuestions] ?? staticQuestions['philosophy'];

  const handleToggle = useCallback(() => {
    if (!disabled) {
      setIsOpen((prev) => !prev);
      setSelectedIndex(null);
    }
  }, [disabled]);

  const handleSelect = useCallback((question: StaticQuestion, index: number) => {
    setSelectedIndex(index);
    onSelect(question.text);
    setIsOpen(false);
  }, [onSelect]);

  const handleKeyDown = useCallback((e: React.KeyboardEvent) => {
    if (!isOpen) return;
    switch (e.key) {
      case 'ArrowUp':
        e.preventDefault();
        setSelectedIndex((prev) => (prev === null || prev === 0 ? questions.length - 1 : prev - 1));
        break;
      case 'ArrowDown':
        e.preventDefault();
        setSelectedIndex((prev) => (prev === null || prev === questions.length - 1 ? 0 : prev + 1));
        break;
      case 'Enter':
        e.preventDefault();
        if (selectedIndex !== null) handleSelect(questions[selectedIndex], selectedIndex);
        break;
      case 'Escape':
        setIsOpen(false);
        setSelectedIndex(null);
        break;
    }
  }, [isOpen, selectedIndex, questions, handleSelect]);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      const target = e.target as Node;
      if (
        fanRef.current &&
        !fanRef.current.contains(target) &&
        panelRef.current &&
        !panelRef.current.contains(target)
      ) {
        setIsOpen(false);
        setSelectedIndex(null);
      }
    };
    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [isOpen]);

  // Measure the trigger's on-screen position so the portaled panel (rendered
  // into document.body, escaping the transformed bottom bar) opens just above
  // it, centered in the viewport and spanning the available horizontal space.
  const measurePanel = useCallback(() => {
    if (!fanRef.current) return;
    const rect = fanRef.current.getBoundingClientRect();
    setPanelMetrics({ bottom: window.innerHeight - rect.top + 8 });
  }, []);

  useEffect(() => {
    if (isOpen) {
      measurePanel();
      window.addEventListener('resize', measurePanel);
      return () => window.removeEventListener('resize', measurePanel);
    }
  }, [isOpen, measurePanel]);

  // Layout: two columns of equal height, each column is a vertical list.
  // Questions are distributed left-col / right-col so both sides fill evenly.
  const mid = Math.ceil(questions.length / 2);
  const leftCol = questions.slice(0, mid);
  const rightCol = questions.slice(mid);

  return (
    <div ref={fanRef} className="relative" onKeyDown={handleKeyDown}>
      {/* Trigger */}
      <button
        type="button"
        onClick={handleToggle}
        disabled={disabled}
        className="flex items-center justify-between w-full text-left gap-2 text-[13px] font-serif transition-colors disabled:opacity-50 disabled:cursor-not-allowed px-1 cursor-pointer"
        style={{ color: 'var(--pr-text)' }}
      >
        <span className="truncate">
          {selectedText || 'Select a question from the fan...'}
        </span>
        <svg
          className={`w-4 h-4 shrink-0 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`}
          fill="none" stroke="currentColor" viewBox="0 0 24 24"
        >
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 9l-7 7-7-7" />
        </svg>
      </button>

      {/* Dropdown panel - portaled to document.body, centered on viewport and
          spanning the available horizontal width (capped + centered on huge screens) */}
      {isOpen && panelMetrics && createPortal(
        <div
          ref={panelRef}
          className="fixed z-50 rounded-xl shadow-2xl overflow-hidden animate-fade-in-up pr-glass-surface"
          style={{
            left: 16,
            right: 16,
            marginLeft: 'auto',
            marginRight: 'auto',
            maxWidth: 1100,
            bottom: panelMetrics.bottom,
            backgroundColor: 'var(--pr-surface)',
            border: '1px solid var(--pr-border)',
          }}
        >
          {/* Header */}
          <div className="flex items-center justify-between px-4 py-2 border-b" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-surface-raised)' }}>
            <span className="font-mono text-[10px] uppercase tracking-widest" style={{ color: 'var(--pr-text-muted)' }}>
              {module} · select a question
            </span>
            <button
              type="button"
              onClick={() => setIsOpen(false)}
              className="transition-colors cursor-pointer"
              style={{ color: 'var(--pr-text-dim)' }}
            >
              <svg className="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M18 6L6 18M6 6l12 12" />
              </svg>
            </button>
          </div>

          {/* Two-column grid */}
          <div className="grid grid-cols-2 divide-x" style={{ borderColor: 'var(--pr-border)', backgroundColor: 'var(--pr-bg)' }}>
            {[leftCol, rightCol].map((col, colIdx) => (
              <div key={colIdx} className="flex flex-col">
                {col.map((question) => {
                  const index = questions.indexOf(question);
                  const isSelected = selectedIndex === index;
                  return (
                    <button
                      key={question.id}
                      type="button"
                      onClick={() => handleSelect(question, index)}
                      onMouseEnter={() => setSelectedIndex(index)}
                      onMouseLeave={() => setSelectedIndex(null)}
                      className="text-left px-4 py-2.5 text-[13px] font-serif transition-colors duration-100 border-b last:border-b-0 cursor-pointer"
                      style={{
                        borderColor: 'var(--pr-border)',
                        backgroundColor: isSelected ? 'rgba(120,89,26,0.15)' : 'transparent',
                        color: isSelected ? 'var(--pr-role-proposer)' : 'var(--pr-text)',
                      }}
                    >
                      {question.text}
                    </button>
                  );
                })}
              </div>
            ))}
          </div>
        </div>,
        document.body
      )}
    </div>
  );
}
