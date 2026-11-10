import { useEffect, useRef } from 'react';
import { getConfig } from '../lib/config';

interface CosmicBackdropCanvasProps {
  width?: number;
  height?: number;
}

export default function CosmicBackdropCanvas({ width, height }: CosmicBackdropCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    let animationFrameId: number;
    let time = 0;

    // Cache gradients and resize dimensions to prevent allocating GPU textures 60x/sec
    let bgGrad: CanvasGradient | null = null;
    let nebulaGrad1: CanvasGradient | null = null;
    let nebulaGrad2: CanvasGradient | null = null;

    const isLight = () => {
      const t = document.documentElement.getAttribute('data-theme');
      return t === 'light' || t === 'sol' || t === 'albedo';
    };

    const createGradients = (w: number, h: number) => {
      const light = isLight();
      bgGrad = ctx.createRadialGradient(w / 2, h / 2, 50, w / 2, h / 2, Math.max(w, h));
      if (light) {
        bgGrad.addColorStop(0, '#ffffff');
        bgGrad.addColorStop(0.5, '#f8f6f0');
        bgGrad.addColorStop(1, '#f2ece1');
      } else {
        bgGrad.addColorStop(0, '#0a0d18');
        bgGrad.addColorStop(0.5, '#080a0f');
        bgGrad.addColorStop(1, '#020306');
      }

      nebulaGrad1 = ctx.createRadialGradient(w * 0.3, h * 0.4, 0, w * 0.3, h * 0.4, 300);
      nebulaGrad1.addColorStop(0, light ? 'rgba(74, 144, 226, 0.04)' : 'rgba(74, 144, 226, 0.08)');
      nebulaGrad1.addColorStop(1, 'transparent');

      nebulaGrad2 = ctx.createRadialGradient(w * 0.7, h * 0.6, 0, w * 0.7, h * 0.6, 350);
      nebulaGrad2.addColorStop(0, light ? 'rgba(212, 175, 55, 0.05)' : 'rgba(212, 175, 55, 0.07)');
      nebulaGrad2.addColorStop(1, 'transparent');
    };

    const resize = () => {
      if (!canvas.parentElement) return;
      canvas.width = canvas.parentElement.clientWidth;
      canvas.height = canvas.parentElement.clientHeight;
      createGradients(canvas.width, canvas.height);
    };

    const handleThemeChange = () => {
      createGradients(canvas.width, canvas.height);
    };

    window.addEventListener('resize', resize);
    window.addEventListener('seldon-theme-change', handleThemeChange);
    resize();

    // Create persistent cosmic stars - fewer for perf
    const stars = Array.from({ length: getConfig().backdropStars }, () => ({
      x: Math.random() * 2000,
      y: Math.random() * 2000,
      size: Math.random() * 1.5 + 0.3,
      alpha: Math.random() * 0.6 + 0.1,
      pulseSpeed: Math.random() * 0.02 + 0.1,
      hue: Math.random() > 0.5 ? 45 : 210, // Gold or Cyan
    }));

    let isVisible = true;
    const handleVisibility = () => {
      isVisible = !document.hidden;
      if (isVisible) {
        animationFrameId = requestAnimationFrame(render);
      } else {
        cancelAnimationFrame(animationFrameId);
      }
    };
    document.addEventListener('visibilitychange', handleVisibility);

    // Throttle to configured FPS - stars twinkle slowly, no visual difference
    const FRAME_INTERVAL = 1000 / getConfig().backdropTargetFps;
    let lastRender = 0;

    const render = (timestamp: number) => {
      if (!isVisible) return;
      if (timestamp - lastRender < FRAME_INTERVAL) {
        animationFrameId = requestAnimationFrame(render);
        return;
      }
      lastRender = timestamp;
      time += getConfig().backdropTimeStep;
      const w = canvas.width;
      const h = canvas.height;

      // Dark space background gradient (cached)
      if (bgGrad) {
        ctx.fillStyle = bgGrad;
        ctx.fillRect(0, 0, w, h);
      }

      // Render cosmic nebula ambient glows (cached)
      if (nebulaGrad1) {
        ctx.fillStyle = nebulaGrad1;
        ctx.fillRect(0, 0, w, h);
      }
      if (nebulaGrad2) {
        ctx.fillStyle = nebulaGrad2;
        ctx.fillRect(0, 0, w, h);
      }

      // Render cosmic star field - batch by hue to minimize fillStyle changes
      const goldStars: typeof stars = [];
      const cyanStars: typeof stars = [];
      for (const star of stars) {
        (star.hue === 45 ? goldStars : cyanStars).push(star);
      }

      const drawBatch = (batch: typeof stars, r: number, g: number, b: number) => {
        if (batch.length === 0) return;
        ctx.fillStyle = `rgba(${r},${g},${b},0.5)`;
        ctx.beginPath();
        for (const star of batch) {
          const screenX = (star.x + w / 2 + Math.sin(time * 0.1) * 10) % w;
          const screenY = (star.y + h / 2 + Math.cos(time * 0.1) * 10) % h;
          const finalX = screenX < 0 ? screenX + w : screenX;
          const finalY = screenY < 0 ? screenY + h : screenY;
          ctx.moveTo(finalX + star.size, finalY);
          ctx.arc(finalX, finalY, star.size, 0, Math.PI * 2);
        }
        ctx.globalAlpha = 0.6;
        ctx.fill();
        ctx.globalAlpha = 1;
      };

      drawBatch(goldStars, 255, 215, 0);
      drawBatch(cyanStars, 100, 200, 255);

      animationFrameId = requestAnimationFrame(render);
    };

    render(0);

    return () => {
      window.removeEventListener('resize', resize);
      document.removeEventListener('visibilitychange', handleVisibility);
      cancelAnimationFrame(animationFrameId);
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      className="absolute inset-0 pointer-events-none z-0"
      style={{ width: width ? `${width}px` : '100%', height: height ? `${height}px` : '100%' }}
    />
  );
}
