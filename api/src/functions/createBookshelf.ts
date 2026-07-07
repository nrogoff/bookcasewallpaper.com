import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';
import type { Bookshelf, BookshelfSettings } from '../shared/types';
import { randomUUID } from 'crypto';

export async function createBookshelf(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('createBookshelf triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const body = await request.json() as { name?: string; settings?: BookshelfSettings };

    if (!body.name?.trim()) {
      return { status: 400, jsonBody: { error: 'name is required' } };
    }

    const now = new Date().toISOString();
    const shelf: Bookshelf = {
      id: randomUUID(),
      userId,
      name: body.name.trim(),
      books: [],
      settings: body.settings ?? {
        width: 1920,
        height: 1080,
        shelfColor: '#8B4513',
        wallColor: '#F5DEB3',
        shelfCount: 4,
        booksPerShelf: 20,
        showTitles: false,
        format: 'wallpaper',
      },
      createdAt: now,
      updatedAt: now,
    };

    const container = await getBookshelvesContainer();
    const { resource } = await container.items.create<Bookshelf>(shelf);

    return { status: 201, jsonBody: resource };
  } catch (error) {
    context.error('createBookshelf error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('createBookshelf', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'createBookshelf',
  handler: createBookshelf,
});
