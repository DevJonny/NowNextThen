# NowNextThen - Claude Code Context

> Note: the product is branded **NowNextThen** (repo `DevJonny/NowNextThen`), but the .NET project folder, assembly, and namespace remain `NowThenNext` by design — file paths below are correct as written.

## Project Overview
Visual schedule, food choice, and activity choice app for children with SEN (Special Educational Needs). Displays pictures in Now/Next/Then format to communicate daily plans and offers visual choice boards for food and activities.

## Tech Stack
- **Framework**: Blazor WebAssembly (WASM) - .NET 10 RC (preview)
- **Language**: C# / .NET
- **Styling**: Tailwind CSS CDN with custom SEN-friendly theme
- **Storage**: Browser IndexedDB via `wwwroot/js/indexeddb.js` (base64 encoded images, hundreds of MB capacity)
- **Testing**: Playwright E2E tests with xUnit v3
- **Font**: Nunito (Google Fonts) - rounded, friendly typography

## Key Design Principles
1. **Calm & Clear**: Soft colors (teal primary #5B9A9A, muted green accent #7BA893, phonics blue #7BA3C4, off-white bg #F9F7F3), generous spacing, minimum 48px touch targets
2. **No Harsh Colors**: Avoid pure red/bright yellow - use calm amber #D4A06A for warnings
3. **Accessibility**: WCAG AA contrast (~8.5:1 ratio), large fonts (18px base, 24px+ labels), rounded sans-serif
4. **Simplicity**: Minimal UI, no distractions, straightforward navigation
5. **Visual-First**: Large images, clear icons, visual feedback on interactions
6. **Slow Animations**: Calming, gentle transitions (3s pulse, 7s auto-advance)

## Project Structure
```
src/NowThenNext/           # Main Blazor WASM application
├── Pages/                 # Razor pages for each view
├── Components/            # Reusable Blazor components
├── Models/                # Data models (ImageItem, etc.)
├── Services/              # Business logic (ImageStorageService)
└── wwwroot/               # Static assets

tests/NowThenNext.Tests.E2E/  # E2E tests with Playwright
```

## Core Features
1. **Image Libraries**: Places, Food, Activities
2. **Schedule Builder**: Select up to 3 places for Now/Next/Then display
3. **Choice Boards**: Food Choices and Activity Choices (2-6 items)
4. **Favorites**: Cross-category favorites view
5. **Backup/Restore**: Export/import data as JSON
6. **Phonics Flashcards**: UK Letters and Sounds Phases 2-5 with sequential unlocking and progress tracking
7. **Cloud Sync (Google Drive)**: Optional cross-device sync of images, learning cards, and phonics progress via Drive `appDataFolder`

## Current Status
- Branch: `ralph/phonics-flashcards` (phonics feature in progress)
- Completed: US-001 through US-037 (37/37 user stories - **MVP COMPLETE**)
- Phonics: US-038 through US-048 complete (13/13 phonics stories - **PHONICS COMPLETE**)
- Activities category fully implemented with library, choices selection, display, and E2E tests

## Important Files
- [`prd.json`](fleet-file://3j4f3alj5183smoqcslr/Users/jonnyolliff-lee/code/NowThenNext/prd.json?type=file&root=%252F) - Full PRD with all 37 user stories
- [`progress.txt`](fleet-file://3j4f3alj5183smoqcslr/Users/jonnyolliff-lee/code/NowThenNext/progress.txt?type=file&root=%252F) - Detailed implementation history and learnings
- [`src/NowThenNext/Pages/Home.razor`](fleet-file://3j4f3alj5183smoqcslr/Users/jonnyolliff-lee/code/NowThenNext/src/NowThenNext/Pages/Home.razor?type=file&root=%252F) - Main menu/navigation
- [`src/NowThenNext/Services/LocalStorageImageService.cs`](fleet-file://3j4f3alj5183smoqcslr/Users/jonnyolliff-lee/code/NowThenNext/src/NowThenNext/Services/LocalStorageImageService.cs?type=file&root=%252F) - Data persistence layer
- [`src/NowThenNext/wwwroot/index.html`](fleet-file://3j4f3alj5183smoqcslr/Users/jonnyolliff-lee/code/NowThenNext/src/NowThenNext/wwwroot/index.html?type=file&root=%252F) - Tailwind config & JS interop functions
- [`tests/NowThenNext.Tests.E2E/`](fleet-file://3j4f3alj5183smoqcslr/Users/jonnyolliff-lee/code/NowThenNext/tests/NowThenNext.Tests.E2E?type=file&root=%252F) - E2E test suite (54 tests)

## Development Commands
```bash
# Run application
dotnet run --project src/NowThenNext

# Run E2E tests
dotnet test tests/NowThenNext.Tests.E2E

# Build project
dotnet build
```

## Design Patterns
- **Category-based architecture**: All images categorized as Places, Food, or Activities
- **IndexedDB JSON storage**: Images stored as base64 JSON strings in IndexedDB (one record per store, key `"data"`)
- **Reusable components**: ImageTile component shared across all libraries
- **Parallel workflows**: Food Choices and Activity Choices follow same pattern as templates
- **Static data service**: Phonics GPC data is hardcoded in `PhonicsDataService` (singleton), progress tracked in `PhonicsProgressService` (scoped, uses IndexedDB store `phonics-progress`)
- **Cloud Sync**: `Services/Sync/SyncService.cs` (scoped) orchestrates sync; `Services/Sync/SyncMergeEngine.cs` is the pure, unit-tested per-entity last-write-wins + tombstone merge. Auth and Drive HTTP live in JS (`wwwroot/js/googleAuth.js` = GIS token flow, `wwwroot/js/googleDrive.js` = Drive `appDataFolder` file access exposing `window.driveSync`). The whole dataset syncs as one JSON file `nownextthen-sync.json`. Storage services raise a `DataChanged` event that `SyncService` debounces (2s) — storage services never depend on `SyncService` (no DI cycle). `SyncService.ApplyMergedAsync` on each store writes pulled data **without** raising `DataChanged` (avoids a sync loop). Init runs once in `MainLayout.OnAfterRenderAsync(firstRender)`. Equal timestamps are broken by `SyncContent.Compare` (tombstone wins, then field-by-field) so devices converge. Drive v3 has no compare-and-swap, so uploads check the file's `version` first (412 → re-merge); uploads are resumable because the envelope can exceed 5 MB. Google sign-in needs a user gesture, so an expired token yields `SyncStatus.NeedsReconnect` rather than a popup. **OAuth `CLIENT_ID` in `googleAuth.js` stays a placeholder in git**; the deploy workflow injects it from the `GOOGLE_CLIENT_ID` repo variable — see README "Cloud Sync setup".

## Routing Map
| Route | Page | Description |
|-------|------|-------------|
| `/` | Home.razor | Main menu: Places, Food, Activities, Plan the Day, Food Choices, Activity Choices, Favorites, Learning Cards |
| `/places` | PlacesLibrary.razor | Places image library |
| `/food` | FoodLibrary.razor | Food image library |
| `/activities` | ActivitiesLibrary.razor | Activities image library |
| `/upload` | Upload.razor | Image upload (optional category param) |
| `/upload/{category}` | Upload.razor | Image upload with pre-selected category |
| `/plan` | PlanDay.razor | Schedule selection (Now/Next/Then) |
| `/schedule` | ScheduleDisplay.razor | Schedule display (`?ids=id1,id2,id3`) |
| `/food-choices` | FoodChoices.razor | Food choice selection |
| `/food-display` | FoodDisplay.razor | Food display (`?ids=id1,id2,...`) |
| `/activity-choices` | ActivityChoices.razor | Activity choice selection |
| `/activity-display` | ActivityDisplay.razor | Activity display (`?ids=id1,id2,...`) |
| `/favorites` | Favorites.razor | Favorited images from all categories |
| `/settings` | Settings.razor | Backup/restore data |
| `/phonics` | PhonicsPhases.razor | Phonics phase selection (Phase 2-5) |
| `/phonics/{phaseId}` | PhonicsSoundList.razor | Sound list within a phase |
| `/phonics/{phaseId}/card/{graphemeId}` | PhonicsCard.razor | Flashcard display with navigation |

## Data Model

### ImageItem
```csharp
public class ImageItem
{
    public string Id { get; set; }           // GUID
    public string Base64Data { get; set; }   // Base64 image data
    public string Label { get; set; }        // Optional label
    public ImageCategory Category { get; set; } // Places/Food/Activities
    public bool IsFavorite { get; set; }     // Favorited flag
    public DateTime CreatedAt { get; set; }  // Creation timestamp
    public DateTime LastModifiedUtc { get; set; } // Sync: newest wins on merge
    public bool IsDeleted { get; set; }      // Sync: soft-delete tombstone
}
```
> `ImageItem`, `LearningCategory`, and `LearningCard` implement `ISyncEntity` (`Id` + `LastModifiedUtc` + `IsDeleted`). Deletes are **soft** (tombstones) so they propagate across devices; user-facing reads filter out `IsDeleted`. Phonics progress is stored as a `Dictionary<string, PhonicsCardProgress>` (per-card `Completed` + `LastModifiedUtc`), migrated transparently from the old `HashSet<string>` shape on read.

### ImageCategory Enum
```csharp
public enum ImageCategory
{
    Places = 0,
    Food = 1,
    Activities = 2  // Added in US-032
}
```

## Common Code Patterns

### Service Injection
```razor
@inject IImageStorageService ImageStorage
@inject IJSRuntime JSRuntime
@inject NavigationManager Navigation
@inject IPhonicsDataService PhonicsData
@inject IPhonicsProgressService PhonicsProgress
```

### Tailwind + Custom Colors
```razor
<!-- Use calm-* classes defined in Tailwind config (in wwwroot/index.html) -->
<div class="bg-calm-bg text-calm-text">
  <button class="bg-calm-primary hover:bg-calm-primary-dark">
```

### Image Compression (JS Interop)
```csharp
// Compress to max 800px, 0.7 quality
var compressed = await JSRuntime.InvokeAsync<string>(
    "compressImage", dataUrl, 800, 0.7);
```

### Storage Operations
```csharp
// Get by category
var images = await ImageStorage.GetImagesByCategoryAsync(ImageCategory.Places);

// Save image
await ImageStorage.SaveImageAsync(new ImageItem { ... });

// Delete image
await ImageStorage.DeleteImageAsync(imageId);

// Toggle favorite
await ImageStorage.ToggleFavoriteAsync(imageId);

// Check storage usage
var info = await ImageStorage.GetStorageInfoAsync();
// Returns: UsagePercentage, EstimatedRemainingImages, CurrentUsageBytes, EstimatedQuotaBytes
```

### E2E Test Pattern
```csharp
[Collection("BlazorApp")]
public class MyTests(BlazorAppFixture fixture)
{
    [Fact]
    public async Task MyTest()
    {
        var page = fixture.Page;
        await page.GotoAsync(fixture.BaseUrl);
        await page.ClearLocalStorageAsync(); // Clear AFTER navigating
        // ... test logic
    }
}
```

## Important Implementation Details

### Escape Characters in Razor
- Use `@@keyframes` (double @) for CSS animations in `<style>` blocks
- Arrow in title uses Unicode: `→` (displayed as "Now → Next → Then")

### Grid Layouts
- Use `min-width: 0` on grid items to allow proper shrinking in CSS Grid
- Fixed columns (`repeat(3, 1fr)`) often better than `auto-fill` for consistency
- Tablet breakpoints: 768px (md:) and 1024px (lg:)

### Modal Pattern
- Boolean flag for visibility (e.g., `ShowDeleteConfirmation`)
- `@onclick:stopPropagation="true"` on modal content to prevent backdrop clicks

### File Uploads
- Use `InputFile` component with `accept` attribute
- `OpenReadStream(maxSize)` to read files (50MB max for restore)
- Convert to base64 via `MemoryStream` and `Convert.ToBase64String`

### Selection Patterns
- **Schedule (PlanDay)**: Max 3 items, numbered badges (1=Now, 2=Next, 3=Then)
- **Food Choices**: Min 2 items, no max limit, checkmark badges
- **Activity Choices**: Min 2 items, no max limit, checkmark badges

### Phonics Patterns
- **GraphemeCard ID format**: `p{phase}-w{week}-{grapheme}` (e.g., `p2-w1-s`, `p3-w2-oo_long`, `p5-w3-ch_chef`)
- **Duplicate grapheme IDs**: When multiple cards share a grapheme (e.g., oo long/short, alternative pronunciations), use suffixed IDs and manual card construction instead of the `BuildWeek` helper
- **Sequential unlocking**: First grapheme in each phase always unlocked; completing one unlocks the next by `OrderIndex`
- **Progress storage**: `HashSet<string>` of completed card IDs serialized as JSON in IndexedDB store `phonics-progress`
- **Three sound tile states**: Completed (muted bg + checkmark), Current (phonics-blue border), Locked (grey + lock icon, `<div>` not `<a>`)
- **Phonics color**: `calm-phonics: #7BA3C4`, `calm-phonics-dark: #6890B0`, `calm-phonics-light: #9BBDD6`

### Blazor Same-Route Navigation
- When navigating between pages with the same route template (e.g., `/phonics/2/card/A` to `/phonics/2/card/B`), Blazor reuses the component instance
- `OnInitializedAsync` does NOT re-run - use `OnParametersSetAsync` instead for pages with changing route parameters
- Reset any local state flags (like `ShowPhaseComplete`) at the start of `OnParametersSetAsync`

### E2E Test Timing
- Blazor WASM needs extra init time - use 60s timeout on `WaitForSelectorAsync`
- Use `WaitForURLAsync()` for navigation assertions
- Clear storage (IndexedDB + localStorage) AFTER navigating to page (requires page context)
- Note: FileChooser tests can be flaky due to Playwright/Blazor InputFile interaction
- **Use RELATIVE paths in href selectors** (e.g., `a[href='phonics']` NOT `a[href='/phonics']`) - Blazor renders relative hrefs in the DOM

## Test Status
- **E2E (`tests/NowThenNext.Tests.E2E`, Playwright)**: HomePageTests, UploadTests, LibraryTests, ScheduleTests, FoodChoicesTests, ActivityChoicesTests, BackupRestoreTests, PhonicsTests, LearningCardsTests, CloudSyncTests (signed-out sync UI)
- **Unit (`tests/NowThenNext.Tests.Unit`, xUnit v3)**: `SyncMergeEngineTests` covers the merge engine (LWW, tombstone propagation, 90-day GC, phonics map merge). Run with `dotnet test tests/NowThenNext.Tests.Unit`.
- **Coverage**: All user stories (US-001 to US-037, US-046 to US-048)
- Note: Google Drive sign-in uses a Google popup that can't be automated in Playwright, so sync E2E is limited to the signed-out UI.

## Debugging Priorities
When debugging 404s, routing errors, or broken assets in this app, check causes in this order:
1. **Base path / relative URLs** - Are all URLs relative or respecting `<base href>`? This is the #1 cause of issues on GitHub Pages subdirectory deployments.
2. **Case sensitivity** - File/path case mismatches between code and filesystem.
3. **Build output** - Is the expected file actually in the publish output?
4. Only after ruling out the above, investigate deeper causes (service workers, framework internals, server config).

## GitHub Pages Deployment
This app deploys to GitHub Pages at a subdirectory path (`/NowNextThen/`).
- All asset references, navigation links, and API calls must use relative paths or respect `<base href>`
- Never use absolute paths starting with `/` in HTML, Razor, or JS - they will 404 on GitHub Pages
- The `<base href>` is set to `/` for dev and rewritten to `/NowNextThen/` during CI publish
- After any bulk path changes, grep the entire project for remaining absolute paths: `href="/` and `src="/`

## Notes for Development
- Always maintain calm visual design - avoid bright/harsh colors
- Keep touch targets ≥48px for accessibility
- Test in browser for visual verification
- E2E tests required for all user-facing features
- Storage quota warnings at 80% capacity
- Dev server runs on **localhost:5161**
- Use `pkill -f "dotnet"` to kill stray processes before E2E tests
- Responsive padding pattern: `px-6 py-8` mobile, `sm:px-8 sm:py-10` larger screens
- After bulk edits across multiple files, always verify with grep that no stale instances of the old pattern remain
