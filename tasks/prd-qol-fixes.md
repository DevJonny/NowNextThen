# PRD: Quality-of-Life Fixes

## Introduction

A batch of small-to-medium fixes identified during a codebase review. The changes address broken tests, CI configuration errors, navigation dead-ends, visual inconsistencies, minor bugs, and dead code. None of these are new features — they restore correctness and consistency across the app.

## Goals

- Fix the home page test that broke after the Phonics/Learning Cards menu merge
- Correct invalid GitHub Actions versions so CI doesn't fail
- Fix a real bug where ScheduleDisplay silently shows nothing for non-Places images
- Remove or update stale navigation links
- Bring copy, colours, icons, and spacing into line with established design patterns
- Remove dead/orphaned code that creates confusion

## User Stories

### US-054: Fix HomePageTests for merged Learning menu group
**Description:** As a developer, I want the home page E2E tests to reflect the current 4-group menu structure so that CI passes.

**Acceptance Criteria:**
- [ ] `HomePageTests` asserts exactly 4 `.menu-group` elements (was 5)
- [ ] The 4th group (Learning) is asserted to contain exactly 2 `.menu-button` elements (Phonics + Learning Cards)
- [ ] The separate assertions for a 5th group are removed
- [ ] All existing home page tests still pass
- [ ] `dotnet test tests/NowThenNext.Tests.E2E` passes

---

### US-055: Fix invalid GitHub Actions versions
**Description:** As a developer, I want the CI workflow to use valid action versions so that deployments don't fail at the checkout or setup step.

**Acceptance Criteria:**
- [ ] `actions/checkout@v6` changed to `actions/checkout@v4` in `.github/workflows/deploy-to-github-pages.yml`
- [ ] `actions/setup-dotnet@v5` changed to `actions/setup-dotnet@v4` in the same file
- [ ] Workflow file is valid YAML (no syntax errors)

---

### US-056: Fix ScheduleDisplay to look up images across all categories
**Description:** As a developer, I want the schedule display to find images from any category so that if the app ever allows non-Places items in schedules, they won't silently disappear.

**Acceptance Criteria:**
- [ ] `ScheduleDisplay.razor.cs` calls `GetAllImagesAsync()` (or equivalent that returns all categories) instead of `GetImagesByCategoryAsync(ImageCategory.Places)`
- [ ] Images still display correctly when navigating from PlanDay with valid Places IDs
- [ ] No other behaviour changes on the schedule display page
- [ ] `dotnet build` passes

---

### US-057: Fix redundant ternary in selection count labels
**Description:** As a developer, I want the selection count label logic to be readable and correct so that the code is easy to understand.

**Acceptance Criteria:**
- [ ] `FoodChoices.razor` selection label ternary simplified: `SelectedImages.Count == 1 ? "item selected (need 2+)" : "items selected"`
- [ ] `ActivityChoices.razor` selection label ternary simplified the same way
- [ ] Displayed text is unchanged for all counts (0 → "items selected", 1 → "item selected (need 2+)", 2+ → "items selected")
- [ ] `dotnet build` passes

---

### US-058: Fix stale "Browse My Pictures" link in Favorites empty state
**Description:** As a user, I want the empty-state call-to-action in Favorites to take me somewhere useful so that I'm not sent to an orphaned legacy page.

**Acceptance Criteria:**
- [ ] The `<a href="my-pictures">` link in `Favorites.razor` empty state is replaced with a link to `"/"` (home) labelled "Go to Home" (or similar useful destination)
- [ ] The link renders correctly in the empty-state section
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-059: Style the NotFound page to match the app design
**Description:** As a user who lands on a 404, I want to see a styled, calm page with a way to get home so that I'm not confused by an unstyled HTML fallback.

**Acceptance Criteria:**
- [ ] `NotFound.razor` has a page title, calm background, and styled text consistent with other pages
- [ ] Page includes a clearly visible "Go Home" link/button that navigates to `"/"`
- [ ] Page uses `calm-*` Tailwind classes, not plain HTML
- [ ] Minimum 48px touch target on the home link
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-060: Fix ScheduleDisplay auto-advance toggle label
**Description:** As a user, I want the auto-advance toggle button on the schedule to clearly indicate what will happen when I press it so that I'm not confused about the current state.

**Acceptance Criteria:**
- [ ] When auto-advance is **on**: button label is "Auto" (unchanged — currently correct)
- [ ] When auto-advance is **off**: button label is "Auto" with a play icon (clicking will enable auto-advance), not "Static"
- [ ] Tooltip or aria-label accurately reflects the action ("Enable auto-advance" / "Disable auto-advance")
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-061: Fix food empty-state icon (dollar coin → food icon)
**Description:** As a user, I want the food library and food choices empty states to show a relevant icon so that the page looks intentional.

**Acceptance Criteria:**
- [ ] `FoodLibrary.razor` empty-state SVG replaced with a food-appropriate icon (e.g. the apple/food icon already used on the home menu button for Food)
- [ ] `FoodChoices.razor` empty-state SVG replaced with the same food icon
- [ ] Dollar-coin path (`M12 8c-1.657 0...M21 12a9 9 0 11-18...`) no longer appears in either file
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-062: Fix Upload page success and error message colours
**Description:** As a user, I want the upload success and error messages to use the app's calm colour palette so that they don't look visually jarring.

**Acceptance Criteria:**
- [ ] `.success-message` in `Upload.razor` uses `#6B9A7D` (calm-success) for border and text, and a soft tinted background consistent with `Settings.razor`'s success message
- [ ] `.error-message` in `Upload.razor` uses `#D4A06A` (calm-warning/amber) for border and text instead of `#E65100` (bright orange-red)
- [ ] Neither `#2E7D32` nor `#E65100` remain in `Upload.razor`'s style block
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-063: Fix Places library empty-state copy
**Description:** As a user viewing the empty Places library, I want the hint text to accurately describe the Places category so that I'm not confused by a reference to Activities.

**Acceptance Criteria:**
- [ ] `PlacesLibrary.razor` empty-state paragraph changed from "Add pictures of places and activities..." to "Add pictures of places to use in your daily schedule."
- [ ] `MyPictures.razor` Places tab empty-state paragraph updated with the same correction (line ~97)
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-064: Fix Learning pages padding inconsistency
**Description:** As a developer, I want Learning pages to use the same horizontal padding as all other pages so that the layout is consistent.

**Acceptance Criteria:**
- [ ] `LearningCategories.razor` header/content div changed from `px-6 sm:px-8` to `px-4 sm:px-6`
- [ ] `LearningCardGrid.razor` header/content div changed from `px-6 sm:px-8` to `px-4 sm:px-6`
- [ ] No other layout changes made
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-065: Remove unused Bootstrap CSS
**Description:** As a developer, I want the Bootstrap CSS bundle removed from the app so that ~200KB of unused CSS isn't downloaded on every page load.

**Acceptance Criteria:**
- [ ] `<link rel="stylesheet" href="lib/bootstrap/dist/css/bootstrap.min.css" />` removed from `wwwroot/index.html`
- [ ] Any Bootstrap-specific CSS variables (`--bs-*`) in `app.css` replaced with hard-coded values or calm-palette equivalents (specifically `--bs-secondary-color` on line ~182)
- [ ] App visually unchanged after removal — no unstyled elements
- [ ] `dotnet build` passes
- [ ] Verify in browser using dev-browser skill

---

### US-066: Remove dead MyPictures page
**Description:** As a developer, I want the orphaned `MyPictures.razor` page removed so that the codebase doesn't contain unreachable routes and legacy UI that is missing the Activities category.

**Acceptance Criteria:**
- [ ] `MyPictures.razor` deleted
- [ ] `@page "/my-pictures"` and `@page "/my-pictures/{Tab}"` route aliases removed from `PlacesLibrary.razor` and `FoodLibrary.razor`
- [ ] No remaining references to `/my-pictures` anywhere in the project (grep confirms)
- [ ] `Favorites.razor` empty-state link (updated in US-058) no longer points to `/my-pictures`
- [ ] `dotnet build` passes

---

## Functional Requirements

- FR-1: `HomePageTests` must assert 4 menu groups, with the 4th containing 2 buttons
- FR-2: CI workflow must use `actions/checkout@v4` and `actions/setup-dotnet@v4`
- FR-3: `ScheduleDisplay` must look up images by ID across all categories, not just Places
- FR-4: Selection count ternary in `FoodChoices` and `ActivityChoices` must be simplified to a single condition
- FR-5: Favorites empty-state link must point to a reachable, useful destination
- FR-6: `NotFound.razor` must be styled consistently with the rest of the app and include a home link
- FR-7: ScheduleDisplay auto-advance toggle must use "Auto" label in both states
- FR-8: Food empty-state icon must be a food-relevant SVG in both `FoodLibrary` and `FoodChoices`
- FR-9: Upload success messages must use `#6B9A7D`; error messages must use `#D4A06A`
- FR-10: Places empty-state text must not mention Activities
- FR-11: Learning pages must use `px-4 sm:px-6` horizontal padding
- FR-12: Bootstrap CSS must be removed and any `--bs-*` variable references resolved
- FR-13: `MyPictures.razor` and all its route aliases must be deleted

## Non-Goals

- No refactoring of the duplicated CSS/code-behind across library pages (M1) — too large, risk of regression
- No re-enabling of E2E tests in CI — requires separate infrastructure work
- No new features or behaviour changes beyond the fixes described
- No changes to the Phonics or Learning Cards pages beyond padding (US-064)

## Technical Considerations

- `GetAllImagesAsync()` should already exist on `IImageStorageService` — confirm before implementing US-056; if not, add it
- Bootstrap's `lib/` folder in `wwwroot` can remain on disk after removing the `<link>` tag; it just won't be loaded
- Deleting `MyPictures.razor` (US-066) should be done after US-058 is complete to avoid a dangling link

## Success Metrics

- `dotnet test tests/NowThenNext.Tests.E2E` passes with no failures
- No `--bs-*` CSS variable references remain in the project
- No `/my-pictures` references remain in the project
- All UI pages use `px-4 sm:px-6` horizontal padding on headers/content
- Upload messages use only calm-palette colours

## Open Questions

- Should E2E tests be re-enabled in CI as a follow-up? The test suite currently runs locally but is commented out in the workflow.
- Should `StorageWarningBanner` be added to the individual library pages (`PlacesLibrary`, `FoodLibrary`, `ActivitiesLibrary`)? Left out of scope here but worth considering.
