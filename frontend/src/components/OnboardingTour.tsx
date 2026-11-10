import { useState, useEffect, useCallback } from 'react';

const TOUR_COMPLETE_KEY = 'seldon_onboarding_complete';

interface TourStep {
  id: number;
  title: string;
  description: string;
  targetSelector?: string;
  position?: 'top' | 'bottom' | 'left' | 'right' | 'center';
}

const TOUR_STEPS: TourStep[] = [
  {
    id: 1,
    title: 'Welcome to Seldon Lattice',
    description:
      'Seldon Lattice is a dialectical reasoning engine that runs multi-agent philosophical debates to help you explore complex questions from multiple perspectives.',
    position: 'center',
  },
  {
    id: 2,
    title: 'Choose Your Framework',
    description:
      'Two modules are available: Philosophical Reasoning (Proposer, Skeptic, Mediator) for formal logic debates, and Research Orchestration, which chains the philosophy module with any custom modules created at runtime.',
    targetSelector: '[data-tour="module-badge"]',
    position: 'bottom',
  },
  {
    id: 3,
    title: 'Start Your First Debate',
    description:
      'Enter a theme or question in the input bar, choose a recursion depth, and press Enter or click the arrow. The agents will debate and produce a synthesis.',
    targetSelector: '[data-tour="theme-input"]',
    position: 'top',
  },
  {
    id: 4,
    title: 'Read the Results',
    description:
      'Each debate produces a synthesis with quality metrics. Quality gates ensure coherence - rejected candidates trigger a retry. The Merkle DAG stores an immutable audit trail.',
    targetSelector: '[data-tour="mindmap"]',
    position: 'left',
  },
  {
    id: 5,
    title: 'Explore the Graph',
    description:
      'The MindMap visualizes the debate as a graph. Click nodes to inspect, pan/zoom to navigate, switch to Index view for a linear transcript, or Inspector for node details and sigils.',
    targetSelector: '[data-tour="view-switcher"]',
    position: 'bottom',
  },
  {
    id: 6,
    title: 'Export & Share',
    description:
      'Export transcripts as Markdown, JSON, or printable reports with Merkle proofs. Use the notification bell for debate events, and keyboard shortcuts (?) for power-user workflows.',
    targetSelector: '[data-tour="export-btn"]',
    position: 'top',
  },
];

interface OnboardingTourProps {
  isOpen: boolean;
  onClose: () => void;
  onComplete: () => void;
}

export default function OnboardingTour({ isOpen, onClose, onComplete }: OnboardingTourProps) {
  const [currentStep, setCurrentStep] = useState(0);
  const [skipAnimation, setSkipAnimation] = useState(false);

  const step = TOUR_STEPS[currentStep];
  const isLastStep = currentStep === TOUR_STEPS.length - 1;

  const nextStep = useCallback(() => {
    setSkipAnimation(true);
    setTimeout(() => {
      if (isLastStep) {
        onComplete();
      } else {
        setCurrentStep((s) => s + 1);
        setSkipAnimation(false);
      }
    }, 150);
  }, [isLastStep, onComplete]);

  const prevStep = useCallback(() => {
    if (currentStep > 0) {
      setSkipAnimation(true);
      setTimeout(() => {
        setCurrentStep((s) => s - 1);
        setSkipAnimation(false);
      }, 150);
    }
  }, [currentStep]);

  const handleSkip = useCallback(() => {
    onComplete();
  }, [onComplete]);

  useEffect(() => {
    if (isOpen) {
      setCurrentStep(0);
      setSkipAnimation(false);
      document.body.style.overflow = 'hidden';
    }
    return () => {
      document.body.style.overflow = '';
    };
  }, [isOpen]);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      if (e.key === 'Enter' || e.key === 'ArrowRight' || e.key === ' ') nextStep();
      if (e.key === 'ArrowLeft') prevStep();
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [onClose, nextStep, prevStep]);

  if (!isOpen) return null;

  const progress = ((currentStep + 1) / TOUR_STEPS.length) * 100;

  return (
    <div className="fixed inset-0 z-200 flex items-center justify-center p-4" style={{ backgroundColor: 'rgba(15, 12, 8, 0.5)', backdropFilter: 'blur(6px)' }}>
      {/* Spotlight overlay for targeted steps */}
      {step.targetSelector && !skipAnimation && (
        <div className="fixed inset-0 pointer-events-none">
          <div className="absolute inset-0" style={{ backgroundColor: 'rgba(15, 12, 8, 0.75)' }} />
          <div
            className="absolute pointer-events-auto"
            style={{
              // Position will be set by JS
              transition: 'all 0.3s ease-out',
            }}
            id="tour-spotlight"
          />
        </div>
      )}

      {/* Main tour card */}
      <div
        className={`relative w-full max-w-lg p-8 rounded-2xl animate-fade-in-up pr-glass-surface ${skipAnimation ? 'opacity-0 scale-95' : ''}`}
        style={{
          transition: 'opacity 0.15s ease-out, transform 0.15s ease-out',
          zIndex: 201,
          backgroundColor: 'var(--pr-surface)',
          border: '1px solid var(--pr-border)',
          boxShadow: '0 24px 60px rgba(0,0,0,0.25)',
        }}
      >
        {/* Progress bar */}
        <div className="mb-6 h-1.5 rounded-full overflow-hidden" style={{ backgroundColor: 'var(--pr-surface-raised)' }}>
          <div
            className="h-full transition-all duration-300 ease-out"
            style={{ backgroundColor: 'var(--pr-role-proposer)', width: `${progress}%` }}
          />
        </div>

        {/* Step indicator */}
        <div className="flex items-center justify-between mb-4">
          <span className="text-[13px] font-mono uppercase tracking-wider" style={{ color: 'var(--pr-text-muted)' }}>
            Step {currentStep + 1} of {TOUR_STEPS.length}
          </span>
          <button
            onClick={handleSkip}
            className="text-[13px] font-mono uppercase tracking-wider cursor-pointer"
            style={{ color: 'var(--pr-text-dim)', background: 'none', border: 'none' }}
          >
            Skip
          </button>
        </div>

        {/* Content */}
        <div className="space-y-4">
          <div className="text-center">
            <h2 className="text-3xl font-semibold mb-3" style={{ fontFamily: 'var(--pr-font-display)', color: 'var(--pr-text)' }}>
              {step.title}
            </h2>
            <p className="text-[15px] leading-relaxed" style={{ color: 'var(--pr-text-muted)' }}>
              {step.description}
            </p>
          </div>

          {/* Navigation */}
          <div className="flex items-center justify-between pt-4 border-t" style={{ borderColor: 'var(--pr-border)' }}>
            <button
              onClick={prevStep}
              disabled={currentStep === 0}
              className="flex items-center gap-2 px-4 py-2 rounded-lg border font-mono text-[13px] uppercase tracking-wider cursor-pointer transition-colors"
              style={{
                borderColor: 'var(--pr-border)',
                color: currentStep === 0 ? 'var(--pr-text-dim)' : 'var(--pr-text)',
                backgroundColor: 'var(--pr-surface-raised)',
                opacity: currentStep === 0 ? 0.4 : 1,
              }}
            >
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M15 18l-6-6 6-6" />
              </svg>
              Back
            </button>

            <div className="flex items-center gap-2">
              <span className="text-[13px] font-mono" style={{ color: 'var(--pr-text-dim)' }}>
                  Enter / →
                </span>
              <button
                onClick={nextStep}
                className="flex items-center gap-2 px-4 py-2 rounded-lg font-mono text-[13px] uppercase font-semibold tracking-wider cursor-pointer transition-all"
                style={{
                  backgroundColor: 'var(--pr-role-proposer)',
                  color: 'var(--pr-bg)',
                  border: 'none',
                }}
              >
                {isLastStep ? 'Finish' : 'Next'}
                {!isLastStep && (
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <path d="M9 18l6-6-6-6" />
                  </svg>
                )}
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// Hook for managing onboarding state
export function useOnboarding() {
  const [isOpen, setIsOpen] = useState(false);
  const [hasCompleted, setHasCompleted] = useState(() => {
    try {
      return localStorage.getItem(TOUR_COMPLETE_KEY) === 'true';
    } catch {
      return false;
    }
  });

  const startTour = useCallback(() => {
    setIsOpen(true);
  }, []);

  const completeTour = useCallback(() => {
    setIsOpen(false);
    setHasCompleted(true);
    try {
      localStorage.setItem(TOUR_COMPLETE_KEY, 'true');
    } catch {}
  }, []);

  const resetTour = useCallback(() => {
    setHasCompleted(false);
    try {
      localStorage.removeItem(TOUR_COMPLETE_KEY);
    } catch {}
  }, []);

  const shouldShow = !hasCompleted;

  return {
    isOpen,
    shouldShow,
    startTour,
    completeTour,
    resetTour,
    setIsOpen,
  };
}