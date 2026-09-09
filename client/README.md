# Meeting Rooms — Angular client

The front end of the [Meeting Room Booking System](../README.md). Angular 21, standalone components,
signals, one lazily loaded chunk per screen.

Normally you do not run this on its own: `dotnet run --project ../src/MeetingRooms.AppHost` starts
the API, the database and this client together, and passes the API's address in as `API_URL`.

## Running it alone

```bash
npm install
npm start          # http://localhost:4200
```

`proxy.conf.js` forwards `/api` and `/hubs` to `API_URL`, defaulting to `https://localhost:7188` —
the API's own launch profile. Proxying keeps development same-origin, which is why the refresh
cookie works locally without `SameSite=None`.

```bash
npm run build      # dist/meeting-rooms-client/browser
npm test           # vitest
```

## Configuration

`public/config.json` is read at start-up and holds `apiBaseUrl`. Empty means same-origin, which is
what the dev-server proxy provides. The deployment workflow overwrites this file with the API's URL,
so pointing a built client at a different API needs no rebuild.

`public/web.config` carries the IIS rewrite that makes deep links work on an Azure Web App.
