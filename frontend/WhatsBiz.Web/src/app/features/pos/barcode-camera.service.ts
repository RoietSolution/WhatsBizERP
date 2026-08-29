import { Injectable } from '@angular/core';
import { BarcodeFormat, BrowserMultiFormatReader, IScannerControls } from '@zxing/browser';
import { DecodeHintType, Result } from '@zxing/library';

export interface BarcodeCameraSession {
  readonly torchAvailable: boolean;
  stop(): void;
  setTorch(enabled: boolean): Promise<void>;
}

export interface BarcodeScanResult {
  readonly value: string;
  readonly barcodeType: string;
}

@Injectable({ providedIn: 'root' })
export class BarcodeCameraService {
  async start(
    video: HTMLVideoElement,
    detected: (result: BarcodeScanResult) => void,
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
        const value = result?.getText();
        if (value?.trim() && result)
          detected({ value, barcodeType: this.barcodeType(result.getBarcodeFormat()) });
      },
    );

    return this.session(video, controls);
  }

  private barcodeType(format: BarcodeFormat): string {
    switch (format) {
      case BarcodeFormat.EAN_13:
        return 'EAN13';
      case BarcodeFormat.EAN_8:
        return 'EAN8';
      case BarcodeFormat.UPC_A:
        return 'UPCA';
      case BarcodeFormat.UPC_E:
        return 'UPCE';
      case BarcodeFormat.CODE_128:
        return 'CODE128';
      case BarcodeFormat.CODE_39:
        return 'CODE39';
      case BarcodeFormat.QR_CODE:
        return 'QR';
      default:
        return 'CUSTOM';
    }
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
