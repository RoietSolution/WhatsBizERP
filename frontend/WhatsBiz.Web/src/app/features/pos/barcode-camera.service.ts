import { Injectable } from '@angular/core';
import { BarcodeFormat, BrowserMultiFormatReader, IScannerControls } from '@zxing/browser';
import { DecodeHintType, Result } from '@zxing/library';

export interface BarcodeCameraSession {
  readonly torchAvailable: boolean;
  stop(): void;
  setTorch(enabled: boolean): Promise<void>;
}

@Injectable({ providedIn: 'root' })
export class BarcodeCameraService {
  async start(
    video: HTMLVideoElement,
    detected: (value: string) => void,
  ): Promise<BarcodeCameraSession> {
    if (!window.isSecureContext || !navigator.mediaDevices?.getUserMedia) {
      throw new DOMException('Camera scanning requires HTTPS and a compatible browser.', 'NotSupportedError');
    }

    const hints = new Map<DecodeHintType, unknown>();
    hints.set(DecodeHintType.POSSIBLE_FORMATS, [
      BarcodeFormat.EAN_13,
      BarcodeFormat.EAN_8,
      BarcodeFormat.UPC_A,
      BarcodeFormat.UPC_E,
      BarcodeFormat.CODE_128,
      BarcodeFormat.CODE_39,
      BarcodeFormat.QR_CODE,
    ]);
    hints.set(DecodeHintType.TRY_HARDER, true);

    const reader = new BrowserMultiFormatReader(hints);
    const controls = await reader.decodeFromConstraints(
      { audio: false, video: { facingMode: { ideal: 'environment' } } },
      video,
      (result: Result | undefined) => {
        const value = result?.getText().trim();
        if (value) detected(value);
      },
    );

    return this.session(video, controls);
  }

  private session(video: HTMLVideoElement, controls: IScannerControls): BarcodeCameraSession {
    let stopped = false;
    const stop = () => {
      if (stopped) return;
      stopped = true;
      controls.stop();
      const stream = video.srcObject;
      if (stream instanceof MediaStream) stream.getTracks().forEach((track) => track.stop());
      video.srcObject = null;
    };
    return {
      torchAvailable: typeof controls.switchTorch === 'function',
      stop,
      setTorch: async (enabled: boolean) => {
        if (!controls.switchTorch) return;
        await controls.switchTorch(enabled);
      },
    };
  }
}
