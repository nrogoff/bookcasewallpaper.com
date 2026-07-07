import { app, InvocationContext, Timer } from '@azure/functions';
import axios from 'axios';
import { getJobsContainer, getBookshelvesContainer } from '../shared/cosmosClient';
import { uploadFromUrl } from '../shared/storageClient';
import type { BookCoverFetchJob, Bookshelf } from '../shared/types';

const MAX_JOBS_PER_RUN = 20;

// Timer-triggered background function that processes pending cover fetch jobs.
// Finds book cover and spine images using Open Library / Google Books APIs,
// uploads them to Azure Blob Storage, and updates the bookshelf in Cosmos DB.
export async function fetchBookCovers(_timer: Timer, context: InvocationContext): Promise<void> {
  context.log('fetchBookCovers timer triggered');

  const jobsContainer = await getJobsContainer();
  const bookshelvesContainer = await getBookshelvesContainer();

  const { resources: pendingJobs } = await jobsContainer.items
    .query<BookCoverFetchJob>({
      query: `SELECT TOP ${MAX_JOBS_PER_RUN} * FROM c WHERE c.status = 'pending' ORDER BY c.createdAt ASC`,
    })
    .fetchAll();

  context.log(`Processing ${pendingJobs.length} pending cover fetch jobs`);

  for (const job of pendingJobs) {
    try {
      // Mark as processing
      job.status = 'processing';
      await jobsContainer.item(job.id, job.id).replace(job);

      // Try to find a cover image
      const coverUrl = await findCoverImage(job);

      if (coverUrl) {
        // Upload to Azure Storage
        const blobName = `covers/${job.bookId}.jpg`;
        const storedUrl = await uploadFromUrl('book-covers', blobName, coverUrl);

        // Update the book record in its bookshelf
        const { resource: shelf } = await bookshelvesContainer
          .item(job.shelfId, job.shelfId)
          .read<Bookshelf>();

        if (shelf) {
          const bookIndex = shelf.books.findIndex((b) => b.id === job.bookId);
          if (bookIndex >= 0) {
            shelf.books[bookIndex].coverUrl = storedUrl;
            shelf.updatedAt = new Date().toISOString();
            await bookshelvesContainer.item(job.shelfId, job.shelfId).replace(shelf);
          }
        }
      }

      // Mark as done (even if no image found — prevents retry loops)
      job.status = 'done';
      await jobsContainer.item(job.id, job.id).replace(job);

      context.log(`Processed job ${job.id} for "${job.title}"`);
    } catch (error) {
      context.error(`Failed to process job ${job.id}:`, error);
      try {
        job.status = 'failed';
        await jobsContainer.item(job.id, job.id).replace(job);
      } catch {
        // Ignore update errors
      }
    }
  }
}

async function findCoverImage(job: BookCoverFetchJob): Promise<string | undefined> {
  // 1. Try Open Library by ASIN (ISBN) if available
  if (job.asin) {
    const olUrl = `https://covers.openlibrary.org/b/isbn/${job.asin}-L.jpg`;
    try {
      const head = await axios.head(olUrl, { timeout: 5000 });
      if (head.status === 200 && (head.headers['content-type'] as string)?.startsWith('image')) {
        return olUrl;
      }
    } catch {
      // Not found via ASIN
    }
  }

  // 2. Try Open Library search
  try {
    const q = encodeURIComponent(`${job.title} ${job.author}`);
    const { data } = await axios.get(
      `https://openlibrary.org/search.json?q=${q}&fields=cover_i&limit=1`,
      { timeout: 8000 },
    );
    const coverId = data.docs?.[0]?.cover_i;
    if (coverId) {
      return `https://covers.openlibrary.org/b/id/${coverId}-L.jpg`;
    }
  } catch {
    // Ignore
  }

  // 3. Try Google Books API
  try {
    const q = encodeURIComponent(`${job.title} ${job.author}`);
    const key = process.env.GOOGLE_BOOKS_API_KEY;
    const url = key
      ? `https://www.googleapis.com/books/v1/volumes?q=${q}&key=${key}&maxResults=1`
      : `https://www.googleapis.com/books/v1/volumes?q=${q}&maxResults=1`;
    const { data } = await axios.get(url, { timeout: 8000 });
    const thumbnail = data.items?.[0]?.volumeInfo?.imageLinks?.thumbnail as string | undefined;
    if (thumbnail) {
      // Upgrade to higher-res image
      return thumbnail.replace('zoom=1', 'zoom=2').replace('http:', 'https:');
    }
  } catch {
    // Ignore
  }

  return undefined;
}

app.timer('fetchBookCovers', {
  schedule: '0 */30 * * * *', // Every 30 minutes
  handler: fetchBookCovers,
});
