# Document Identity

Which stored document a `POST /api/agent/documents/save` with `options.useKeyAsIdentifier = true`
replaces.

## Identity

```text
tenant_id + agent_id + type + key + activation_name + participant_id
```

These are exactly the fields Xians.Lib stamps on every save (`AgentId`, and inside a workflow or
activity `ActivationName = XiansContext.SafeIdPostfix`, `ParticipantId = XiansContext.SafeParticipantId`)
and filters on when it reads a document back with `GetByKeyAsync`. Resolving the save by the same
fields means a save replaces only a document its caller can read:

- Two activations of one agent saving the same `type` + `key` get two documents.
- Two agents saving the same `type` + `key` get two documents. A save can never find, let alone
  replace, another agent's document.
- Two participants get two documents. A null `participant_id` (a scheduled run, workflow code
  with no participant, or an admin-created record) is a slot of its own, as is a null
  `activation_name` (a save from outside any workflow context).
- Empty or whitespace `activationName` / `participantId` are stored as null so that "missing" and
  "empty" are the same slot.

Previously the save resolved by `tenant_id + type + key` alone, so every instance shared one
document and only the last writer could read it back.

## Save outcomes

| Caller's slot | `overwrite` | Result |
| --- | --- | --- |
| empty | any | insert |
| occupied | `true` (Xians.Lib default) | replace in place, keeping `id` and `createdAt` |
| occupied | `false` | 409, nothing written |
| occupied by another agent, activation or participant | any | not a match: treated as empty |

## Reads

The lib's `GetByKeyAsync` goes through `/query` with `AgentId`, `Type`, `Key` and, in context,
`ActivationName` and `ParticipantId`. A non-empty filter is matched exactly. When the context has
no participant the clause is dropped and the newest matching document across participant slots is
returned. `/get-by-key` (not used by the lib) still resolves by tenant + type + key only.

## Not enforced by the store

There is no unique index on the identity. Concurrent first writes of the same identity, or keyed
saves made without `useKeyAsIdentifier`, can still produce two documents in one slot; reads then
return the newest.
