# Projects

Projects allow users to create isolated instances of a flow definition by using a shared flow definition. This isolated flow in a project can override the shared flow definition with project specific values for:

- Flow Start Parameters
- Flow Instructions

## Information Architecture

Project relates to a single flow definition. The project can then optionally override the flow definition with project specific values for:

- Flow Start Parameters
- Flow Instructions

### Attributes

- `ProjectId`: The ID of the project.
- `Owner`: The owner of the project.
- `DefinitionId`: The ID of the flow definition.
- `ProjectActivities`: The activities of the project.
  - `ActivityName`: The name of the activity.
  - `ActivityInstructions`: The instructions of the activity.
    - `Name`: The name of the instruction.
    - `Version`: The version of the instruction.
- `CreatedAt`: The date and time the project was created.

## Functional Requirements

### Core Functionality

1. Users must be able to create new projects based on existing flow definitions
2. Users must be able to override flow start parameters for their specific project
3. Users must be able to customize flow instructions for their project
4. Users must be able to run a project
5. Users must be able to delete a project
6. Users must be able to view a project
7. Users must be able to share (Read/write permissions) a project with other users
8. Users must be able to unshare a project with other users

## Business Rules

1. A project must be associated with exactly one flow definition
2. Project names must be unique within an owner's workspace
3. Project modifications must not affect the original flow definition
4. Projects cannot change the fundamental structure of the flow definition
