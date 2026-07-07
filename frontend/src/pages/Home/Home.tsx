import { Link } from 'react-router-dom';
import styles from './Home.module.css';

export function Home() {
  return (
    <main className={styles.hero}>
      <div className={styles.content}>
        <h1 className={styles.title}>📚 Bookshelf Wallpaper</h1>
        <p className={styles.subtitle}>
          Turn your audiobook library into a stunning virtual bookshelf — perfect for desktop
          wallpapers, Microsoft Teams or Zoom backgrounds.
        </p>

        <div className={styles.features}>
          <FeatureCard
            icon="🎧"
            title="Audible Integration"
            description="Connect your Audible account and automatically import your full library."
          />
          <FeatureCard
            icon="📤"
            title="Upload a Book List"
            description="Upload a CSV or text file with your book titles to build a bookshelf."
          />
          <FeatureCard
            icon="🖼️"
            title="Generate Wallpapers"
            description="Create beautiful bookshelf images sized for desktops, Teams, or Zoom."
          />
          <FeatureCard
            icon="🗂️"
            title="Manage Shelves"
            description="Create multiple bookshelves, add or remove books, and keep them in sync."
          />
        </div>

        <div className={styles.cta}>
          <Link to="/bookshelf" className={styles.ctaPrimary}>
            Get Started →
          </Link>
          <Link to="/connect" className={styles.ctaSecondary}>
            Connect Audible
          </Link>
        </div>
      </div>
    </main>
  );
}

function FeatureCard({
  icon,
  title,
  description,
}: {
  icon: string;
  title: string;
  description: string;
}) {
  return (
    <div className={styles.featureCard}>
      <div className={styles.featureIcon}>{icon}</div>
      <h3 className={styles.featureTitle}>{title}</h3>
      <p className={styles.featureDesc}>{description}</p>
    </div>
  );
}
