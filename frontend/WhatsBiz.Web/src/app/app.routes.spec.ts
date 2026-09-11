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
});
