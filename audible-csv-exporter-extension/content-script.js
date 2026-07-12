(function () {
  const MAX_PAGES = 250;
  const STATE_KEY = 'audibleCsvExporterStateV1';

  let isRunning = false;

  chrome.runtime.onMessage.addListener((message, _sender, sendResponse) => {
    if (message?.type !== 'audible-export-start') {
      return;
    }

    if (isRunning) {
      sendResponse({ ok: false, error: 'Export already running.' });
      return;
    }

    if (!isLikelyLibraryPage()) {
      sendResponse({ ok: false, error: 'Open your Audible Library page first.' });
      return;
    }

    isRunning = true;
    const initialState = {
      running: true,
      pagesVisited: 0,
      byKey: {},
      visitedPages: [],
      debug: [],
      startedAt: new Date().toISOString()
    };
    saveState(initialState);

    runExport()
      .catch((error) => {
        alert(`Audible CSV export failed: ${error instanceof Error ? error.message : 'Unknown error'}`);
        clearState();
      })
      .finally(() => {
        isRunning = false;
      });

    sendResponse({ ok: true });
  });

  function isLikelyLibraryPage() {
    return /\/library\b/i.test(window.location.pathname) || /Library/i.test(document.title);
  }

  if (isLikelyLibraryPage()) {
    const state = loadState();
    if (state?.running) {
      runExport().catch(() => {
        // Ignore here; explicit failures are surfaced from interactive runs.
      });
    }
  }

  async function runExport() {
    const state = loadState();
    if (!state?.running) {
      return;
    }

    const requiredUrl = buildInitialLibraryUrl(window.location.href);
    if (state.pagesVisited === 0 && !isSamePageFingerprint(window.location.href, requiredUrl)) {
      window.location.href = requiredUrl;
      return;
    }

    await waitForLibraryContent();

    const books = scrapeBooksFromDocument(document);
    state.debug.push({
      page: getCurrentPageNumber(window.location.href),
      rowsDetected: document.querySelectorAll('.adbl-library-content-row').length,
      titleLinksDetected: document.querySelectorAll('a[href*="/pd/"]').length,
      booksExtracted: books.length
    });
    for (const book of books) {
      const key = book.asin ? `asin:${book.asin}` : `${book.title}|||${book.author}`.toLowerCase();
      if (!state.byKey[key]) {
        state.byKey[key] = book;
      }
    }

    const currentFingerprint = toPageFingerprint(window.location.href);
    if (!state.visitedPages.includes(currentFingerprint)) {
      state.visitedPages.push(currentFingerprint);
      state.pagesVisited += 1;
    }

    const nextUrl = getNextPageUrl(document, window.location.href);
    if (!nextUrl || state.pagesVisited >= MAX_PAGES) {
      const rows = Object.values(state.byKey);
      downloadCsv(rows, state.debug);
      if (rows.length === 0) {
        alert(`Audible CSV export finished but no books were extracted. Debug rows were written to the CSV.`);
      } else {
        alert(`Audible CSV export complete. ${rows.length} books exported.`);
      }
      clearState();
      return;
    }

    const nextFingerprint = toPageFingerprint(nextUrl);
    if (state.visitedPages.includes(nextFingerprint)) {
      const rows = Object.values(state.byKey);
      downloadCsv(rows, state.debug);
      if (rows.length === 0) {
        alert(`Audible CSV export stopped by loop guard and no books were extracted. Debug rows were written to the CSV.`);
      } else {
        alert(`Audible CSV export complete (loop guard). ${rows.length} books exported.`);
      }
      clearState();
      return;
    }

    saveState(state);
    window.location.href = nextUrl;
  }

  async function waitForLibraryContent() {
    const timeoutMs = 12000;
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
      const hasRows = document.querySelectorAll('.adbl-library-content-row').length > 0;
      const hasTitles = document.querySelectorAll('a[href*="/pd/"]').length > 0;
      if (hasRows || hasTitles) {
        return;
      }
      await sleep(250);
    }
  }

  function scrapeBooksFromDocument(rootDoc) {
    const rowCards = [...rootDoc.querySelectorAll('.adbl-library-content-row')];
    const cards = rowCards.length > 0
      ? rowCards
      : [...rootDoc.querySelectorAll('[data-testid*="library-item"], .library-item')];

    if (cards.length === 0) {
      return scrapeFallback(rootDoc);
    }

    const fromCards = cards
      .map((card) => {
        const title = extractTitle(card);

        const author = extractAuthor(card);
        const publisher = extractField(card, /publisher:\s*([^\n\r|]+)/i);
        const isbn = extractField(card, /isbn(?:-1[03])?:\s*([0-9xX-]+)/i);
        if (!title) return null;
        return {
          title: clean(title),
          author: clean(author || 'Unknown'),
          publisher: clean(publisher || ''),
          isbn: clean(isbn || ''),
          asin: extractAsin(card)
        };
      })
      .filter(Boolean);

    if (fromCards.length > 0) {
      return fromCards;
    }

    return scrapeByTitleLinks(rootDoc);
  }

  function scrapeFallback(rootDoc) {
    const fromLinks = scrapeByTitleLinks(rootDoc);
    if (fromLinks.length > 0) {
      return fromLinks;
    }

    const titles = [...rootDoc.querySelectorAll('a[href*="/pd/"]')]
      .map((el) => clean(el.textContent || ''))
      .filter((title) => title && !isGarbageTitle(title));
    return titles.map((title) => ({ title, author: 'Unknown', publisher: '', isbn: '', asin: '' }));
  }

  function scrapeByTitleLinks(rootDoc) {
    const links = [
      ...rootDoc.querySelectorAll(
        '.adbl-library-content-row a[href*="/pd/"], .adbl-library-content-row a[href*="/dp/"], .adbl-library-content-row a[href*="/podcast/"]'
      ),
      ...rootDoc.querySelectorAll('a[href*="/pd/"], a[href*="/dp/"], a[href*="/podcast/"]')
    ];
    const records = [];
    const seen = new Set();

    for (const link of links) {
      const title = clean(link.textContent || '');
      if (!title || isGarbageTitle(title)) {
        continue;
      }
      const href = link.getAttribute('href') || '';
      if (href.includes('/author/') || href.includes('searchAuthor')) {
        continue;
      }
      if (link.closest('.parentTitleLabel, .ratingAndReviewLabel, .summaryLabel')) {
        continue;
      }

      const row = link.closest('.adbl-library-content-row') || link.closest('li') || rootDoc.body;
      const author = extractAuthor(row);
      const publisher = extractField(row, /publisher:\s*([^\n\r|]+)/i);
      const isbn = extractField(row, /isbn(?:-1[03])?:\s*([0-9xX-]+)/i);
      const key = `${title}|||${author}`.toLowerCase();
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);
      records.push({
        title,
        author: clean(author || 'Unknown'),
        publisher: clean(publisher || ''),
        isbn: clean(isbn || ''),
        asin: extractAsin(row)
      });
    }

    return records;
  }

  function text(root, selector) {
    const el = root.querySelector(selector);
    return el ? el.textContent : '';
  }

  function clean(value) {
    return value.replace(/\s+/g, ' ').trim();
  }

  function normalizeAuthor(value) {
    const cleaned = clean(value || '');
    return cleaned
      .replace(/^Written\s+by:\s*/i, '')
      .replace(/^By:\s*/i, '')
      .replace(/^Author:\s*/i, '');
  }

  function extractAuthor(card) {
    const authorContainer = card.querySelector('span.authorLabel') || card.querySelector('.authorLabel');
    const authorLinks = [
      ...(authorContainer
        ? authorContainer.querySelectorAll('a.bc-link, a[href*="/author/"], a[href*="author="]')
        : card.querySelectorAll('[data-testid*="author"] a, .authorLabel a, a[href*="/author/"], a[href*="author="]'))
    ]
      .map((el) => clean(el.textContent || ''))
      .filter(Boolean);

    if (authorLinks.length > 0) {
      return unique(authorLinks).join('; ');
    }

    const authorLabelText =
      (authorContainer ? clean(authorContainer.textContent || '') : '') ||
      text(card, '[data-testid*="author"]') ||
      text(card, 'span.authorLabel') ||
      text(card, '[class*="author"]');
    const normalizedLabel = normalizeAuthor(authorLabelText);
    if (normalizedLabel) {
      return normalizedLabel;
    }

    const cardText = clean(card.textContent || '');
    const byMatch = cardText.match(/(?:written by|by)\s*:\s*(.+?)(?:narrated by|length:|release date:|$)/i);
    if (byMatch?.[1]) {
      return clean(byMatch[1]);
    }

    return 'Unknown';
  }

  function extractTitle(card) {
    const scopedTitleLink = [...card.querySelectorAll('a[href]')].find((a) => {
      const href = a.getAttribute('href') || '';
      return (
        href.includes('ref=a_library_t_c5_libItem_') &&
        !href.includes('_author_') &&
        !href.includes('_narrator_') &&
        !href.includes('_parentTitle_') &&
        !href.includes('_series_')
      );
    });
    if (scopedTitleLink) {
      const scopedText =
        clean(text(scopedTitleLink, 'span.bc-size-headline3')) ||
        clean(scopedTitleLink.textContent || '');
      if (scopedText && !isGarbageTitle(scopedText)) {
        return scopedText;
      }
    }

    const candidates = [
      text(card, 'li.bc-list-item a.bc-link > span.bc-size-headline3'),
      text(card, 'a[href*="/podcast/"] > span.bc-size-headline3'),
      text(card, 'a[href*="/podcast/"]'),
      text(card, 'a[href*="/pd/"]'),
      text(card, 'a[href*="/dp/"]'),
      text(card, 'h3 a[href*="/pd/"]'),
      text(card, 'h3 a[href*="/dp/"]'),
      text(card, 'h2 a[href*="/pd/"]'),
      text(card, 'h2 a[href*="/dp/"]'),
      text(card, 'h3 a.bc-link'),
      text(card, 'h2 a.bc-link'),
      text(card, '[data-testid*="title"] a'),
      text(card, '[data-testid*="title"]'),
      text(card, 'h3'),
      text(card, 'h2')
    ].map(clean).filter(Boolean);

    for (const candidate of candidates) {
      if (!isGarbageTitle(candidate)) {
        return candidate;
      }
    }

    return '';
  }

  function isGarbageTitle(title) {
    const normalized = title.toLowerCase();
    return (
      normalized === 'interactive rating stars' ||
      normalized === 'rating stars' ||
      normalized.startsWith('stars') ||
      normalized.includes(':root {') ||
      normalized.includes('--adbl-') ||
      normalized.includes('mark as unfinished') ||
      normalized.includes('mark as finished') ||
      normalized.includes('write a review') ||
      normalized.length < 2
    );
  }

  function extractField(card, pattern) {
    const cardText = clean(card.textContent || '');
    const match = cardText.match(pattern);
    return match?.[1] ?? '';
  }

  function extractAsin(card) {
    const rowId = card.getAttribute('id') || '';
    const rowIdMatch = rowId.match(/adbl-library-content-row-([A-Z0-9]+)/i);
    if (rowIdMatch?.[1]) {
      return rowIdMatch[1];
    }

    const asinInput = card.querySelector('input[name="asin"]');
    const asinValue = asinInput?.getAttribute('value') || '';
    if (asinValue) {
      return clean(asinValue);
    }

    const asinData = card.getAttribute('data-asin') || '';
    if (asinData) {
      return clean(asinData);
    }

    const titleHref = card.querySelector('a[href*="/pd/"], a[href*="/dp/"], a[href*="/podcast/"]')?.getAttribute('href') || '';
    const hrefMatch = titleHref.match(/\/([A-Z0-9]{8,})\b/);
    return hrefMatch?.[1] ?? '';
  }

  function buildInitialLibraryUrl(currentUrl) {
    const source = new URL(currentUrl);
    const url = new URL(`${source.origin}/library/titles`);
    url.searchParams.set('loginAttempt', 'true');
    url.searchParams.set('pageSize', '50');
    url.searchParams.set('page', '1');
    return url.toString();
  }

  function getNextPageUrl(pageDoc, currentUrl) {
    const nextAnchor =
      pageDoc.querySelector('.nextButton a.bc-button-text') ||
      pageDoc.querySelector('.nextButton a') ||
      pageDoc.querySelector('li .nextButton a.bc-button-text') ||
      pageDoc.querySelector('li .nextButton a');

    if (!nextAnchor) {
      return null;
    }

    const wrapper = nextAnchor.closest('.nextButton') || nextAnchor.closest('span.bc-button');
    const disabled =
      nextAnchor.hasAttribute('disabled') ||
      nextAnchor.getAttribute('aria-disabled') === 'true' ||
      nextAnchor.classList.contains('bc-button-disabled') ||
      wrapper?.classList.contains('bc-button-disabled') === true;

    if (disabled) {
      return null;
    }

    const url = new URL(currentUrl);
    const currentPage = Number(url.searchParams.get('page') || '1');
    if (!Number.isFinite(currentPage) || currentPage < 1) {
      return null;
    }
    url.searchParams.set('loginAttempt', 'true');
    url.searchParams.set('page', String(currentPage + 1));
    url.searchParams.set('pageSize', '50');
    return url.toString();
  }

  function toPageFingerprint(urlValue) {
    const url = new URL(urlValue);
    return `${url.origin}${url.pathname}?page=${url.searchParams.get('page') || '1'}&pageSize=${url.searchParams.get('pageSize') || ''}`;
  }

  function isSamePageFingerprint(left, right) {
    return toPageFingerprint(left) === toPageFingerprint(right);
  }

  function loadState() {
    try {
      const raw = sessionStorage.getItem(STATE_KEY);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }

  function saveState(state) {
    sessionStorage.setItem(STATE_KEY, JSON.stringify(state));
  }

  function clearState() {
    sessionStorage.removeItem(STATE_KEY);
  }

  function sleep(ms) {
    return new Promise((resolve) => setTimeout(resolve, ms));
  }

  function escapeCsv(value) {
    const escaped = (value || '').replace(/"/g, '""');
    return `"${escaped}"`;
  }

  function downloadCsv(rows, debug) {
    const csvLines = ['Title,Author,Publisher,ISBN'];
    rows.forEach((row) => {
      csvLines.push(
        `${escapeCsv(row.title)},${escapeCsv(row.author)},${escapeCsv(row.publisher)},${escapeCsv(row.isbn)}`
      );
    });

    if (rows.length === 0 && Array.isArray(debug) && debug.length > 0) {
      csvLines.push('');
      csvLines.push('Debug,Value,,');
      debug.forEach((d) => {
        csvLines.push(`${escapeCsv(`page=${d.page}`)},${escapeCsv(`rows=${d.rowsDetected}; titleLinks=${d.titleLinksDetected}; books=${d.booksExtracted}`)},,`);
      });
    }

    const csv = csvLines.join('\n');
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `audible-library-${new Date().toISOString().slice(0, 10)}.csv`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  function unique(values) {
    return [...new Set(values)];
  }

  function getCurrentPageNumber(urlValue) {
    const url = new URL(urlValue);
    return Number(url.searchParams.get('page') || '1');
  }
})();
