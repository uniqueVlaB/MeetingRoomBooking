---
name: azure-deploy
description: Diagnose and verify the GitHub Actions deployment to the two Azure Web Apps. Use when a deploy fails, when the deployed app misbehaves but works locally, or when setting up the Azure resources and GitHub secrets for the first time.
---

# Deploying to Azure

Full setup instructions are in `docs/deployment.md`. This skill is for getting a broken deployment
working, and covers the failures that actually happen.

## Shape of the deployment

`.github/workflows/deploy.yml` runs on every push to `main`: build and test first — including the
concurrency test — then two parallel deploys, API and client, each with a publish profile.

Aspire is **not** used to deploy. It orchestrates local development only; the deployment target is
Azure Web Apps, whereas Aspire's own publishing targets Container Apps.

## Symptom → cause

### "Signing in works, but reloading the page signs me out"

The refresh cookie is not coming back. This is the most common deployed-only failure, because the
cookie is same-site locally (through the dev-server proxy) and cross-site in Azure. All of these
must hold:

- `RefreshTokenCookie__CrossSite` is `true` on the API Web App;
- both apps are on HTTPS (`SameSite=None` without `Secure` is rejected outright);
- `Cors__AllowedOrigins__0` is the client's **exact** origin, no trailing slash — `AllowCredentials`
  is incompatible with a wildcard;
- the browser's network tab shows `Set-Cookie` on the sign-in response and a `Cookie` header on the
  `/api/auth/refresh` request.

### "The schedule does not update live"

- Is `Azure__SignalR__ConnectionString` set? Without it the API silently uses its in-process hub,
  which works on one instance and not across several.
- Is the SignalR resource in **Default** mode? `Serverless` mode rejects a hub hosted by the app.
- Are **Web sockets** enabled on the API Web App? Without them SignalR falls back to long polling —
  slower, but it should still work, so if nothing arrives at all, look elsewhere.
- Check `/hubs/bookings/negotiate` returns 200 with a token and 401 without one.

### "The API returns 500 on every request"

Usually configuration. Check the Web App's log stream. `Jwt__SigningKey` shorter than 32 characters
fails at start-up by design, as does a missing `Jwt` section.

### "Deep links 404, but the home page works"

`web.config` did not reach the client Web App. It ships from `client/public/`, and the deploy job
asserts its presence — check that step's output. It must also be a Windows/IIS Web App for the
rewrite to apply.

### "The client calls the wrong API"

`config.json` in the deployed output should contain the `API_BASE_URL` repository variable. Fetch
`https://<client>/config.json` and look. It is written by the deploy job, not baked into the bundle,
so a wrong value needs a variable change and a redeploy, not a rebuild.

### "Download publish profile is greyed out"

Set `SCM Basic Auth Publishing Credentials` to On under the Web App's Configuration → General
settings.

## Verifying a deployment

```bash
curl -s -o /dev/null -w '%{http_code}\n' https://<api>/health          # expect 200
curl -s -o /dev/null -w '%{http_code}\n' https://<api>/api/rooms       # expect 401
curl -s https://<client>/config.json                                   # expect the API's URL
```

Then in a browser: sign in, reload the page (the cookie test), and open one schedule in two windows
— booking in one must update the other without a refresh. Those two are the checks that behave
differently once the halves are on separate origins.

## What not to do

- Do not commit a publish profile, connection string or signing key. They belong in GitHub secrets
  and Web App settings.
- Do not disable the test job to get a deploy out. That gate is what stops a change that
  reintroduces double-booking from reaching production.
