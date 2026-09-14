# Deploying to Azure

The system deploys as **two Azure Web Apps** — one for the API, one for the Angular client — plus
**Azure SQL Database** and **Azure SignalR Service**. Pushing to `Deploy` builds, tests and deploys
both apps automatically. `Deploy` is a dedicated deployment branch, separate from `main`, so a
deploy is always a deliberate push rather than a side effect of every merge to `main`.

Aspire is a development-time orchestrator only. It is not used to publish: the task calls for Azure
Web Apps, whereas Aspire's own publishing targets Container Apps.

## 1. Create the Azure resources

In the portal, in one resource group:

| Resource | Notes |
| --- | --- |
| **Azure SQL Database** | Any tier. Note the server name and create a SQL login, or use Entra authentication. |
| **Azure SignalR Service** | Free tier is enough. Set **Service Mode** to `Default` — not `Serverless`, because the API hosts the hub itself. |
| **Web App** (API) | .NET 10 stack. This is `AZURE_API_APP_NAME`. |
| **Web App** (client) | Windows/IIS, so the `web.config` rewrite applies. This is `AZURE_CLIENT_APP_NAME`. |

Both Web Apps can share one App Service Plan.

### SQL firewall

On the SQL **server**, under Networking, enable **Allow Azure services and resources to access this
server**. That covers the API's Web App. It does **not** cover GitHub's runners — see
[Migrations](#migrations) for why that does not matter here.

## 2. Configure the API Web App

Under **Settings → Environment variables → App settings**. The double underscore is how a nested
configuration key is written as an environment variable.

| Setting | Value |
| --- | --- |
| `ConnectionStrings__meetingrooms-db` | The Azure SQL connection string. The name matches the Aspire resource name, so there is one name across both environments. |
| `Azure__SignalR__ConnectionString` | From the SignalR resource's Keys blade. When absent the API falls back to its in-process hub, which works but does not scale past one instance. |
| `Jwt__SigningKey` | A random string of at least 32 characters. Generate a fresh one; never reuse the development default. |
| `Jwt__Issuer` | For example `MeetingRoomsApi`. |
| `Jwt__Audience` | For example `MeetingRoomsClient`. |
| `Cors__AllowedOrigins__0` | The client Web App's exact origin, e.g. `https://meetingrooms-client.azurewebsites.net`. No trailing slash. |
| `RefreshTokenCookie__CrossSite` | `true` — see [the cookie caveat](#the-cross-site-cookie-caveat). |
| `Database__MigrateOnStartup` | `true` — see [Migrations](#migrations). |
| `Seed__Enabled` | `true` for the first deployment, so there is an administrator to sign in as. |
| `Seed__Admin__Email` | The administrator's email address. |
| `Seed__Admin__Password` | A strong password. Seeding is skipped if the account already exists. |
| `Booking__TimeZone` | The IANA or Windows time zone for the schedule, e.g. `Europe/Kyiv` or `UTC`. Slots and booking dates are interpreted in this zone, so date rules ("no booking in the past") are correct in every geography. Defaults to `UTC`. |
| `Booking__MaxDaysAhead` | How far ahead a slot may be booked, in days; a guard against absurd input. Defaults to `365`. Must be between 1 and 3650. |

Also enable **Web sockets** under Configuration → General settings. SignalR falls back to long
polling without it, which works but adds latency to every update.

Consider turning `Seed__Enabled` back to `false` once the administrator account exists.

## The cross-site cookie caveat

This is the single thing most likely to work locally and fail in Azure.

The refresh token travels in an HttpOnly cookie. Locally the Angular dev server proxies `/api`, so
the cookie is **same-site**. In Azure the client and the API are different origins, so it is
**cross-site**, and a browser will only store and send it when all of the following hold:

- the cookie is marked `SameSite=None; Secure` — this is what `RefreshTokenCookie__CrossSite=true`
  does;
- both apps are served over HTTPS — `SameSite=None` without `Secure` is rejected outright;
- CORS allows credentials for the client's **exact** origin. `AllowCredentials` is incompatible with
  a wildcard, so `Cors__AllowedOrigins__0` must be the real URL;
- the client sends `withCredentials: true`, which `AuthService` already does.

The symptom when it is wrong: signing in works, but a page reload signs the user out, because the
refresh call arrives without a cookie.

## 3. Configure the client Web App

Nothing to configure. The deployment workflow writes `config.json` into the published output from
the `API_BASE_URL` repository variable, and the client reads it at start-up. Pointing the client at
a different API therefore needs a variable change and a redeploy, not a rebuild.

`web.config` ships from `client/public/` and carries the IIS rewrite that serves `index.html` for
deep links such as `/rooms/{id}/schedule`, while leaving `/api` and `/hubs` alone.

## 4. Configure GitHub

**Repository secrets** (Settings → Secrets and variables → Actions → Secrets):

| Secret | Where it comes from |
| --- | --- |
| `AZURE_API_PUBLISH_PROFILE` | API Web App → Overview → **Download publish profile**. Paste the whole XML file. |
| `AZURE_CLIENT_PUBLISH_PROFILE` | The same, from the client Web App. |

**Repository variables** (the Variables tab):

| Variable | Value |
| --- | --- |
| `AZURE_API_APP_NAME` | The API Web App's name. |
| `AZURE_CLIENT_APP_NAME` | The client Web App's name. |
| `API_BASE_URL` | The API's origin, e.g. `https://meetingrooms-api.azurewebsites.net`. No trailing slash. |

If **Download publish profile** is greyed out, set `SCM Basic Auth Publishing Credentials` to On
under the Web App's Configuration → General settings.

## 5. Deploy

Push to `Deploy`. [`deploy.yml`](../.github/workflows/deploy.yml) builds, runs the full test suite —
including the concurrency test — and only then deploys the two apps in parallel. A change that
reintroduces double-booking cannot reach Azure, because the gate fails first.

## Migrations

Migrations are applied **at start-up**, gated behind `Database__MigrateOnStartup`.

This is a deliberate trade-off. The production-grade alternative is to build a migration bundle in
CI and run it against Azure SQL:

```bash
dotnet ef migrations bundle -p src/MeetingRooms.Infrastructure.SQL -s src/MeetingRooms.Infrastructure.SQL
./efbundle --connection "$AZURE_SQL_CONNECTION"
```

That needs the GitHub runner to reach Azure SQL, which means opening the firewall for its IP for the
duration of the job — and that needs `az login`, which the publish-profile approach does not provide.
Adding OIDC federated credentials would enable it, at the cost of setting up an Entra app
registration and role assignment before anything can deploy at all.

Start-up migration is safe for this deployment because EF Core takes an exclusive migration lock:
if two instances start together, one applies the migration while the other waits, rather than both
trying. What it does not give you is a migration that can be reviewed and run separately from the
deployment, which is why the bundle is the better answer for a system with real data in it.

## Verifying a deployment

1. Open the client URL and sign in as the seeded administrator.
2. Create a room, and confirm it appears for a non-administrator.
3. Open the same room's schedule in two browsers. Book a slot in one — the other must flip to booked
   **without a refresh**. This exercises Azure SignalR, WebSockets and the query-string token hook
   together.
4. Cancel it; both must show it free again.
5. Sign in, then reload the page. Staying signed in proves the cross-site refresh cookie works.
6. Check `https://<api>/health` responds.

Steps 3 and 5 are the ones that behave differently once the two halves are on separate origins, so
they are worth doing deliberately rather than assuming local behaviour carries over.
