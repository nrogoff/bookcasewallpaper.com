'use strict';

// ── API Configuration ──────────────────────────────────────────────────────
const API_PATH = '/kindle-library/search';
const PAGE_SIZE = 50;

// ── State ──────────────────────────────────────────────────────────────────
let cancelled = false;

// ── Utilities ──────────────────────────────────────────────────────────────

/** Strip trailing colons, commas, and whitespace Amazon appends to author names */
function cleanAuthor(name) {
  return (name || '').replace(/[,:;\s]+$/, '').trim();
}

/** Fetch one page of library results */
async function fetchPage(paginationToken) {
  const params = new URLSearchParams({
    query: '',
    libraryType: 'BOOKS',
    sortType: 'recency',
    querySize: String(PAGE_SIZE),
  });
  if (paginationToken) {
    params.set('paginationToken', String(paginationToken));
  }

  const response = await fetch(`${API_PATH}?${params.toString()}`, {
    credentials: 'include',
    headers: { Accept: 'application/json' },
  });

  if (!response.ok) {
    throw new Error(`Kindle API returned ${response.status}. Make sure you are signed in at read.amazon.co.uk.`);
  }

  return response.json();
}

/** Normalise a raw book item from the API */
function normaliseBook(item) {
  return {
    title: (item.title || '').trim(),
    authors: (item.authors || []).map(cleanAuthor).filter(Boolean),
    asin: (item.asin || '').trim(),
    originType: (item.originType || '').trim(),   // PURCHASE | KINDLE_UNLIMITED | PRIME_READING …
    resourceType: (item.resourceType || '').trim(), // EBOOK | EBOOK_SAMPLE | MAGAZINE …
    percentageRead: item.percentageRead ?? 0,
    coverUrl: (item.productUrl || '').trim(),
    webReaderUrl: (item.webReaderUrl || '').trim(),
  };
}

// ── Main export routine ────────────────────────────────────────────────────

async function runExport() {
  cancelled = false;
  const allBooks = [];
  let paginationToken = null;
  let pageNum = 0;

  // Send progress to popup
  function sendProgress(status) {
    chrome.runtime.sendMessage({
      action: 'EXPORT_PROGRESS',
      status,
      count: allBooks.length,
    }).catch(() => {});
  }

  try {
    sendProgress('Connecting to Kindle library…');

    do {
      if (cancelled) return;

      pageNum++;
      sendProgress(`Fetching page ${pageNum}…`);

      const data = await fetchPage(paginationToken);
      const items = data.itemsList || [];

      for (const item of items) {
        if (cancelled) return;
        const book = normaliseBook(item);
        // Deduplicate by ASIN; keep first occurrence
        if (book.asin && !allBooks.some((b) => b.asin === book.asin)) {
          allBooks.push(book);
        }
      }

      sendProgress(`Page ${pageNum} done`);
      paginationToken = data.paginationToken || null;

      // Brief yield to keep the page responsive
      await new Promise((r) => setTimeout(r, 50));
    } while (paginationToken);

    chrome.runtime.sendMessage({
      action: 'EXPORT_COMPLETE',
      books: allBooks,
    }).catch(() => {});
  } catch (err) {
    chrome.runtime.sendMessage({
      action: 'EXPORT_ERROR',
      error: err.message || 'Unknown error during export.',
    }).catch(() => {});
  }
}

// ── Message listener ───────────────────────────────────────────────────────

chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
  if (message.action === 'START_EXPORT') {
    runExport();
    sendResponse({ started: true });
  } else if (message.action === 'CANCEL_EXPORT') {
    cancelled = true;
    sendResponse({ cancelled: true });
  }
  return false; // synchronous response
});
