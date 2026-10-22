// Restores every message to its seeded legacy status and removes the migration claim,
// so each benchmark scenario starts from the same pre-backfill state.
const collection = db.getCollection('conversation_message');
const result = collection.updateMany(
  {},
  [{ $set: { status: { $ifNull: ['$seed_status', '$$REMOVE'] } } }]);
db.getCollection('data_migrations').deleteOne({ _id: 'conversation-message-non-unread-backfill' });
print(`reset: restored ${result.modifiedCount} messages, claim removed`);
