# Copilot Instructions

## Repository Purpose

BookshelfWallpaper.com generates bookshelf-style wallpapers from audiobook libraries. Users can:
- Create and manage bookshelves.
- Add books manually, by file upload, or by Audible sync.
- Generate wallpapers locally in the browser and via API-backed rendering.
- Persist bookshelf data in Cosmos DB and image assets in Azure Blob Storage.

Primary evidence: `README.md`, `frontend/src/pages/*`, `api/src/functions/*`.

## Repository Structure

```text
.
├── api/                         # Azure Functions (TypeScript, Node.js)
│   ├── src/functions/           # HTTP + timer triggers
│   ├── src/shared/              # Cosmos/Storage clients and shared types
│   ├── host.json
│   ├── local.settings.json.example
│   ├── package.json
│   └── tsconfig.json
├── frontend/                    # React + Vite app (TypeScript)
│   ├── src/components/
│   ├── src/pages/
│   ├── src/hooks/
│   ├── src/services/
│   ├── src/types/
│   ├── package.json
│   └── vite.config.ts
├── infrastructure/
│   ├── main.bicep
│   └── parameters.json
├── staticwebapp.config.json
└── .github/
    └── workflows/deploy.yml
```

## Current State Notes

Implemented:
- Frontend routes and pages are wired (`/`, `/library`, `/bookshelf/:id`, `/wallpaper`, `/connect`).
- API includes bookshelf CRUD, book add/remove/search, wallpaper generation, file upload, Audible sync, and timer-based cover fetching.
- CI deploy workflow builds frontend and API, then deploys to Azure Static Web Apps.
- Infrastructure defines Static Web App, Cosmos DB account/database/containers, Storage account/blob containers, and Application Insights.

Scaffold/placeholder or incomplete areas:
- API auth uses fallback user id `anonymous` when identity headers are absent; comments indicate real identity integration is expected.
- Audible sync currently reads `AUDIBLE_ACCESS_TOKEN` from environment, while comments indicate per-user token persistence is intended.
- `infrastructure/main.bicep` declares `functionAppPlanName` and `functionAppName` params that are not used by any resource.
- `frontend/README.md` is still the default Vite template and not project-specific guidance.

## Technology Stack

Frontend stack:
- React 19 + React Router 7 (`frontend/package.json`).
- Vite 8 + TypeScript 6 project references (`frontend/package.json`, `frontend/tsconfig*.json`).
- TanStack Query + Axios for API calls.
- Oxlint configured as frontend lint command.

API stack:
- Azure Functions v4 for Node.js (`@azure/functions` in `api/package.json`, `api/host.json`).
- TypeScript 5, CommonJS output targeting ES2022 (`api/tsconfig.json`).
- Azure Cosmos DB SDK and Azure Blob Storage SDK.
- `canvas`, `axios`, and `cheerio` used for image generation and content fetch/parsing.

Infrastructure/deployment stack:
- Bicep for IaC (`infrastructure/main.bicep`).
- Azure Static Web Apps GitHub Action (`.github/workflows/deploy.yml`).
- Static Web Apps runtime routing/security headers (`staticwebapp.config.json`).

## Active Projects

- `frontend`: User-facing single-page app with bookshelf management and wallpaper generation flows.
- `api`: Backend Azure Functions app providing REST-style endpoints and background cover-fetch timer.
- `infrastructure`: Azure resource definitions and parameter file for environment provisioning.
- `.github/workflows/deploy.yml`: CI/CD pipeline for type-check/build/deploy to Azure Static Web Apps.

## Coding Expectations

- Use TypeScript strict mode conventions already present in each project (`api/tsconfig.json`, `frontend/tsconfig*.json`).
- Keep frontend API calls centralized in `frontend/src/services/api.ts` and consume via hooks in `frontend/src/hooks`.
- Add new function handlers under `api/src/functions` and shared clients/types under `api/src/shared`.
- Prefer small, explicit HTTP responses with status + JSON body patterns matching existing handlers.
- Preserve existing route naming style (e.g., `getBookshelves`, `createBookshelf`, `generateWallpaper`) unless a coordinated rename is required.
- Do not assume authenticated identity exists unless auth is implemented end-to-end.

## Testing Conventions

Current state:
- No automated test project/files are present in this repository.

Minimum validation expected for changes:
- Frontend type-check/build must pass.
- API type-check/build must pass.
- Manual smoke test for changed flow (frontend page + corresponding API endpoint/function).

When adding tests in future:
- Keep tests close to their project (`frontend` and/or `api`) and document run commands in this file.

## Build and Validation

From repository root:

```bash
npm --prefix frontend ci
npm --prefix api ci

npm --prefix frontend run lint
npm --prefix frontend run build
npm --prefix frontend run dev
npm --prefix frontend run preview

npm --prefix api run build
npm --prefix api run start
npm --prefix api run watch
```

Type-check commands used in CI:

```bash
npx --prefix frontend tsc --noEmit
npx --prefix api tsc --noEmit
```

Infrastructure validation/deploy (Azure CLI):

```bash
az deployment group create \
  --resource-group nerr-bookcase-rg-nore \
  --template-file infrastructure/main.bicep \
  --parameters infrastructure/parameters.json
```

## Pipelines and Infrastructure

CI/CD:
- Workflow: `.github/workflows/deploy.yml`.
- Triggers: push to `main`; PR open/sync/reopen/close against `main`.
- Build steps: install dependencies for `frontend` and `api`, run TypeScript no-emit checks, build frontend, deploy with `Azure/static-web-apps-deploy@v1`.

Infrastructure defined in `infrastructure/main.bicep`:
- `Microsoft.Web/staticSites` (Standard SKU).
- Cosmos DB account + SQL database + containers (`bookshelves`, `coverFetchJobs`).
- Storage account + blob containers (`book-covers`, `wallpapers`) with blob public access enabled.
- Application Insights component.

## Security and Configuration

Configuration sources:
- API local settings template: `api/local.settings.json.example`.
- SWA runtime config and headers: `staticwebapp.config.json`.

Required API environment values (from `api/local.settings.json.example` and shared clients):
- `AzureWebJobsStorage`
- `FUNCTIONS_WORKER_RUNTIME`
- `COSMOS_ENDPOINT`
- `COSMOS_KEY`
- `COSMOS_DATABASE`
- `AZURE_STORAGE_CONNECTION_STRING`
- `AMAZON_CLIENT_ID`
- `AUDIBLE_REDIRECT_URI`
- `GOOGLE_BOOKS_API_KEY` (optional)

Current security posture notes:
- API routes in `staticwebapp.config.json` allow role `anonymous`.
- API function handlers are declared with `authLevel: 'anonymous'`.
- CSP and security headers are present in `staticwebapp.config.json`.
- Blob containers are configured for public blob access; keep this intentional and documented for cover/wallpaper URL access.

## Working Rules for This Repository

- Ground all implementation decisions in existing project layout (`frontend`, `api`, `infrastructure`).
- Keep changes scoped; avoid introducing new frameworks or runtime stacks unless explicitly requested.
- If adding new endpoints, update both:
  - API function handler in `api/src/functions`.
  - Frontend API client/hook usage in `frontend/src/services` and `frontend/src/hooks`.
- Keep infrastructure and app assumptions aligned; do not reference resources not declared in Bicep.
- Treat auth and Audible token persistence as incomplete areas and call out implications in PR notes when touched.
- Update this file whenever build commands, project layout, or deployment flow changes.

## Maintenance Checklist

- Verify all commands in this document still match `package.json` scripts.
- Remove or resolve scaffolded/incomplete items when implemented:
  - `anonymous` fallback identity usage.
  - env-based single Audible token handling.
  - unused Bicep params for function app plan/name.
  - default Vite template `frontend/README.md`.
- Reconfirm `staticwebapp.config.json` headers and route permissions after auth or routing changes.
- Keep infrastructure outputs/params synchronized with actual runtime usage.
- Re-run frontend and API type-check/build after structural changes.
