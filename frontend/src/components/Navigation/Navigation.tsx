import { Link, useLocation } from 'react-router-dom';
import styles from './Navigation.module.css';

export function Navigation() {
  const { pathname } = useLocation();

  const links = [
    { to: '/', label: '🏠 Home' },
    { to: '/library', label: '📚 My Library' },
    { to: '/bookshelf', label: '🗂️ Bookshelves' },
    { to: '/wallpaper', label: '🖼️ Generate Wallpaper' },
    { to: '/connect', label: '🔗 Connect Audible' },
    { to: '/privacy-notice', label: '🔒 Privacy Notice' },
  ];

  return (
    <nav className={styles.nav}>
      <div className={styles.brand}>
        <Link to="/" className={styles.brandLink}>
          📖 BookshelfWallpaper
        </Link>
      </div>
      <ul className={styles.links}>
        {links.map(({ to, label }) => (
          <li key={to}>
            <Link
              to={to}
              className={`${styles.link} ${pathname === to ? styles.active : ''}`}
            >
              {label}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
