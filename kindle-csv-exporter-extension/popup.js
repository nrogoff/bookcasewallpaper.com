'use strict';

let exportedBooks = null;
let activeTabId = null;

function showPanel(id) {
  ['status-idle', 'status-running', 'status-done', 'status-error'].forEach((p) => {
    document.getElementById(p).classList.add('hidden');
  });
  document.getElementById(id).classList.remove('hidden');
}

async function getActiveKindleTab() {
  const tabs = await chrome.tabs.query({ active: true, currentWindow: true });
  const tab = tabs[0];
  if (!tab) return null;
  const url = tab.url || '';
  if (url.includes('read.amazon.co.uk') || url.includes('read.amazon.com')) {
    return tab;
  }
  return null;
}

async function ensureContentScript(tabId) {
  try {
    await chrome.scripting.executeScript({
      target: { tabId },
      files: ['content-script.js'],
    });
  } catch {
    // Already injected or not needed
  }
}

async function startExport() {
  exportedBooks = null;
  showPanel('status-running');
  document.getElementById('progress-text').textContent = 'Connecting to Kindle library…';
  document.getElementById('progress-count').textContent = '0 books found';

  const tab = await getActiveKindleTab();
  if (!tab) {
    document.getElementById('error-text').textContent =
      'Please open read.amazon.co.uk in this browser tab first, then try again.';
    showPanel('status-error');
    return;
  }

  activeTabId = tab.id;
  await ensureContentScript(activeTabId);

  chrome.tabs.sendMessage(activeTabId, { action: 'START_EXPORT' }, (response) => {
    if (chrome.runtime.lastError) {
      document.getElementById('error-text').textContent =
        'Could not connect to the page. Please refresh read.amazon.co.uk and try again.';
      showPanel('status-error');
    }
    // Progress updates come via runtime messages
  });
}

chrome.runtime.onMessage.addListener((message) => {
  if (message.action === 'EXPORT_PROGRESS') {
    document.getElementById('progress-text').textContent = message.status || 'Fetching…';
    document.getElementById('progress-count').textContent = `${message.count} book${message.count !== 1 ? 's' : ''} found`;
  } else if (message.action === 'EXPORT_COMPLETE') {
    exportedBooks = message.books;
    document.getElementById('done-text').textContent = 'Export complete!';
    document.getElementById('done-count').textContent =
      `${message.books.length} book${message.books.length !== 1 ? 's' : ''} exported`;
    showPanel('status-done');
  } else if (message.action === 'EXPORT_ERROR') {
    document.getElementById('error-text').textContent = message.error || 'Export failed. Please try again.';
    showPanel('status-error');
  }
});

function downloadCSV(books) {
  const headers = ['Title', 'Author', 'ASIN', 'Type', 'Cover URL'];
  const escape = (v) => {
    const s = String(v ?? '').replace(/"/g, '""');
    return `"${s}"`;
  };
  const rows = books.map((b) => [
    escape(b.title),
    escape(b.authors ? b.authors.join(', ') : b.author || ''),
    escape(b.asin),
    escape(b.originType || b.type || ''),
    escape(b.productUrl || b.coverUrl || ''),
  ]);
  const csv = [headers.map(escape).join(','), ...rows.map((r) => r.join(','))].join('\r\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `kindle-library-${new Date().toISOString().slice(0, 10)}.csv`;
  a.click();
  URL.revokeObjectURL(url);
}

document.addEventListener('DOMContentLoaded', () => {
  document.getElementById('btn-export').addEventListener('click', startExport);
  document.getElementById('btn-cancel').addEventListener('click', () => {
    if (activeTabId) {
      chrome.tabs.sendMessage(activeTabId, { action: 'CANCEL_EXPORT' }).catch(() => {});
    }
    showPanel('status-idle');
  });
  document.getElementById('btn-download').addEventListener('click', () => {
    if (exportedBooks) downloadCSV(exportedBooks);
  });
  document.getElementById('btn-restart').addEventListener('click', () => {
    exportedBooks = null;
    showPanel('status-idle');
  });
  document.getElementById('btn-retry').addEventListener('click', () => {
    showPanel('status-idle');
  });
});
