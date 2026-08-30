import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ProductAddedSoundService {
  private context?: AudioContext;
  private nextStartAt = 0;

  unlock(): void {
    const context = this.audioContext();
    if (context?.state === 'suspended') void context.resume().catch(() => undefined);
  }

  play(): void {
    const context = this.audioContext();
    if (!context) return;
    const sound = () => {
      const start = Math.max(context.currentTime, this.nextStartAt);
      const stop = start + 0.09;
      this.nextStartAt = stop + 0.035;
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      oscillator.type = 'square';
      oscillator.frequency.setValueAtTime(1760, start);
      oscillator.frequency.exponentialRampToValueAtTime(1320, stop);
      gain.gain.setValueAtTime(0.0001, start);
      gain.gain.linearRampToValueAtTime(0.32, start + 0.004);
      gain.gain.exponentialRampToValueAtTime(0.0001, stop);
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start(start);
      oscillator.stop(stop);
    };
    if (context.state === 'suspended') void context.resume().then(sound).catch(() => undefined);
    else sound();
  }

  private audioContext(): AudioContext | undefined {
    if (this.context) return this.context;
    try {
      this.context = new AudioContext();
      return this.context;
    } catch {
      return undefined;
    }
  }
}
