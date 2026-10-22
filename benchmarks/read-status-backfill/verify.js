// Checks the end goal: nothing that cannot be unread is left un-Read, and every
// outgoing Chat/File message kept its seeded status.
const collection = db.getCollection('conversation_message');
const outgoingChatOrFile = { direction: 'Outgoing', message_type: { $in: ['Chat', 'File'] } };

const remaining = collection.countDocuments({ status: { $ne: 'Read' }, $nor: [outgoingChatOrFile] });
const seededToFix = collection.countDocuments({ seed_status: { $ne: 'Read' }, $nor: [outgoingChatOrFile] });
const unreadNow = collection.countDocuments({ ...outgoingChatOrFile, status: { $ne: 'Read' } });
const unreadSeeded = collection.countDocuments({ ...outgoingChatOrFile, seed_status: { $ne: 'Read' } });
const claim = db.getCollection('data_migrations').findOne({ _id: 'conversation-message-non-unread-backfill' });

print(`verify: rows that must be Read but are not: ${remaining} (seeded: ${seededToFix})`);
print(`verify: outgoing chat/file unread: ${unreadNow} (seeded: ${unreadSeeded}) -> ${unreadNow === unreadSeeded ? 'unchanged' : 'CHANGED'}`);
print(`verify: claim: ${claim ? EJSON.stringify(claim) : 'none'}`);
