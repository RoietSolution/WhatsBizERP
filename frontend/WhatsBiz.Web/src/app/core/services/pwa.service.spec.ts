import { TestBed } from '@angular/core/testing';
import { EMPTY } from 'rxjs';
import { SwUpdate } from '@angular/service-worker';
import { PwaService } from './pwa.service';

describe('PwaService', () => {
  let service: PwaService;
  const updates = { isEnabled: false, versionUpdates: EMPTY, activateUpdate: jasmine.createSpy('activateUpdate') };

  beforeEach(() => {
    localStorage.removeItem('whatsbiz.pwa.install-dismissed-until');
    TestBed.configureTestingModule({ providers: [{ provide: SwUpdate, useValue: updates }] });
    service = TestBed.inject(PwaService);
  });

  it('tracks connectivity without changing application data state', () => {
    window.dispatchEvent(new Event('offline'));
    expect(service.offline()).toBeTrue();
    window.dispatchEvent(new Event('online'));
    expect(service.offline()).toBeFalse();
  });

  it('exposes a deferred install prompt and clears it after dismissal', () => {
    const event = new Event('beforeinstallprompt', { cancelable: true }) as Event & {
      prompt: () => Promise<void>;
      userChoice: Promise<{ outcome: 'accepted' | 'dismissed'; platform: string }>;
    };
    event.prompt = async () => undefined;
    event.userChoice = Promise.resolve({ outcome: 'dismissed', platform: 'web' });
    window.dispatchEvent(event);
    expect(service.installAvailable()).toBeTrue();
    service.dismissInstall();
    expect(service.installAvailable()).toBeFalse();
  });
});