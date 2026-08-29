import { ElementRef, NgZone } from '@angular/core';
import { BarcodeCameraService, BarcodeCameraSession } from './barcode-camera.service';
import { BarcodeScannerComponent } from './barcode-scanner.component';

describe('BarcodeScannerComponent', () => {
  function setup(start: () => Promise<BarcodeCameraSession>) {
    const camera = jasmine.createSpyObj<BarcodeCameraService>('BarcodeCameraService', ['start']);
    camera.start.and.callFake(start);
    const zone = { run: <T>(callback: () => T) => callback() } as NgZone;
    const component = new BarcodeScannerComponent(camera, zone);
    component.preview = new ElementRef(document.createElement('video'));
    return { camera, component };
  }

  it('debounces repeated frames but permits an intentional repeat after the cooldown', () => {
    const { component } = setup(async () => session());
    const values: string[] = [];
    component.detected.subscribe((value) => values.push(value));

    component.accept('8901234567890', 1000);
    component.accept('8901234567890', 1200);
    component.accept('8901234567890', 2500);

    expect(values).toEqual(['8901234567890', '8901234567890']);
  });

  it('emits the exact QR value and detected type as inert data', () => {
    const { component } = setup(async () => session());
    const values: Array<{ value: string; barcodeType: string }> = [];
    component.scanned.subscribe((value) => values.push(value));

    component.accept(' https://manufacturer.example/item?id=ABC ', 1000, 'QR');

    expect(values).toEqual([
      { value: ' https://manufacturer.example/item?id=ABC ', barcodeType: 'QR' },
    ]);
  });

  it('stops and releases the scanner session when destroyed', async () => {
    const active = session();
    const { component } = setup(async () => active);
    await component.ngAfterViewInit();

    component.ngOnDestroy();

    expect(active.stop).toHaveBeenCalledTimes(1);
  });

  it('reports camera permission denial without throwing', async () => {
    const { component } = setup(async () => {
      throw new DOMException('Denied', 'NotAllowedError');
    });

    await component.ngAfterViewInit();

    expect(component.error()).toContain('permission was denied');
  });

  it('stops a late camera session when navigation destroys the component during startup', async () => {
    let resolve!: (value: BarcodeCameraSession) => void;
    const pending = new Promise<BarcodeCameraSession>((done) => (resolve = done));
    const { component } = setup(() => pending);
    const starting = component.ngAfterViewInit();
    component.ngOnDestroy();
    const late = session();
    resolve(late);

    await starting;

    expect(late.stop).toHaveBeenCalledTimes(1);
  });

  function session(): BarcodeCameraSession {
    return {
      torchAvailable: false,
      stop: jasmine.createSpy('stop'),
      setTorch: jasmine.createSpy('setTorch').and.resolveTo(),
    };
  }
});
