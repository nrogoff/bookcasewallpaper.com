import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { disconnectAudible } from '../../services/api';
import styles from './AudibleConnect.module.css';

const AUDIBLE_MARKETPLACES = [
  { code: 'US', label: 'United States (audible.com)' },
  { code: 'UK', label: 'United Kingdom (audible.co.uk)' },
  { code: 'DE', label: 'Germany (audible.de)' },
  { code: 'FR', label: 'France (audible.fr)' },
  { code: 'AU', label: 'Australia (audible.com.au)' },
  { code: 'CA', label: 'Canada (audible.ca)' },
  { code: 'JP', label: 'Japan (audible.co.jp)' },
  { code: 'IT', label: 'Italy (audible.it)' },
  { code: 'ES', label: 'Spain (audible.es)' },
  { code: 'IN', label: 'India (audible.in)' },
];

export function AudibleConnect() {
  const [searchParams] = useSearchParams();
  const [marketplace, setMarketplace] = useState('UK');
  const [status, setStatus] = useState<'idle' | 'pending' | 'success' | 'error'>('idle');
  const [message, setMessage] = useState('');
  const [checkingStatus, setCheckingStatus] = useState(true);
  const [isConnected, setIsConnected] = useState(false);
  const shelfId = searchParams.get('shelfId');

  useEffect(() => {
    let cancelled = false;

    async function loadConnectionStatus() {
      try {
        const response = await fetch('/api/getAudibleConnectionStatus');
        if (!response.ok) throw new Error('Failed to get connection status');
        const data = await response.json() as { connected?: boolean; marketplace?: string };
        if (cancelled) return;

        if (data.marketplace) {
          setMarketplace(data.marketplace);
        }
        if (data.connected) {
          setIsConnected(true);
          setStatus('success');
          setMessage('✅ Audible is already connected. You can sync from your bookshelf.');
        }
      } catch {
        if (!cancelled) {
          setStatus('error');
          setMessage('Could not verify Audible connection status. You can still try connecting.');
        }
      } finally {
        if (!cancelled) {
          setCheckingStatus(false);
        }
      }
    }

    loadConnectionStatus();
    return () => {
      cancelled = true;
    };
  }, []);

  async function handleConnect() {
    if (isConnected) return;
    setStatus('pending');
    try {
      // Redirect to Audible OAuth / login-with-amazon flow
      const query = new URLSearchParams({ marketplace });
      if (shelfId) query.set('shelfId', shelfId);
      const response = await fetch(`/api/getAudibleAuthUrl?${query.toString()}`);
      if (!response.ok) throw new Error('Failed to get auth URL');
      const { authUrl } = await response.json() as { authUrl: string };
      window.location.href = authUrl;
    } catch {
      setStatus('error');
      setMessage('Could not initiate Audible connection. Please try again later.');
    }
  }

  async function handleDisconnect() {
    setStatus('pending');
    setMessage('');
    try {
      await disconnectAudible();
      setIsConnected(false);
      setStatus('success');
      setMessage('✅ Audible connection removed. You can connect again with a different marketplace.');
    } catch {
      setStatus('error');
      setMessage('Could not disconnect Audible right now. Please try again.');
    }
  }

  return (
    <main className={styles.page}>
      <div className={styles.card}>
        <div className={styles.logo}>🎧</div>
        <h1 className={styles.title}>Connect Your Audiobook Library</h1>
        <p className={styles.subtitle}>
          Link your Audible account to automatically import your full audiobook collection.
          Your credentials are never stored — we use OAuth to request read-only access to your library.
        </p>

        <div className={styles.form}>
          <label className={styles.label}>
            Audible Marketplace
            <select
              className={styles.select}
              value={marketplace}
              onChange={(e) => setMarketplace(e.target.value)}
            >
              {AUDIBLE_MARKETPLACES.map((m) => (
                <option key={m.code} value={m.code}>{m.label}</option>
              ))}
            </select>
          </label>

          <button
            className={styles.connectBtn}
            onClick={handleConnect}
            disabled={status === 'pending' || checkingStatus || isConnected}
          >
            {checkingStatus
              ? 'Checking connection…'
              : status === 'pending'
                ? '⏳ Redirecting…'
                : isConnected
                  ? '✅ Already Connected'
                  : '🔗 Connect with Audible'}
          </button>
          {isConnected && (
            <button
              className={styles.disconnectBtn}
              onClick={handleDisconnect}
              disabled={status === 'pending'}
            >
              🚪 Log out of Audible
            </button>
          )}

          {status === 'error' && (
            <p className={styles.error}>{message}</p>
          )}
          {status === 'success' && (
            <p className={styles.success}>{message}</p>
          )}
        </div>

        <div className={styles.divider}>or</div>

        <div className={styles.manual}>
          <h2 className={styles.manualTitle}>Upload a Book List Manually</h2>
          <p className={styles.manualDesc}>
            If you prefer not to connect Audible directly, you can export your library from any
            service and upload a CSV or text file. Go to any bookshelf and use the{' '}
            <strong>Upload List</strong> tab.
          </p>
        </div>

        <div className={styles.info}>
          <h3>How it works</h3>
          <ol className={styles.steps}>
            <li>Click <strong>Connect with Audible</strong> above.</li>
            <li>Sign in to Audible and grant read-only library access.</li>
            <li>We fetch your titles, authors and cover images.</li>
            <li>Your books are added to your chosen bookshelf automatically.</li>
            <li>Resync at any time to pick up new purchases.</li>
          </ol>
        </div>
      </div>
    </main>
  );
}
