# NowThenNext

A visual schedule and choice board app designed for children with Special Educational Needs (SEN).

**[Try it now at devjonny.github.io/NowThenNext](https://devjonny.github.io/NowThenNext/)**

---

## Purpose

NowThenNext helps carers communicate daily plans and offer choices to children using clear, familiar pictures. The app uses a calm, distraction-free design with soft colors and large touch targets suitable for tablet use.

## Features

### Visual Schedule (Now → Then → Next)
Upload pictures of places and activities, then select up to three to display as a visual schedule showing what's happening now, what comes next, and what follows after that. The current activity is gently highlighted to help the child focus.

### Food Choices
Upload pictures of food items and present 2-6 options for the child to choose from. When they tap their choice, a confirmation screen shows their selection.

### Activity Choices
Similar to food choices - upload activity pictures and let the child select from multiple options.

### Image Libraries
- **Places** - Locations and activities for schedules
- **Food** - Food items for meal choices
- **Activities** - Activities for choice boards
- **Favorites** - Quick access to frequently used images across all categories

### Backup & Restore
Export all images and settings to a JSON file for safekeeping, and restore from backup on any device.

### Cloud Sync (Google Drive)
Optionally sign in with Google to keep your images, custom learning cards, and phonics progress in sync across devices. Data is stored in your Drive's hidden **app data folder** (scope `drive.appdata`) — the app can only see its own file, never the rest of your Drive. Changes merge per-item (newest edit wins) with deletions propagating across devices, so two devices can both make changes without clobbering each other. See [Cloud Sync setup](#cloud-sync-setup) to configure the OAuth client.

### Install as App (PWA)
Install NowThenNext to your device's home screen for a full-screen, app-like experience. The app works offline after installation and automatically updates when new versions are available.

## Design Principles

- **Calm colors** - Soft teal, muted green, warm off-white (no harsh reds or bright yellows)
- **Large touch targets** - Minimum 48px for easy tapping
- **Minimal distractions** - Clean layouts with generous spacing
- **Accessible** - WCAG AA contrast compliance

## Technology

Built with Blazor WebAssembly and Tailwind CSS. All data is stored locally in the browser using IndexedDB - no account or internet connection required after initial load. Optional Google Drive sync layers on top without changing the offline-first model.

## Cloud Sync setup

Cloud Sync needs a Google OAuth 2.0 **Web application** client id. It's a public identifier, but each deployment registers its own because the allowed origins differ.

1. In the [Google Cloud Console](https://console.cloud.google.com/), create (or pick) a project.
2. **APIs & Services → Library**: enable the **Google Drive API**.
3. **OAuth consent screen**: choose *External*; add the scope `https://www.googleapis.com/auth/drive.appdata`; add your Google account under *Test users* (or publish the app).
4. **Credentials → Create credentials → OAuth client ID → Web application**. Under *Authorized JavaScript origins* add (origin = scheme + host + port, **no path**):
   - `http://localhost:5161` (local dev)
   - `https://<your-username>.github.io` (GitHub Pages — the `/NowNextThen/` sub-path is not part of the origin, so this one entry covers it)

   No redirect URIs are needed — Google Identity Services uses a popup token flow.
5. Add the generated client id as a repository variable named `GOOGLE_CLIENT_ID` (**Settings → Secrets and variables → Actions → Variables**). The deploy workflow injects it into the published `googleAuth.js` and fails if it's missing.
6. For local dev, temporarily paste the client id into `CLIENT_ID` in [`src/NowThenNext/wwwroot/js/googleAuth.js`](src/NowThenNext/wwwroot/js/googleAuth.js) — don't commit it; the workflow expects the placeholder.

Once configured, open **Settings → Cloud Sync → Sign in to Google Drive**. The Home screen shows a small status dot (grey = signed out, teal pulse = syncing, green = synced, amber = error).

## License

This project is licensed under the [PolyForm Noncommercial License 1.0.0](LICENSE).

**You are free to:**
- Use the app for personal, educational, or charitable purposes
- Modify and share the code
- Contribute improvements back to the project

**You may not:**
- Use this software for commercial purposes
- Sell or monetize the software or derivatives

This ensures NowThenNext remains a free resource for families and carers of children with SEN.
