# Seldon Lattice Frontend

React + TypeScript client for the Seldon Lattice dialectical reasoning engine. It consumes
the backend's SSE stream and renders the debate as an interactive mind map.

## Prerequisites

- Node.js 20+ and npm
- The backend running on `http://localhost:5050` (see the root `README.md`)

## Setup

```bash
npm install
```

The Vite dev server proxies `/api` to the backend. Copy `.env.example` to `.env` if you need
to change the proxy target or the API key; the defaults match the committed development
configuration.

```bash
cp .env.example .env   # optional
```

## Scripts

| Script | Purpose |
|---|---|
| `npm run dev` | Start the Vite dev server on `http://localhost:5174` |
| `npm run build` | Type-check and build for production |
| `npm run lint` | Run oxlint |
| `npm run test` | Run the vitest suite |
| `npm run preview` | Preview the production build |

## Environment

| Variable | Default | Purpose |
|---|---|---|
| `VITE_DEV_API_KEY` | `ApiKey dev-key-full` | Authorization header injected by the dev proxy |
| `VITE_DEV_PROXY_TARGET` | `http://localhost:5050` | Backend the dev proxy forwards `/api` to |

`.env` is gitignored; `.env.example` is committed.

## Structure

| Path | What it is |
|---|---|
| `src/components/` | UI components (panels, settings, dialogs) |
| `src/components/mindmap/` | React Flow graph and SSE stream handling |
| `src/layout/` | Graph construction and layout algorithms |
| `src/lib/` | API client and transcript utilities |
| `src/hooks/` | Shared React hooks |
| `src/config/` | Agent and framework configuration |
| `src/data/` | Static question lists |
| `src/theme/` | Design tokens |
| `src/types/` | Shared TypeScript types |

## Notes

- The frontend expects the backend's SSE contract (`DebateTurnEvent`). See the backend
  controllers for the event shape.
- Production builds are static files. Serve them from any static host and point the client
  at your backend.
