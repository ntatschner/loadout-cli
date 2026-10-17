# StoreList

StoreList is the left of the Memory page: a project's topics in its three stores, each with what it means.

| Store | Label | Hint |
|---|---|---|
| `project` | This project | Travels with the workspace. |
| `user` | Your work, any project | Travels with the workspace. |
| `machine` | This machine only | Never synced. Tagged "read-only here". |

- Each topic shows its name, what it is about, and how many facts it holds. The open one has the accent edge and `aria-current="page"`.
- "Recorded by the agent, outside the workspace" shows what `loadout memory import` would bring in and which topics differ from the workspace's copy. It is shown, never written from here.
