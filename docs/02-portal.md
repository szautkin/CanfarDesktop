# Portal — Session Management

Manage CANFAR Science Portal sessions from the desktop.

![Portal](images/portal.png)

## Layout
Platform load, storage and batch jobs across the top; active sessions the full width; then the CANFAR
images beside recent launches. In a window narrower than about 1000 pixels the cards stack in one column,
in the same order.

## Features
- **Launch sessions** — the accented **Launch session** button on Active sessions opens the launch form:
  JupyterLab, CARTA, NoVNC and more (Standard), your own image from any registry (Advanced), and batch
  jobs (Headless), with custom resource allocation. **Use this image** on an image opens it with that image
  chosen. If a launch fails, the form comes back as you left it, with the reason
- **CANFAR images** — the images a launch can start, filtered by session type and then by project, with
  what each holds once inspected
- **Active sessions** — View running sessions with status badges, resource display
- **Session management** — Open in browser, renew, delete, view events and logs
- **Batch jobs** — Monitor and manage batch processing sessions
- **Platform load** — Real-time CPU, RAM, and instance metrics
- **Recent launches** — Quick access to previously launched configurations
