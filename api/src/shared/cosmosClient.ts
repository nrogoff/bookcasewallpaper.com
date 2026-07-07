import { CosmosClient, Database, Container } from '@azure/cosmos';

let client: CosmosClient | null = null;
let database: Database | null = null;

function getClient(): CosmosClient {
  if (!client) {
    const endpoint = process.env.COSMOS_ENDPOINT;
    const key = process.env.COSMOS_KEY;
    if (!endpoint || !key) {
      throw new Error('COSMOS_ENDPOINT and COSMOS_KEY environment variables must be set');
    }
    client = new CosmosClient({ endpoint, key });
  }
  return client;
}

async function getDatabase(): Promise<Database> {
  if (!database) {
    const dbName = process.env.COSMOS_DATABASE ?? 'BookshelfWallpaper';
    const { database: db } = await getClient()
      .databases.createIfNotExists({ id: dbName });
    database = db;
  }
  return database;
}

export async function getContainer(containerId: string): Promise<Container> {
  const db = await getDatabase();
  const partitionKey = containerId === 'bookshelves' ? '/userId' : '/id';
  const { container } = await db.containers.createIfNotExists({
    id: containerId,
    partitionKey: { paths: [partitionKey] },
  });
  return container;
}

export async function getBookshelvesContainer(): Promise<Container> {
  return getContainer('bookshelves');
}

export async function getJobsContainer(): Promise<Container> {
  return getContainer('coverFetchJobs');
}
