import axios from 'axios';
import { randomUUID } from 'crypto';
import type { InvocationContext } from '@azure/functions';
import { getAudibleConnectionsContainer, getBookshelvesContainer, getJobsContainer } from './cosmosClient';
import type { AudibleConnection, AudibleSyncResult, Book, BookCoverFetchJob, Bookshelf } from './types';

interface AudibleLibraryItem {
  asin: string;
  title: string;
  authors?: Array<{ name: string }>;
  product_images?: Record<string, string>;
}

interface AmazonTokenResponse {
  access_token: string;
  refresh_token?: string;
  token_type?: string;
  expires_in?: number;
}

export class AudibleSyncError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

export async function syncAudibleIntoShelf(
  userId: string,
  shelfId: string,
  marketplace: string | undefined,
  context: InvocationContext,
): Promise<AudibleSyncResult> {
  const bookshelvesContainer = await getBookshelvesContainer();
  const { resource: shelf } = await bookshelvesContainer.item(shelfId, userId).read<Bookshelf>();
  if (!shelf) {
    throw new AudibleSyncError(404, 'Bookshelf not found');
  }

  let connection = await getAudibleConnectionForUser(userId);
  let accessToken = connection?.accessToken ?? process.env.AUDIBLE_ACCESS_TOKEN ?? null;
  if (!accessToken) {
    throw new AudibleSyncError(428, 'Audible account not connected. Please connect your Audible account first.');
  }

  if (connection && isExpiring(connection.expiresAt)) {
    const refreshed = await refreshAudibleAccessToken(connection, context);
    if (refreshed) {
      connection = refreshed;
      accessToken = refreshed.accessToken;
      context.log('Refreshed Audible access token before sync');
    }
  }

  const preferredMarketplace = marketplace ?? connection?.marketplace ?? 'UK';
  const marketplaceOrder = getMarketplaceFallbackOrder(preferredMarketplace);

  let data: { items?: AudibleLibraryItem[] } | null = null;
  let effectiveMarketplace = preferredMarketplace;
  const deniedMarketplaces: string[] = [];
  const denyReasons: string[] = [];
  let refreshedOnAuthFailure = false;

  for (const candidateMarketplace of marketplaceOrder) {
    try {
      data = await fetchAudibleLibrary(accessToken, connection?.tokenType, candidateMarketplace);
      effectiveMarketplace = candidateMarketplace;
      break;
    } catch (error) {
      if (axios.isAxiosError(error) && (error.response?.status === 401 || error.response?.status === 403)) {
        if (!refreshedOnAuthFailure && connection?.refreshToken) {
          const refreshed = await refreshAudibleAccessToken(connection, context);
          refreshedOnAuthFailure = true;
          if (refreshed) {
            connection = refreshed;
            accessToken = refreshed.accessToken;
            try {
              data = await fetchAudibleLibrary(accessToken, connection?.tokenType, candidateMarketplace);
              effectiveMarketplace = candidateMarketplace;
              break;
            } catch (retryError) {
              if (!axios.isAxiosError(retryError) || (retryError.response?.status !== 401 && retryError.response?.status !== 403)) {
                throw retryError;
              }
              const retryResponseError =
                retryError.response?.data as { error?: string; message?: string } | string | undefined;
              const retryDenyReason =
                typeof retryResponseError === 'string'
                  ? retryResponseError
                  : retryResponseError?.message ?? retryResponseError?.error ?? retryError.message;
              deniedMarketplaces.push(candidateMarketplace);
              denyReasons.push(`${candidateMarketplace}: ${retryDenyReason}`);
              continue;
            }
          }
        }

        deniedMarketplaces.push(candidateMarketplace);
        const responseError = error.response?.data as { error?: string; message?: string } | string | undefined;
        const denyReason =
          typeof responseError === 'string'
            ? responseError
            : responseError?.message ?? responseError?.error ?? error.message;
        denyReasons.push(`${candidateMarketplace}: ${denyReason}`);
        continue;
      }

      throw error;
    }
  }

  if (!data) {
    const details = denyReasons.length > 0 ? ` Details: ${denyReasons.join(' | ')}` : '';
    const tokenStatus = await validateAmazonProfileToken(accessToken, connection?.tokenType);
    if (tokenStatus.valid) {
      throw new AudibleSyncError(
        403,
        `Your Amazon login token is valid, but this app is not authorized to read the Audible library API for this account. This is an Audible API entitlement/permission issue on the Amazon developer app, not a marketplace mismatch.${details}`,
      );
    }
    throw new AudibleSyncError(
      403,
      `Audible denied library access for marketplaces ${deniedMarketplaces.join(', ')}. Use Connect / Manage Audible to reconnect and confirm the marketplace tied to your library.${details}`,
    );
  }

  if (connection && connection.marketplace !== effectiveMarketplace) {
    try {
      const container = await getAudibleConnectionsContainer();
      await container.items.upsert<AudibleConnection>({
        ...connection,
        marketplace: effectiveMarketplace,
        updatedAt: new Date().toISOString(),
      });
      context.log(`Updated stored Audible marketplace to ${effectiveMarketplace}`);
    } catch (marketplaceUpdateError) {
      context.warn('Failed to persist detected Audible marketplace', marketplaceUpdateError);
    }
  }

  const items: AudibleLibraryItem[] = data.items ?? [];
  const existingAsins = new Set(shelf.books.map((b) => b.asin).filter(Boolean));
  const newBooks: Book[] = [];
  const jobsContainer = await getJobsContainer();

  for (const item of items) {
    if (item.asin && existingAsins.has(item.asin)) {
      continue;
    }

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

    if (!book.spineUrl) {
      const job: BookCoverFetchJob = {
        id: randomUUID(),
        bookId: book.id,
        shelfId,
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
  await bookshelvesContainer.item(shelfId, userId).replace<Bookshelf>(shelf);

  return {
    booksFound: items.length,
    booksAdded: newBooks.length,
    books: newBooks,
  };
}

export async function getLatestShelfIdForUser(userId: string): Promise<string | null> {
  const container = await getBookshelvesContainer();
  const { resources } = await container.items
    .query<{ id: string }>({
      query: 'SELECT TOP 1 c.id FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC',
      parameters: [{ name: '@userId', value: userId }],
    })
    .fetchAll();

  return resources[0]?.id ?? null;
}

export async function hasValidAudibleConnection(userId: string): Promise<boolean> {
  const container = await getAudibleConnectionsContainer();
  const { resource } = await container.item(userId, userId).read<AudibleConnection>();
  if (!resource?.accessToken) {
    return false;
  }

  if (!resource.expiresAt) {
    return true;
  }

  return Date.parse(resource.expiresAt) > Date.now();
}

async function getAudibleConnectionForUser(userId: string): Promise<AudibleConnection | null> {
  const container = await getAudibleConnectionsContainer();
  const { resource } = await container.item(userId, userId).read<AudibleConnection>();
  return resource ?? null;
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
  return hosts[marketplace] ?? 'api.audible.co.uk';
}

async function fetchAudibleLibrary(
  accessToken: string,
  tokenType: string | undefined,
  marketplace: string,
): Promise<{ items?: AudibleLibraryItem[] }> {
  const apiHost = getAudibleApiHost(marketplace);
  const audibleClientId = process.env.AUDIBLE_CLIENT_ID ?? '0';
  const response = await axios.get<{ items?: AudibleLibraryItem[] }>(`https://${apiHost}/1.0/library`, {
    headers: {
      Authorization: `${tokenType ?? 'Bearer'} ${accessToken}`,
      'client-id': audibleClientId,
    },
    params: { response_groups: 'product_details,product_desc', num_results: 1000 },
    timeout: 15000,
  });
  return response.data;
}

async function validateAmazonProfileToken(
  accessToken: string,
  tokenType: string | undefined,
): Promise<{ valid: boolean }> {
  try {
    await axios.get('https://api.amazon.com/user/profile', {
      headers: { Authorization: `${tokenType ?? 'Bearer'} ${accessToken}` },
      timeout: 10000,
    });
    return { valid: true };
  } catch {
    return { valid: false };
  }
}

function getMarketplaceFallbackOrder(preferredMarketplace: string): string[] {
  const allMarketplaces = ['US', 'UK', 'DE', 'FR', 'AU', 'CA', 'JP', 'IT', 'ES', 'IN'];
  return [preferredMarketplace, ...allMarketplaces.filter((m) => m !== preferredMarketplace)];
}

function isExpiring(expiresAt: string | undefined): boolean {
  if (!expiresAt) {
    return false;
  }

  // Refresh slightly before expiry to avoid races.
  return Date.parse(expiresAt) <= Date.now() + 60_000;
}

async function refreshAudibleAccessToken(
  connection: AudibleConnection,
  context: InvocationContext,
): Promise<AudibleConnection | null> {
  const clientId = process.env.AMAZON_CLIENT_ID;
  const clientSecret = process.env.AMAZON_CLIENT_SECRET;
  if (!clientId || !clientSecret || !connection.refreshToken) {
    return null;
  }

  try {
    const body = new URLSearchParams({
      grant_type: 'refresh_token',
      refresh_token: connection.refreshToken,
      client_id: clientId,
      client_secret: clientSecret,
    });

    const { data } = await axios.post<AmazonTokenResponse>('https://api.amazon.com/auth/o2/token', body, {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      timeout: 15000,
    });

    const refreshed: AudibleConnection = {
      ...connection,
      accessToken: data.access_token,
      refreshToken: data.refresh_token ?? connection.refreshToken,
      tokenType: data.token_type ?? connection.tokenType,
      expiresAt: data.expires_in
        ? new Date(Date.now() + data.expires_in * 1000).toISOString()
        : connection.expiresAt,
      updatedAt: new Date().toISOString(),
    };

    const container = await getAudibleConnectionsContainer();
    await container.items.upsert<AudibleConnection>(refreshed);
    return refreshed;
  } catch (refreshError) {
    context.warn('Failed to refresh Audible access token', refreshError);
    return null;
  }
}
