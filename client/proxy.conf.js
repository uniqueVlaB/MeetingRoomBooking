/*
 * Dev-server proxy.
 *
 * JavaScript rather than JSON so the API's address can come from the environment. Aspire assigns
 * the API its port and passes it in as API_URL, so the proxy follows whatever port was chosen. The
 * reference project hardcoded this in a .json file and broke whenever the API moved.
 *
 * Proxying also keeps local development same-origin, which means the refresh cookie is same-site
 * and needs no SameSite=None -- browsers reject that over plain HTTP.
 */
const target = process.env['API_URL'] ?? 'https://localhost:7188';

module.exports = {
  '/api': {
    target,
    // The local development certificate is self-signed.
    secure: false,
    changeOrigin: true,
  },
  '/hubs': {
    target,
    secure: false,
    changeOrigin: true,
    // SignalR upgrades to WebSockets after negotiating.
    ws: true,
  },
};
