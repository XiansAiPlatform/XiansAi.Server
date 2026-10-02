# Build Your Own Xians UI

This folder is a starting point for building a **custom user interface** for Xians.ai
on top of AdminApi. That could be an admin console, a task inbox for human-in-the-loop approvals, an
embedded chat widget, a support dashboard, anything that lets a person manage or
converse with your agents.

There is no single required stack. AdminApi is plain, versioned REST (plus one SSE
stream for live messages), so your UI can be written in any language or framework such as
React, Vue, Svelte, Angular, plain JavaScript, iOS/Android native, a CLI, whatever fits
your product. This folder gives you:

1. **[docs/](docs/)**: a framework-agnostic guide covering everything a custom UI needs
   to decide and implement against AdminApi, regardless of what you build it with.
2. **[examples/react-vite-tailwind/](examples/react-vite-tailwind/)**: one concrete, minimal
   reference implementation (React 19 + Vite + Tailwind CSS) that follows the guide, so you can
   see the concepts as working code. It's a starting example, not a template you must
   use as-is.

## Getting Started

| Doc | Description |
|---|---|
| [Overview](docs/overview.md) | What AdminApi is and what a custom UI built on it has to do |
| [Connecting to a server](docs/connecting-to-server.md) | Which Xians deployment you're pointing at, and what you need from it |
| [Authentication](docs/authentication.md) | The two ways to call AdminApi: a service API key, or your signed-in user's own identity via OIDC, with RBAC deciding what they can do |
| [Calling AdminApi](docs/integration-options.md) | REST plus one SSE stream, no SDK required, and the endpoint groups available |
| [Optional features](docs/optional-features.md) | What's required vs. what you can add later |

Take a look at [examples/react-vite-tailwind](examples/react-vite-tailwind/) to see it wired up.
