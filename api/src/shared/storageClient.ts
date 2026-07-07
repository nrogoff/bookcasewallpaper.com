import { BlobServiceClient, ContainerClient } from '@azure/storage-blob';

let blobServiceClient: BlobServiceClient | null = null;

function getBlobServiceClient(): BlobServiceClient {
  if (!blobServiceClient) {
    const connectionString = process.env.AZURE_STORAGE_CONNECTION_STRING;
    if (!connectionString) {
      throw new Error('AZURE_STORAGE_CONNECTION_STRING environment variable must be set');
    }
    blobServiceClient = BlobServiceClient.fromConnectionString(connectionString);
  }
  return blobServiceClient;
}

export async function getContainerClient(containerName: string): Promise<ContainerClient> {
  const client = getBlobServiceClient();
  const containerClient = client.getContainerClient(containerName);
  await containerClient.createIfNotExists({ access: 'blob' });
  return containerClient;
}

export async function uploadBuffer(
  containerName: string,
  blobName: string,
  buffer: Buffer,
  contentType: string,
): Promise<string> {
  const containerClient = await getContainerClient(containerName);
  const blockBlobClient = containerClient.getBlockBlobClient(blobName);
  await blockBlobClient.uploadData(buffer, {
    blobHTTPHeaders: { blobContentType: contentType },
  });
  return blockBlobClient.url;
}

export async function uploadFromUrl(
  containerName: string,
  blobName: string,
  sourceUrl: string,
): Promise<string> {
  const containerClient = await getContainerClient(containerName);
  const blockBlobClient = containerClient.getBlockBlobClient(blobName);
  await blockBlobClient.syncCopyFromURL(sourceUrl);
  return blockBlobClient.url;
}

export async function getStorageUrl(containerName: string, blobName: string): Promise<string> {
  const client = getBlobServiceClient();
  const containerClient = client.getContainerClient(containerName);
  return containerClient.getBlockBlobClient(blobName).url;
}
