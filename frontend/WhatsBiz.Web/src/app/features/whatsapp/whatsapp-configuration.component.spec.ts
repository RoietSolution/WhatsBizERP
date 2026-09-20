import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { FeatureService } from '../../core/services/feature.service';
import { WhatsAppApiService, WhatsAppConfiguration } from './whatsapp-api.service';
import { WhatsAppConfigurationComponent } from './whatsapp-configuration.component';

describe('WhatsAppConfigurationComponent LIVE retailer setup', () => {
  let api: jasmine.SpyObj<WhatsAppApiService>;
  let features: jasmine.SpyObj<FeatureService>;
  const originalFacebook = (window as unknown as { FB?: unknown }).FB;

  const liveConfig = (overrides: Partial<WhatsAppConfiguration> = {}): WhatsAppConfiguration => ({
    providerMode: 'LIVE', isEnabled: true, connectionStatus: 'NOT_CONFIGURED',
    hasAccessToken: false, hasWebhookVerifyToken: false, hasAppSecret: false,
    usesSharedPlatformCredentials: true, ...overrides,
  });

  async function create(owner: boolean, initial = liveConfig()): Promise<ComponentFixture<WhatsAppConfigurationComponent>> {
    api = jasmine.createSpyObj<WhatsAppApiService>('WhatsAppApiService', [
      'get', 'save', 'validate', 'completeOnboarding', 'onboardingConfig', 'diagnostics', 'sendTestMessage',
    ]);
    api.get.and.returnValue(of(initial));
    api.save.and.returnValue(of(initial));
    api.validate.and.returnValue(of({ succeeded: true, connectionStatus: 'CONNECTED', validatedAt: new Date().toISOString() }));
    api.completeOnboarding.and.returnValue(of({ succeeded: true, connectionStatus: 'CONNECTED', validatedAt: new Date().toISOString() }));
    api.diagnostics.and.returnValue(of({ webhookPath: '/api/whatsapp/webhook', tenantResolutionSucceeded: false, duplicateWebhookCount: 0 }));
    features = jasmine.createSpyObj<FeatureService>('FeatureService', ['tenants']);
    features.tenants.and.returnValue(of([]));

    await TestBed.configureTestingModule({
      imports: [WhatsAppConfigurationComponent],
      providers: [
        { provide: WhatsAppApiService, useValue: api },
        { provide: FeatureService, useValue: features },
        { provide: ActivatedRoute, useValue: { snapshot: { data: { platform: owner } } } },
        provideRouter([]),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(WhatsAppConfigurationComponent);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    if (originalFacebook === undefined) delete (window as unknown as { FB?: unknown }).FB;
    else (window as unknown as { FB?: unknown }).FB = originalFacebook;
    TestBed.resetTestingModule();
  });

  function installFacebookLogin(response: { authResponse?: { code?: string } } = { authResponse: { code: 'auth-code' } }) {
    const login = jasmine.createSpy('login').and.callFake((callback: (value: typeof response) => void) => callback(response));
    (window as unknown as { FB: { login: typeof login } }).FB = { login };
    return login;
  }

  it('shows LIVE retailer Connect action without manual Meta credential fields', async () => {
    const fixture = await create(false);
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('NOT CONNECTED');
    expect(text).toContain('Connect WhatsApp Business');
    expect(text).not.toContain('Access token');
    expect(text).not.toContain('Phone Number ID');
    expect(text).not.toContain('Meta app secret');
    expect(text).not.toContain('Provider mode');
  });

  it('shows MOCK as read-only demo mode with no retailer configuration actions', async () => {
    const fixture = await create(false, liveConfig({
      providerMode: 'MOCK', connectionStatus: 'CONFIGURED', usesSharedPlatformCredentials: false,
    }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('MOCK mode is for demos and testing');
    expect(text).toContain('CONFIGURED');
    expect(text).not.toContain('Enable this connection');
    expect(text).not.toContain('Save Configuration');
    expect(text).not.toContain('Connect WhatsApp Business');
    expect(text).not.toContain('Meta App ID');
  });

  it('shows LIVE disabled notice without allowing onboarding or configuration', async () => {
    const fixture = await create(false, liveConfig({ isEnabled: false, connectionStatus: 'DISABLED' }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('not been enabled for this retailer');
    expect(text).not.toContain('Connect WhatsApp Business');
    expect(text).not.toContain('Enable this connection');
    expect(text).not.toContain('Save Configuration');
  });

  it('lets Application Owner save LIVE enabled without connection credentials and leaves it disconnected', async () => {
    const fixture = await create(true);
    const component = fixture.componentInstance;
    component.tenantId = 'retailer-tenant';
    component.model.set(liveConfig());
    component.save();

    expect(api.save).toHaveBeenCalledWith('retailer-tenant', jasmine.objectContaining({
      providerMode: 'LIVE', isEnabled: true, whatsAppBusinessAccountId: '', phoneNumberId: '', accessToken: undefined,
    }));
    expect(component.model().connectionStatus).toBe('NOT_CONFIGURED');
    expect(component.model().connectionStatus).not.toBe('CONNECTED');
  });

  it('masks the connected retailer phone number', async () => {
    const fixture = await create(false, liveConfig({
      connectionStatus: 'CONNECTED', displayPhoneNumber: '+1 555 123 4567',
      businessDisplayName: 'Retail Store', lastValidatedDate: new Date().toISOString(),
    }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Retail Store');
    expect(text).toContain('Meta WhatsApp Cloud API');
    expect(text).not.toContain('+1 555 123 4567');
    expect(text).toContain('67');
  });

  it('preserves META_TEST technical fields and test actions', async () => {
    const fixture = await create(false, liveConfig({
      providerMode: 'META_TEST', isEnabled: false, connectionStatus: 'CONFIGURED',
      usesSharedPlatformCredentials: false,
    }));
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Meta App ID');
    expect(text).toContain('WhatsApp Business Account ID');
    expect(text).toContain('Access token');
    expect(text).toContain('Send Test Message');
    expect(text).toContain('Validate Meta Connection');
    expect(text).toContain('Save Configuration');
  });

  it('launches the Meta Builder Coexistence flow with the required extras', async () => {
    const fixture = await create(false);
    api.onboardingConfig.and.returnValue(of({ enabled: true, appId: '123', configurationId: '456', graphApiVersion: 'v24.0' }));
    const login = installFacebookLogin();

    fixture.componentInstance.connectLive();

    const options = login.calls.mostRecent().args[1] as { config_id: string; response_type: string; override_default_response_type: boolean; extras: Record<string, unknown> };
    expect(options.config_id).toBe('456');
    expect(options.response_type).toBe('code');
    expect(options.override_default_response_type).toBeTrue();
    expect(options.extras).toEqual({ setup: {}, featureType: 'whatsapp_business_app_onboarding', sessionInfoVersion: '3', version: 'v4' });
  });

  it('keeps standard FINISH completion supported', async () => {
    const fixture = await create(false);
    api.onboardingConfig.and.returnValue(of({ enabled: true, appId: '123', configurationId: '456', graphApiVersion: 'v24.0' }));
    const login = installFacebookLogin({});
    fixture.componentInstance.connectLive();
    window.dispatchEvent(new MessageEvent('message', {
      origin: 'https://www.facebook.com',
      data: JSON.stringify({ event: 'FINISH', data: { waba_id: 'waba-1', phone_number_id: 'phone-1' } }),
    }));
    login.calls.mostRecent().args[0]({ authResponse: { code: 'auth-code' } });

    expect(api.completeOnboarding).toHaveBeenCalledWith(jasmine.objectContaining({
      authorizationCode: 'auth-code', whatsAppBusinessAccountId: 'waba-1', phoneNumberId: 'phone-1',
    }));
  });

  it('accepts Coexistence completion without assuming phone_number_id exists', async () => {
    const fixture = await create(false);
    api.onboardingConfig.and.returnValue(of({ enabled: true, appId: '123', configurationId: '456', graphApiVersion: 'v24.0' }));
    const login = installFacebookLogin({});
    fixture.componentInstance.connectLive();
    window.dispatchEvent(new MessageEvent('message', {
      origin: 'https://www.facebook.com',
      data: JSON.stringify({ event: 'FINISH_WHATSAPP_BUSINESS_APP_ONBOARDING', data: { waba_id: 'waba-1' } }),
    }));
    login.calls.mostRecent().args[0]({ authResponse: { code: 'auth-code' } });

    expect(api.completeOnboarding).toHaveBeenCalledWith(jasmine.objectContaining({
      authorizationCode: 'auth-code', whatsAppBusinessAccountId: 'waba-1', phoneNumberId: undefined,
    }));
  });
});
