// MongoDB Playground
// Use Ctrl+Space inside a snippet or a string literal to trigger completions.

// The current database to use.
use("99xio");

var agentName = "Power of Attorney Agent";

// Find a document in a collection.
db.getCollection("flow_definitions").deleteMany({
    "agent": agentName
});

// Find a document in a collection.
db.getCollection("agents").deleteMany({
    "name": agentName
});

