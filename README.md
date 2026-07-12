# BookshelfWallpaper.com

Generate beautiful bookshelf wallpapers from your Audible or other audiobook library. Use them as desktop wallpapers, Microsoft Teams backgrounds, or Zoom backgrounds.

## Features

- 🎧 **Audible Integration** — Connect your Audible account to automatically import your full library
- 📤 **Book List Upload** — Upload a CSV or text file with your titles to build a bookshelf
- 🖼️ **Wallpaper Generation** — Create bookshelf images sized for desktop (1080p/4K), Teams, or Zoom
- 🗂️ **Bookshelf Management** — Create multiple bookshelves, add/remove books, keep them in sync
- 🔍 **Book Search** — Search millions of titles via Open Library
- 🎨 **Customisable** — Choose wall colour, shelf colour, number of shelves, books per row, and more
- ☁️ **Cloud Storage** — Wallpapers are saved to Azure Blob Storage for easy access

## Architecture

```
┌─────────────────────┐    ┌──────────────────────┐    ┌─────────────────┐
│   React + Vite      │    │  Azure Functions      │    │  Azure Cosmos   │
│   (frontend/)       │◄──►│  (api/)               │◄──►│  DB             │
│                     │    │                       │    │  (bookshelves,  │
│  - Bookshelf views  │    │  - REST API           │    │   jobs)         │
│  - Wallpaper canvas │    │  - Audible sync       │    └─────────────────┘
│  - Settings UI      │    │  - Book cover fetch   │
└─────────────────────┘    │  - Image generation   │    ┌─────────────────┐
                           └──────────────────────┘    │  Azure Blob     │
        Deployed as:              ▲                     │  Storage        │
   Azure Static Web App           │ Timer (30 min)      │  (covers,       │
                           Background job fetches       │   wallpapers)   │
                           book covers from Open        └─────────────────┘
                           Library & Google Books
```

## Project Structure

```
.
├── frontend/               # React + Vite app
│   ├── src/
│   │   ├── components/     # Reusable UI components
│   │   ├── hooks/          # React Query hooks for API calls
│   │   ├── pages/          # Route-level page components
│   │   ├── services/       # Axios API client
│   │   └── types/          # Shared TypeScript types
│   └── vite.config.ts
├── api/                    # Azure Functions (Node.js / TypeScript)
│   ├── src/
│   │   ├── functions/      # Individual Azure Function handlers
│   │   └── shared/         # Cosmos DB & Storage clients, types
│   ├── host.json
│   └── local.settings.json.example
├── infrastructure/         # Bicep IaC templates
│   ├── main.bicep
│   └── parameters.json
├── .github/workflows/      # GitHub Actions CI/CD
└── staticwebapp.config.json
```

## Getting Started

### Prerequisites

- Node.js 20+
- [Azure CLI](https://docs.microsoft.com/en-us/cli/azure/install-azure-cli)
- [Azure Functions Core Tools v4](https://docs.microsoft.com/en-us/azure/azure-functions/functions-run-local)
- [Azurite](https://docs.microsoft.com/en-us/azure/storage/common/storage-use-azurite) (local storage emulator) — optional

### Local Development

1. **Clone and install dependencies**

   ```bash
   git clone https://github.com/nrogoff/bookcasewallpaper.com.git
   cd bookcasewallpaper.com

   cd frontend && npm install
   cd ../api && npm install
   ```

2. **Configure the API**

   ```bash
   cp api/local.settings.json.example api/local.settings.json
   # Edit local.settings.json with your Azure Cosmos DB and Storage credentials
   ```

3. **Start the API (Azure Functions)**

   ```bash
   cd api
   npm run build
   npm run start
   # API runs on https://localhost:7071
   ```

4. **Start the frontend**

   ```bash
   cd frontend
   npm run dev
   # App runs on https://localhost:5173
   # Proxies /api/* to https://localhost:7071
   ```

### Environment Variables (API)

| Variable | Description |
|---|---|
| `COSMOS_ENDPOINT` | Azure Cosmos DB endpoint URL |
| `COSMOS_KEY` | Azure Cosmos DB primary key |
| `COSMOS_DATABASE` | Database name (default: `BookshelfWallpaper`) |
| `AZURE_STORAGE_CONNECTION_STRING` | Azure Storage connection string |
| `AMAZON_CLIENT_ID` | Amazon OAuth client ID for Audible integration |
| `AMAZON_CLIENT_SECRET` | Amazon OAuth client secret for token exchange |
| `AUDIBLE_CLIENT_ID` | Audible API client-id header for bearer auth (default `0`) |
| `AUDIBLE_REDIRECT_URI` | OAuth redirect URI for Audible callback |
| `AUDIBLE_POST_CONNECT_URL` | URL to redirect users after successful Audible connect (e.g. `https://localhost:5173/library`) |
| `GOOGLE_BOOKS_API_KEY` | Google Books API key (optional, improves cover search) |

## Deploy to Azure

### 1. Provision infrastructure

```bash
az group create --name nerr-bookcase-rg-nore --location norwayeast

az deployment group create \
  --resource-group nerr-bookcase-rg-nore \
  --template-file infrastructure/main.bicep \
  --parameters infrastructure/parameters.json
```

### 2. Set up GitHub Actions deployment

1. In the Azure Portal, navigate to your Static Web App resource
2. Under **Settings → Deployment tokens**, copy the deployment token
3. Add it as `AZURE_STATIC_WEB_APPS_API_TOKEN` in your GitHub repository secrets
4. Push to `main` — the GitHub Actions workflow will build and deploy automatically

### 3. Configure app settings

In the Azure Portal, add the environment variables listed above to your Static Web App's
**Configuration → Application settings**.

## Audible Integration

The Audible integration uses the Login-with-Amazon (LWA) OAuth 2.0 flow:

1. Register an [Amazon Developer application](https://developer.amazon.com/apps-and-games/login-with-amazon)
2. Set the allowed return URL to `https://<your-app>.azurestaticapps.net/api/audibleCallback`
3. Add `AMAZON_CLIENT_ID` and `AMAZON_CLIENT_SECRET` to app settings
4. Users click **Connect with Audible** and authorise read-only library access

> **Note:** A standard Login with Amazon app may authenticate users successfully but still be denied by Audible library endpoints without the necessary Audible API entitlement/approval.

## Uploading a Book List

Upload a `.txt` or `.csv` file with one book per line:

```
The Hobbit, J.R.R. Tolkien
Project Hail Mary, Andy Weir
Dune, Frank Herbert
```

Or in CSV format:

```csv
"The Lord of the Rings","J.R.R. Tolkien"
"Foundation","Isaac Asimov"
```

## Book Cover Images

Book covers are automatically fetched in the background using:

1. **Open Library** (free, no key required) — searched by title/author
2. **Google Books API** (optional key) — used as a fallback

The background Azure Function timer runs every 30 minutes and processes pending cover-fetch jobs queued when books are added.

## Wallpaper Formats

| Format | Resolution |
|---|---|
| Desktop Wallpaper | 1920 × 1080 |
| Desktop 4K | 3840 × 2160 |
| Microsoft Teams | 1920 × 1080 |
| Zoom | 1280 × 720 |
| Custom | Any dimensions |

## Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

## Licence

MIT
