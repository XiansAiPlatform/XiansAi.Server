// Seeds a mock conversation_message collection that looks like the state before the
// read-status backfill ran: legacy rows that cannot be unread are stored as Unread or with
// no status. Run with: mongosh <db> --eval 'var DAYS=14, PER_DAY=150000' seed.js
//
// Each document keeps its original status in seed_status so reset.js can restore it
// between benchmark runs without reseeding.

const crypto = require('crypto');

const days = typeof DAYS !== 'undefined' ? DAYS : 14;
const perDay = typeof PER_DAY !== 'undefined' ? PER_DAY : 150000;
const total = days * perDay;
const insertBatchSize = 5000;
const messagesPerThread = 30;
const tenantCount = 20;

// Share of each kind of message in an agent conversation: a user turn produces reasoning
// and tool steps before the agent reply.
const messageMix = [
  { direction: 'Incoming', type: 'Chat', weight: 22, textMin: 200, textMax: 1200 },
  { direction: 'Incoming', type: 'Data', weight: 2, textMin: 100, textMax: 400 },
  { direction: 'Incoming', type: 'File', weight: 1, textMin: 80, textMax: 200 },
  { direction: 'Outgoing', type: 'Chat', weight: 22, textMin: 400, textMax: 1600 },
  { direction: 'Outgoing', type: 'File', weight: 1, textMin: 80, textMax: 200 },
  { direction: 'Outgoing', type: 'Reasoning', weight: 24, textMin: 600, textMax: 3000 },
  { direction: 'Outgoing', type: 'Tool', weight: 24, textMin: 200, textMax: 1200 },
  { direction: 'Outgoing', type: 'Data', weight: 3, textMin: 100, textMax: 400 },
  { direction: 'Outgoing', type: 'Handoff', weight: 1, textMin: 100, textMax: 300 },
];
const totalWeight = messageMix.reduce((sum, kind) => sum + kind.weight, 0);

function pickKind() {
  let roll = Math.random() * totalWeight;
  for (const kind of messageMix) {
    roll -= kind.weight;
    if (roll < 0) return kind;
  }
  return messageMix[0];
}

function randomHex(length) {
  return crypto.randomBytes(Math.ceil(length / 2)).toString('hex').slice(0, length);
}

// Stored text is AES-GCM ciphertext in base64, so it is effectively random and does not
// compress. A pool of random strings keeps generation fast without making pages compressible.
const textPool = [];
for (let i = 0; i < 4000; i++) {
  textPool.push(crypto.randomBytes(3000).toString('base64'));
}

function encryptedText(kind) {
  const length = kind.textMin + Math.floor(Math.random() * (kind.textMax - kind.textMin));
  const source = textPool[Math.floor(Math.random() * textPool.length)];
  return source.slice(0, length);
}

// Legacy state: agent Chat/File rows are a mix of Read and Unread; every other row was
// written as Unread or without a status, which is what the backfill rewrites.
function legacyStatus(kind) {
  const canBeUnread = kind.direction === 'Outgoing' && (kind.type === 'Chat' || kind.type === 'File');
  if (canBeUnread) {
    return Math.random() < 0.55 ? 'Read' : 'Unread';
  }
  return Math.random() < 0.8 ? 'Unread' : null;
}

function objectIdAt(date) {
  const seconds = Math.floor(date.getTime() / 1000).toString(16).padStart(8, '0');
  return ObjectId(seconds + randomHex(16));
}

const tenants = Array.from({ length: tenantCount }, (_, i) => `tenant-${i + 1}`);
const threadCount = Math.ceil(total / messagesPerThread);
const threads = Array.from({ length: threadCount }, (_, i) => {
  const tenant = tenants[i % tenantCount];
  const participant = `user-${i}@example.com`;
  const agent = `agent-${i % 40}`;
  return {
    tenantId: tenant,
    threadId: ObjectId().toString(),
    participantId: participant,
    workflowId: `${tenant}:${agent}:Chat:${participant}`,
    workflowType: `${agent}:Chat`,
  };
});

const collection = db.getCollection('conversation_message');
collection.drop();
db.getCollection('data_migrations').drop();

// Mirrors conversation_message in mongodb-indexes.yaml so every status write pays the same
// index maintenance as production.
collection.createIndex({ tenant_id: 1, workflow_id: 1, participant_id: 1, created_at: -1 }, { name: 'bot_message_lookup' });
collection.createIndex({ tenant_id: 1, thread_id: 1, created_at: -1 }, { name: 'thread_message_lookup' });
collection.createIndex({ tenant_id: 1, thread_id: 1, message_type: 1, created_at: -1 }, { name: 'thread_chat_message_lookup' });
collection.createIndex({ tenant_id: 1, workflow_id: 1, participant_id: 1, scope: 1, created_at: -1 }, { name: 'bot_scope_lookup', sparse: true });
collection.createIndex({ tenant_id: 1, thread_id: 1, scope: 1, created_at: -1 }, { name: 'thread_scope_lookup' });
collection.createIndex({ tenant_id: 1, thread_id: 1, direction: 1, scope: 1, created_at: -1 }, { name: 'thread_incoming_origin_lookup' });
collection.createIndex({ tenant_id: 1, request_id: 1, direction: 1 }, { name: 'outgoing_request_id_lookup', sparse: true });
collection.createIndex({ tenant_id: 1 }, { name: 'tenant_lookup' });
collection.createIndex({ tenant_id: 1, status: 1 }, { name: 'message_status' });
collection.createIndex({ message_type: 1 }, { name: 'message_type_1' });
collection.createIndex({ expires_at: 1 }, { name: 'conversation_message_ttl', expireAfterSeconds: 0 });

const end = Date.now();
const start = end - days * 24 * 60 * 60 * 1000;
const retention = 180 * 24 * 60 * 60 * 1000;
const startedAt = Date.now();

let inserted = 0;
while (inserted < total) {
  const batch = [];
  const size = Math.min(insertBatchSize, total - inserted);
  for (let i = 0; i < size; i++) {
    const kind = pickKind();
    const thread = threads[Math.floor(Math.random() * threads.length)];
    const createdAt = new Date(start + Math.random() * (end - start));
    const status = legacyStatus(kind);
    const message = {
      _id: objectIdAt(createdAt),
      thread_id: thread.threadId,
      request_id: kind.direction === 'Outgoing' ? randomHex(24) : null,
      tenant_id: thread.tenantId,
      created_at: createdAt,
      updated_at: createdAt,
      created_by: thread.participantId,
      direction: kind.direction,
      text: encryptedText(kind),
      data: kind.type === 'Tool' || kind.type === 'Data'
        ? { tool: `tool-${Math.floor(Math.random() * 25)}`, args: { query: randomHex(40), limit: 20 } }
        : null,
      participant_id: thread.participantId,
      scope: Math.random() < 0.3 ? `topic-${Math.floor(Math.random() * 5)}` : null,
      hint: null,
      task_id: null,
      workflow_id: thread.workflowId,
      workflow_type: thread.workflowType,
      message_type: kind.type,
      origin: kind.direction === 'Incoming' ? 'web' : null,
      expires_at: new Date(createdAt.getTime() + retention),
    };
    if (status !== null) {
      message.status = status;
      message.seed_status = status;
    }
    batch.push(message);
  }
  collection.insertMany(batch, { ordered: false });
  inserted += size;
  if (inserted % 250000 === 0 || inserted === total) {
    print(`seeded ${inserted}/${total} messages (${Math.round((Date.now() - startedAt) / 1000)} s)`);
  }
}

const stats = collection.stats();
print(`done: ${stats.count} messages over ${days} days, data ${(stats.size / 1e9).toFixed(2)} GB, ` +
  `storage ${(stats.storageSize / 1e9).toFixed(2)} GB, indexes ${(stats.totalIndexSize / 1e9).toFixed(2)} GB`);
