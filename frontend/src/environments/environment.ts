/**
 * Settings that differ between a developer machine and a deployment.
 *
 * The API base is empty in production because the built client is served from the same origin as
 * the API, which removes CORS from the picture entirely. In development the Angular dev server has
 * its own origin, so the base has to be absolute.
 */
export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5199',
};
