import styles from './PrivacyNotice.module.css';

export function PrivacyNotice() {
  return (
    <main className={styles.page}>
      <article className={styles.card}>
        <h1 className={styles.title}>Consent Privacy Notice</h1>
        <p className={styles.updated}>Last updated: 11 July 2026</p>

        <section className={styles.section}>
          <h2>Who we are</h2>
          <p>
            BookshelfWallpaper.com provides tools to generate bookshelf wallpaper images from book
            metadata and user-uploaded lists.
          </p>
        </section>

        <section className={styles.section}>
          <h2>Data we process</h2>
          <p>
            We process your account identifiers, bookshelf settings, uploaded title lists, and
            selected book metadata needed to create and store your generated wallpapers.
          </p>
        </section>

        <section className={styles.section}>
          <h2>Audible connection consent</h2>
          <p>
            If you choose to connect Audible, we request access only to import your audiobook
            library data. We use this data to display your books and generate wallpapers in your
            account.
          </p>
        </section>

        <section className={styles.section}>
          <h2>How data is used and stored</h2>
          <p>
            Data is used for core app functionality, support, and service reliability. Storage is
            hosted on Microsoft Azure services, including database and object storage resources.
          </p>
        </section>

        <section className={styles.section}>
          <h2>Your choices</h2>
          <p>
            You can disconnect third-party integrations at any time and request deletion of stored
            bookshelf and generated image data associated with your account.
          </p>
        </section>

        <section className={styles.section}>
          <h2>Contact</h2>
          <p>
            For privacy requests, contact: <strong>privacy@bookcasewallpaper.com</strong>
          </p>
        </section>
      </article>
    </main>
  );
}
