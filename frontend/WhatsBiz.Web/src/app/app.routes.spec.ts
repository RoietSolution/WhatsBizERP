import { routes } from './app.routes';

describe('application routes', () => {
  it('redirects the bare root URL to the login page', () => {
    const authenticationRoute = routes[0];
    const rootRoute = authenticationRoute.children?.find(
      (route) => route.path === '' && route.pathMatch === 'full',
    );

    expect(rootRoute?.redirectTo).toBe('login');
  });
});
