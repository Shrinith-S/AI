# Chitra Studio — web UI

Angular 22 (standalone components, signals, zoneless) + Tailwind CSS v4.

- `npm start` — dev server on http://localhost:4200; `/api` and `/hubs` are proxied to the API on :5080 (`proxy.conf.json`)
- `npx ng build` — production build to `dist/chitra-web/browser`

Structure: `src/app/core` (API client, SignalR, stores), `src/app/shared` (reusable components), `src/app/pages` (routes), `src/app/layout` (shell).

See the repository [README](../README.md), [SETUP](../SETUP.md) and [ARCHITECTURE](../ARCHITECTURE.md).
