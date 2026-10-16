import { StorefrontNotificationSettingsComponent } from './storefront-notification-settings.component';

describe('StorefrontNotificationSettingsComponent validation', () => {
  function component(): StorefrontNotificationSettingsComponent {
    return new StorefrontNotificationSettingsComponent({} as never);
  }

  it('distinguishes an invalid notification recipient from Gmail credentials', () => {
    const value = component();
    value.settings.newOrderEmailNotificationsEnabled = true;
    value.settings.newOrderNotificationEmail = 'not-an-email';
    expect(value.notificationRecipientInvalid()).toBeTrue();
    expect(value.gmailAddressInvalid()).toBeFalse();
  });

  it('requires a Gmail address and App Password only when Gmail notifications are enabled', () => {
    const value = component();
    value.settings.automatedEmailMode = 'GMAIL';
    value.settings.newOrderEmailNotificationsEnabled = true;
    value.settings.newOrderNotificationEmail = 'orders@example.com';
    expect(value.gmailAddressMessage()).toBe('Enter your Gmail address.');
    expect(value.valid()).toBeFalse();
    value.settings.customSmtpUsername = 'shop@gmail.com';
    expect(value.passwordRequiredInvalid()).toBeTrue();
    value.settings.customSmtpPassword = 'app-password';
    expect(value.passwordRequiredInvalid()).toBeFalse();
    expect(value.valid()).toBeTrue();
  });

  it('keeps a saved password until replacement or removal is explicitly selected', () => {
    const value = component();
    value.settings.automatedEmailMode = 'GMAIL';
    value.settings.hasCustomSmtpPassword = true;
    value.settings.newOrderEmailNotificationsEnabled = true;
    value.settings.newOrderNotificationEmail = 'orders@example.com';
    value.settings.customSmtpUsername = 'shop@gmail.com';
    expect(value.valid()).toBeTrue();
    value.beginPasswordReplacement();
    expect(value.replacePassword).toBeTrue();
  });
});
