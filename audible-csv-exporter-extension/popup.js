const exportButton = document.getElementById('exportButton');
const statusEl = document.getElementById('status');

function setStatus(message) {
  statusEl.textContent = message;
}

function isAudibleLibraryUrl(url) {
  if (!url) return false;
  return /^https:\/\/(?:[^/]+\.)?audible\.(com|co\.uk|de|fr|com\.au|ca|co\.jp|it|es|in)\//.test(url);
}

async function sendStartMessage(tabId) {
  try {
    return await chrome.tabs.sendMessage(tabId, { type: 'audible-export-start' });
  } catch (error) {
    if (!(error instanceof Error) || !error.message.includes('Receiving end does not exist')) {
      throw error;
    }

    await chrome.scripting.executeScript({
      target: { tabId },
      files: ['content-script.js']
    });

    return await chrome.tabs.sendMessage(tabId, { type: 'audible-export-start' });
  }
}

async function startExport() {
  setStatus('');
  exportButton.disabled = true;

  try {
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    if (!tab?.id || !isAudibleLibraryUrl(tab.url)) {
      setStatus('Open an Audible tab first.');
      return;
    }

    const response = await sendStartMessage(tab.id);
    if (!response?.ok) {
      setStatus(response?.error ?? 'Unable to start export on this page.');
      return;
    }

    setStatus('Export started. Keep the Audible tab open.');
  } catch (error) {
    setStatus(`Export failed: ${error instanceof Error ? error.message : 'unknown error'}`);
  } finally {
    exportButton.disabled = false;
  }
}

exportButton.addEventListener('click', startExport);
