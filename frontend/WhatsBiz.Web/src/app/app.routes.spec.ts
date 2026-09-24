import { routes } from './app.routes';

describe('application routes', () => {
  it('redirects the bare root URL to the login page', () => {
    const authenticationRoute = routes[0];
    const rootRoute = authenticationRoute.children?.find(
      (route) => route.path === '' && route.pathMatch === 'full',
    );

    expect(rootRoute?.redirectTo).toBe('login');
  });

  it('provides a dedicated application-owner login and protected owner center', () => {
    const authenticationRoute = routes[0];
    const ownerLogin = authenticationRoute.children?.find(
      (route) => route.path === 'application-owner/login',
    );
    const mainRoute = routes.find((route) => route.canActivateChild);
    const ownerCenter = mainRoute?.children?.find((route) => route.path === 'application-owner');

    expect(ownerLogin?.data?.['portal']).toBe('application-owner');
    expect(ownerCenter?.data?.['role']).toBe('ApplicationOwner');
    expect(ownerCenter?.data?.['platform']).toBeTrue();
  });

  it('marks every application-owner route as platform-wide', () => {
    const mainRoute = routes.find((route) => route.canActivateChild);
    const ownerPaths = [
      'application-owner',
      'admin/features',
      'admin/demo-requests',
      'admin/whatsapp-platform',
      'application-owner/whatsapp-business',
      'application-owner/whatsapp-demo',
      'admin/backup',
      'admin/restore',
      'admin/audit',
      'admin/login-history',
      'admin/system-logs',
    ];

    for (const path of ownerPaths) {
      const route = mainRoute?.children?.find((candidate) => candidate.path === path);
      expect(route).withContext(`missing owner route ${path}`).toBeDefined();
      expect(route?.data?.['role']).withContext(path).toBe('ApplicationOwner');
      expect(route?.data?.['platform']).withContext(path).toBeTrue();
    }
  });

  it('protects WhatsApp usage billing with tenant permission and feature guards',()=>{
    const mainRoute=routes.find(route=>route.canActivateChild);
    const route=mainRoute?.children?.find(candidate=>candidate.path==='admin/whatsapp-usage-billing');
    expect(route).toBeDefined();
    expect(route?.data?.['permission']).toBe('admin.view');
    expect(route?.data?.['feature']).toBe('WHATSAPP_COMMERCE');
    expect(route?.canActivate?.length).toBe(2);
  });

  it('keeps payment settings platform-only and payments available to owner and retailer',()=>{
    const mainRoute=routes.find(route=>route.canActivateChild);
    const settings=mainRoute?.children?.find(candidate=>candidate.path==='application-owner/payment-settings');
    const ownerPayments=mainRoute?.children?.find(candidate=>candidate.path==='application-owner/payments');
    const payments=mainRoute?.children?.find(candidate=>candidate.path==='admin/payments');
    expect(mainRoute?.children?.find(candidate=>candidate.path==='admin/payment-settings')).toBeUndefined();
    expect(settings?.data?.['role']).toBe('ApplicationOwner');expect(settings?.data?.['platform']).toBeTrue();expect(settings?.data?.['permission']).toBe('feature.manage');
    expect(ownerPayments?.data?.['role']).toBe('ApplicationOwner');expect(ownerPayments?.data?.['platform']).toBeTrue();
    expect(payments?.data?.['permission']).toBe('payment.view');
    expect(payments?.data?.['feature']).toBe('WHATSAPP_COMMERCE');
    for(const route of [settings,ownerPayments,payments])expect(route?.canActivate?.length).toBe(2);
  });
});
