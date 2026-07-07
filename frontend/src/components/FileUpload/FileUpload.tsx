import { useCallback } from 'react';
import { useDropzone } from 'react-dropzone';
import styles from './FileUpload.module.css';

interface FileUploadProps {
  onFile: (file: File) => void;
  accept?: Record<string, string[]>;
  label?: string;
  loading?: boolean;
}

export function FileUpload({ onFile, accept, label, loading }: FileUploadProps) {
  const onDrop = useCallback(
    (accepted: File[]) => {
      if (accepted[0]) onFile(accepted[0]);
    },
    [onFile],
  );

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: accept ?? {
      'text/plain': ['.txt'],
      'text/csv': ['.csv'],
    },
    multiple: false,
    disabled: loading,
  });

  return (
    <div
      {...getRootProps()}
      className={`${styles.zone} ${isDragActive ? styles.active : ''} ${loading ? styles.loading : ''}`}
    >
      <input {...getInputProps()} />
      {loading ? (
        <p className={styles.hint}>⏳ Processing…</p>
      ) : isDragActive ? (
        <p className={styles.hint}>📂 Drop the file here…</p>
      ) : (
        <p className={styles.hint}>
          {label ?? '📄 Drag & drop a file here, or click to select'}
        </p>
      )}
    </div>
  );
}
