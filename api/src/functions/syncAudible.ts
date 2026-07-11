import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import axios from 'axios';
import { getAudibleConnectionsContainer, getBookshelvesContainer, getJobsContainer } from '../shared/cosmosClient';
import type { AudibleConnection, Book, Bookshelf, BookCoverFetchJob, AudibleSyncResult } from '../shared/types';
import { randomUUID } from 'crypto';

// Audible uses the Amazon Audible API (unofficial) - this integrates via a
// redirect-based OAuth flow.  The actual OAuth token is stored in Cosmos after
// the user completes the /api/audibleCallback flow.
export async function syncAudible(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('syncAudible triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const body = await request.json() as { shelfId?: string; marketplace?: string };

    if (!body.shelfId) return { status: 400, jsonBody: { error: 'shelfId is required' } };

    const container = await getBookshelvesContainer();
    const { resource: shelf } = await container.item(body.shelfId, userId).read<Bookshelf>();
    if (!shelf) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    const accessToken = await getAccessTokenForUser(userId);
    if (!accessToken) {
      return {
        status: 428,
        jsonBody: { error: 'Audible account not connected. Please connect your Audible account first.' },
      };
    }

    const marketplace = body.marketplace ?? 'US';
    const apiHost = getAudibleApiHost(marketplace);

    // Fetch library from Audible API
    const { data } = await axios.get(`https://${apiHost}/1.0/library`, {
      headers: { Authorization: `Bearer ${accessToken}` },
      params: { response_groups: 'product_details,product_desc', num_results: 1000 },
      timeout: 15000,
    });

    const items: AudibleLibraryItem[] = data.items ?? [];
    const existingAsins = new Set(shelf.books.map((b) => b.asin).filter(Boolean));

    const newBooks: Book[] = [];
    const jobsContainer = await getJobsContainer();

    for (const item of items) {
      if (item.asin && existingAsins.has(item.asin)) continue;

      const book: Book = {
        id: randomUUID(),
        title: item.title,
        author: item.authors?.[0]?.name ?? 'Unknown',
        coverUrl: item.product_images?.['500'] ?? item.product_images?.['330'],
        source: 'audible',
        asin: item.asin,
        addedAt: new Date().toISOString(),
      };

      newBooks.push(book);

      // Queue cover-fetch job for spine images
      if (!book.spineUrl) {
        const job: BookCoverFetchJob = {
          id: randomUUID(),
          bookId: book.id,
          shelfId: body.shelfId,
          title: book.title,
          author: book.author,
          asin: book.asin,
          status: 'pending',
          createdAt: new Date().toISOString(),
        };
        try {
          await jobsContainer.items.create(job);
        } catch (jobErr) {
          context.warn('Failed to create cover fetch job', jobErr);
        }
      }
    }

    shelf.books = [...shelf.books, ...newBooks];
    shelf.updatedAt = new Date().toISOString();
    await container.item(body.shelfId, userId).replace<Bookshelf>(shelf);

    const result: AudibleSyncResult = {
      booksFound: items.length,
      booksAdded: newBooks.length,
      books: newBooks,
    };

    return { status: 200, jsonBody: result };
  } catch (error) {
    context.error('syncAudible error:', error);
    return { status: 500, jsonBody: { error: 'Failed to sync Audible library' } };
  }
}

function getAudibleApiHost(marketplace: string): string {
  const hosts: Record<string, string> = {
    US: 'api.audible.com',
    UK: 'api.audible.co.uk',
    DE: 'api.audible.de',
    FR: 'api.audible.fr',
    AU: 'api.audible.com.au',
    CA: 'api.audible.ca',
    JP: 'api.audible.co.jp',
    IT: 'api.audible.it',
    ES: 'api.audible.es',
    IN: 'api.audible.in',
  };
  return hosts[marketplace] ?? 'api.audible.com';
}

interface AudibleLibraryItem {
  asin: string;
  title: string;
  authors?: Array<{ name: string }>;
  product_images?: Record<string, string>;
}

async function getAccessTokenForUser(userId: string): Promise<string | null> {
  const container = await getAudibleConnectionsContainer();
  const { resource } = await container.item(userId, userId).read<AudibleConnection>();
  if (resource?.accessToken) {
    return resource.accessToken;
  }

  return process.env.AUDIBLE_ACCESS_TOKEN ?? null;
}

app.http('syncAudible', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'syncAudible',
  handler: syncAudible,
});
