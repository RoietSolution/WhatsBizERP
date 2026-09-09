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
  });
});
