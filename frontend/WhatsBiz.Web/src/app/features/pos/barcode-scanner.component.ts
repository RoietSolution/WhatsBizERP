import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  NgZone,
  OnDestroy,
  Output,
  ViewChild,
  signal,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import {
  BarcodeCameraService,
  BarcodeCameraSession,
  BarcodeScanResult,
} from './barcode-camera.service';

@Component({
  selector: 'app-barcode-scanner',
  imports: [MatButtonModule],
  templateUrl: './barcode-scanner.component.html',
  styleUrl: './barcode-scanner.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BarcodeScannerComponent implements AfterViewInit, OnDestroy {
  @ViewChild('preview', { static: true }) preview!: ElementRef<HTMLVideoElement>;
  @Input() feedback = '';
  @Output() detected = new EventEmitter<string>();
  @Output() scanned = new EventEmitter<BarcodeScanResult>();
  @Output() closed = new EventEmitter<void>();

  readonly state = signal('Starting camera...');
  readonly error = signal('');
  readonly torchAvailable = signal(false);
  readonly torchOn = signal(false);
  private readonly seen = new Map<string, number>();
  private session?: BarcodeCameraSession;
  private destroyed = false;
  static readonly duplicateCooldownMs = 1400;

  constructor(
    private readonly camera: BarcodeCameraService,
    private readonly zone: NgZone,
  ) {}

  async ngAfterViewInit() {
    try {
      const session = await this.camera.start(this.preview.nativeElement, (result) =>
        this.zone.run(() => this.accept(result.value, Date.now(), result.barcodeType)),
      );
      if (this.destroyed) {
        session.stop();
        return;
      }
      this.session = session;
      this.torchAvailable.set(session.torchAvailable);
      this.state.set('Point the camera at a barcode');
    } catch (reason) {
      this.error.set(this.cameraError(reason));
      this.state.set('Camera unavailable');
    }
  }

  accept(value: string, now = Date.now(), barcodeType = 'CUSTOM') {
    const barcode = value;
    if (!barcode?.trim()) return;
    const lastSeen = this.seen.get(barcode);
    if (lastSeen !== undefined && now - lastSeen < BarcodeScannerComponent.duplicateCooldownMs) return;
    this.seen.set(barcode, now);
    const preview = barcode.length > 64 ? `${barcode.slice(0, 61)}...` : barcode;
    this.state.set(`Scanned ${preview}`);
    this.detected.emit(barcode);
    this.scanned.emit({ value: barcode, barcodeType });
  }

  async toggleTorch() {
    const enabled = !this.torchOn();
    try {
      await this.session?.setTorch(enabled);
      this.torchOn.set(enabled);
    } catch {
      this.error.set('Flashlight is not available on this camera.');
      this.torchAvailable.set(false);
    }
  }

  close() {
    this.stop();
    this.closed.emit();
  }

  ngOnDestroy() {
    this.destroyed = true;
    this.stop();
  }

  private stop() {
    this.session?.stop();
    this.session = undefined;
  }

  private cameraError(reason: unknown): string {
    const name = reason instanceof DOMException ? reason.name : '';
    if (name === 'NotAllowedError' || name === 'SecurityError')
      return 'Camera permission was denied. Allow camera access in browser settings and try again.';
    if (name === 'NotFoundError' || name === 'OverconstrainedError')
      return 'No usable camera was found on this device.';
    if (name === 'NotReadableError' || name === 'AbortError')
      return 'The camera is busy or could not be started. Close other camera apps and retry.';
    if (name === 'NotSupportedError')
      return 'Camera scanning requires a compatible browser over HTTPS.';
    return 'The camera could not be started. Check permission and try again.';
  }
}
