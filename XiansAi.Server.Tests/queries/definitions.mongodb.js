/* global use, db */
// MongoDB Playground
// Use Ctrl+Space inside a snippet or a string literal to trigger completions.

// The current database to use.
use('99xio');

// Search for documents in the current collection.
db.getCollection('flow_definitions')
  .find(
    {
      "permissions.owner_access": "github|1892961"
      /*
      * Filter
      * fieldA: value or expression
      */
    },
    {
      /*
      * Projection
      * _id: 0, // exclude _id
      * fieldA: 1 // include field
      */
    }
  )
  .sort({
    createdAt: -1,
    /*
    * fieldA: 1 // ascending
    * fieldB: -1 // descending
    */
  });
