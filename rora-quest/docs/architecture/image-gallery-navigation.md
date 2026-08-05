# Architecture: Previous/Next Image Navigation

**Status:** Design Document  
**Feature:** Add Previous/Next navigation to maximized image view  
**Reference PRD:** `rora-quest/docs/prd/image-gallery-navigation.md`  
**Target Component:** `rora-quest/source/apps/web/src/app/tasks/[id]/page.tsx`  
**Date:** 2025

---

## 1. Context & Goals

### Problem Statement
The current maximized image view in the task detail page shows one image at a time, but users must minimize and reselect a different image to view other images in the gallery. This creates friction when reviewing multiple related images sequentially.

### Goals
1. Enable seamless navigation between images without minimizing the expanded view
2. Support both mouse (Previous/Next buttons) and keyboard (arrow keys) navigation
3. Provide visual feedback on position in the sequence (counter display)
4. Maintain accessibility and mobile responsiveness
5. Keep implementation simple and reuse existing patterns from the codebase

### Success Criteria (from PRD)
- Previous/Next buttons appear in maximized image view
- Arrow keys (left/right) navigate between images
- Buttons disabled at boundaries (no navigation beyond first/last image)
- Image counter displays current position ("Image 2 of 5")
- Escape key closes maximized view (existing behavior unchanged)
- No persistence of navigation index across sessions or minimize/maximize cycles
- Mobile responsiveness (appropriate button size and spacing)

---

## 2. Current State Analysis

### Component Structure
The image viewer is implemented in a single component at:
- **File:** `rora-quest/source/apps/web/src/app/tasks/[id]/page.tsx` (1027 lines)
- **Key parts:**
  - Line 97-99: `DetailTakeover` discriminated union type for text OR image takeover
  - Line 171: `takeover` state to track which item is expanded (null = minimized)
  - Line 585-587: `expandedImage` derived state to find the actual asset object
  - Line 570-574: `diagramImages` computed array filtering displayable images
  - Line 268-281: Existing keyboard handler (Escape key) pattern
  - Line 711-725: Detail-takeover header with title and Minimize button
  - Line 736-744: Image preview JSX (conditional rendering)

### Current Image Filtering Logic
```typescript
// Lines 570-574 in page.tsx
const diagramImages = task.assets
  ?.filter(
    (asset) =>
      asset.contentType?.startsWith("image/") || asset.assetType?.toLowerCase().includes("diagram")
  )
  .sort((a, b) => (a.createdAt || 0) - (b.createdAt || 0))
  ?? [];
```

**Key behavior:** Only assets with image content type or "diagram" asset type are included. Sorted by createdAt in ascending order (oldest first).

### State Management Pattern
The component uses React `useState` with a discriminated union:
```typescript
type DetailTakeover = 
  | { type: "field"; field: ExpandableDetailField } 
  | { type: "image"; assetId: string };

const [takeover, setTakeover] = useState<DetailTakeover | null>(null);
```

When any image is clicked, `takeover.assetId` is set. The `expandedImage` computed value finds the asset:
```typescript
const expandedImage = useMemo(
  () => task.assets?.find((a) => a.id === takeover?.type === "image" ? takeover.assetId : undefined),
  [takeover, task.assets]
);
```

### Keyboard Event Handling Pattern
Existing Escape key handler (lines 268-281) uses `useEffect` with proper cleanup:
```typescript
useEffect(() => {
  const handleKeydown = (event: KeyboardEvent) => {
    if (event.key === "Escape" && takeover) {
      setTakeover(null);
    }
  };
  window.addEventListener("keydown", handleKeydown);
  return () => window.removeEventListener("keydown", handleKeydown);
}, [takeover]);
```

This pattern can be extended to handle arrow keys.

### CSS Styling
**File:** `rora-quest/source/apps/web/src/app/globals.css`

- `.detail-takeover-card` (line 606-611): Grid layout, min-height calc(100vh - 18rem)
- `.detail-takeover-header` (line 612-614): Flexbox, space-between justification
- `.detail-image-preview-img` (line 630-637): object-fit contain, max-height calc(100vh - 24rem)

The header currently contains: title (filename) + modal note + Minimize button

---

## 3. Proposed Design

### 3.1 UI/UX: Button Placement & Layout

#### Button Placement Strategy
**Approach:** Floating buttons on left/right sides of image (positioned absolutely within the takeover card)

**Rationale:**
- Does not disrupt existing header layout (which may contain filename and metadata)
- Provides intuitive left-arrow-for-previous, right-arrow-for-next cognitive mapping
- Works well on both desktop and mobile (tappable target at edges of screen)
- Mimics common gallery/carousel UX patterns (Google Photos, Instagram, etc.)

#### Button Specification
- **Position:** `position: absolute` within `.detail-takeover-card`
  - Left button: `left: 1rem` (16px from left edge)
  - Right button: `right: 1rem` (16px from right edge)
  - `top: 50%`, `transform: translateY(-50%)` (vertically centered on image)
  
- **Size:** 44px × 44px (meets WCAG touch target minimum of 44×44)

- **Visual style:**
  - Background: `rgba(0, 0, 0, 0.6)` (semi-transparent dark overlay)
  - Icon: chevron-left / chevron-right in white (16×16 inside button)
  - Border radius: 4px
  - Hover: `rgba(0, 0, 0, 0.8)` (darker on hover)
  - Focus: Blue outline (2px solid #0066cc) for keyboard navigation
  - Disabled: `opacity: 0.5`, `cursor: not-allowed`, no hover effect

#### Image Counter Display
- **Location:** Bottom-right corner of image container
- **Format:** "2 of 5" text in small label
- **Styling:**
  - Background: `rgba(0, 0, 0, 0.6)`
  - Color: white
  - Font: 12px, sans-serif
  - Padding: 8px 12px
  - Border radius: 4px
  - Positioned absolutely within image container

### 3.2 Component Architecture

#### Extended DetailTakeover Type
The discriminated union will be extended to include index tracking for images:

**Current:**
```typescript
type DetailTakeover = 
  | { type: "field"; field: ExpandableDetailField } 
  | { type: "image"; assetId: string };
```

**Proposed:**
```typescript
type DetailTakeover = 
  | { type: "field"; field: ExpandableDetailField } 
  | { type: "image"; assetId: string; currentImageIndex?: number };
```

The `currentImageIndex` will store the index in the filtered `diagramImages` array when an image is maximized. This allows the navigation handlers to increment/decrement the index and fetch the corresponding asset.

#### Navigation Sub-Component (Optional)
Two approaches:

**Approach A (Simpler - Recommended):** Keep navigation logic inline in the main component
- Add navigation handlers directly in page.tsx
- Add JSX for buttons and counter in the existing image preview section
- Add CSS classes to globals.css

**Approach B (Cleaner - Requires Refactor):** Extract to `ImageNavigation.tsx`
- Create new component: `rora-quest/source/apps/web/src/components/ImageNavigation.tsx`
- Accepts props: `currentIndex`, `totalImages`, `onPrevious`, `onNext`, `disabled`
- Handles button rendering and ARIA labels
- Pros: Reusable, cleaner main component
- Cons: Adds file, requires prop drilling

**Recommendation:** Start with Approach A (inline). Extract to component if reused elsewhere.

---

## 4. State Management Strategy

### Filtered Image Array
The `diagramImages` array (already computed at lines 570-574) serves as the source of truth:
```typescript
const diagramImages = task.assets
  ?.filter(...)
  .sort((a, b) => (a.createdAt || 0) - (b.createdAt || 0))
  ?? [];
```

### Current Image Index Tracking
When an image is clicked to expand, store its index in the `diagramImages` array:

**Updated click handler (when user clicks an image to maximize):**
```typescript
const handleImageClick = (imageAsset: Asset) => {
  const index = diagramImages.findIndex((img) => img.id === imageAsset.id);
  setTakeover({
    type: "image",
    assetId: imageAsset.id,
    currentImageIndex: index >= 0 ? index : 0,
  });
};
```

### Navigation Handlers

**handleNextImage:**
```typescript
const handleNextImage = () => {
  if (takeover?.type !== "image") return;
  
  const currentIndex = takeover.currentImageIndex ?? 0;
  if (currentIndex < diagramImages.length - 1) {
    const nextAsset = diagramImages[currentIndex + 1];
    setTakeover({
      type: "image",
      assetId: nextAsset.id,
      currentImageIndex: currentIndex + 1,
    });
  }
};
```

**handlePreviousImage:**
```typescript
const handlePreviousImage = () => {
  if (takeover?.type !== "image") return;
  
  const currentIndex = takeover.currentImageIndex ?? 0;
  if (currentIndex > 0) {
    const prevAsset = diagramImages[currentIndex - 1];
    setTakeover({
      type: "image",
      assetId: prevAsset.id,
      currentImageIndex: currentIndex - 1,
    });
  }
};
```

### Button Disabled State
Buttons should be disabled (and visually indicate as such) at boundaries:
```typescript
const canNavigatePrevious = takeover?.type === "image" && (takeover.currentImageIndex ?? 0) > 0;
const canNavigateNext = takeover?.type === "image" && (takeover.currentImageIndex ?? 0) < diagramImages.length - 1;
```

### Index Persistence Notes
- **No persistence across minimize/maximize:** When user minimizes and reopens the same image, index is not remembered. The index is only used internally during navigation.
- **Robustness:** If `currentImageIndex` is missing or invalid when rendering, default to 0.
- **Edge case - deleted images:** If user navigates to a different page and returns, `diagramImages` may have changed. Use `assetId` as the source of truth; if it's not in `diagramImages`, close the takeover.

---

## 5. Keyboard Navigation

### Keyboard Events
- **Arrow Right (→):** Navigate to next image (disabled if at end)
- **Arrow Left (←):** Navigate to previous image (disabled if at start)
- **Escape:** Close maximized view (existing behavior, unchanged)

### Implementation Strategy
Extend the existing keyboard handler (lines 268-281) to also handle arrow keys:

```typescript
useEffect(() => {
  const handleKeydown = (event: KeyboardEvent) => {
    if (!takeover) return;

    if (event.key === "Escape") {
      setTakeover(null);
    } else if (takeover.type === "image") {
      if (event.key === "ArrowRight") {
        event.preventDefault();
        handleNextImage();
      } else if (event.key === "ArrowLeft") {
        event.preventDefault();
        handlePreviousImage();
      }
    }
  };
  
  window.addEventListener("keydown", handleKeydown);
  return () => window.removeEventListener("keydown", handleKeydown);
}, [takeover, diagramImages]);
```

**Key decisions:**
- Call `event.preventDefault()` on arrow keys to prevent scrolling or other default browser behavior
- Only listen for arrow keys when `takeover?.type === "image"` (ignore when viewing expanded text fields)
- No visual focus indication needed for arrow key navigation (image updates silently)

---

## 6. Accessibility

### ARIA Labels
Buttons must have descriptive labels for screen reader users:

**Previous Button:**
```jsx
<button
  aria-label={`Previous image (${currentIndex} of ${diagramImages.length})`}
  disabled={!canNavigatePrevious}
  onClick={handlePreviousImage}
>
  <ChevronLeftIcon />
</button>
```

**Next Button:**
```jsx
<button
  aria-label={`Next image (${currentIndex} of ${diagramImages.length})`}
  disabled={!canNavigateNext}
  onClick={handleNextImage}
>
  <ChevronRightIcon />
</button>
```

### Image Counter
Announce the counter text as live region (or as part of ARIA label above):
```jsx
<div aria-live="polite" aria-atomic="true" className="image-counter">
  {currentIndex + 1} of {diagramImages.length}
</div>
```

Using `aria-live="polite"` will announce the counter to screen readers when it changes during navigation.

### Focus Management
- Buttons are naturally focusable (native `<button>` elements)
- Focus outline provided by CSS (2px solid blue)
- No need to programmatically manage focus (arrow key navigation doesn't change focus)
- Escape key closes modal (user returns focus to original image thumbnail if they navigate back)

### Keyboard Indicators
- Don't add visual "press arrow keys" hint on desktop (common pattern, users expect it)
- On mobile, buttons are prominent enough that users will understand the UI
- ARIA labels adequately communicate functionality to assistive technologies

---

## 7. Implementation Approach

### Phase 1: Core Navigation (Minimal viable feature)

#### Step 1: Update DetailTakeover type
**File:** `rora-quest/source/apps/web/src/app/tasks/[id]/page.tsx`  
**Lines:** ~97-99

Change:
```typescript
type DetailTakeover = 
  | { type: "field"; field: ExpandableDetailField } 
  | { type: "image"; assetId: string };
```

To:
```typescript
type DetailTakeover = 
  | { type: "field"; field: ExpandableDetailField } 
  | { type: "image"; assetId: string; currentImageIndex?: number };
```

#### Step 2: Add navigation handlers
**File:** Same, add near other handlers (around line 200-300)

```typescript
const currentImageIndex = takeover?.type === "image" ? (takeover.currentImageIndex ?? 0) : 0;
const canNavigatePrevious = takeover?.type === "image" && currentImageIndex > 0;
const canNavigateNext = takeover?.type === "image" && currentImageIndex < diagramImages.length - 1;

const handlePreviousImage = () => {
  if (takeover?.type !== "image" || !canNavigatePrevious) return;
  
  const prevAsset = diagramImages[currentImageIndex - 1];
  setTakeover({
    type: "image",
    assetId: prevAsset.id,
    currentImageIndex: currentImageIndex - 1,
  });
};

const handleNextImage = () => {
  if (takeover?.type !== "image" || !canNavigateNext) return;
  
  const nextAsset = diagramImages[currentImageIndex + 1];
  setTakeover({
    type: "image",
    assetId: nextAsset.id,
    currentImageIndex: currentImageIndex + 1,
  });
};
```

#### Step 3: Update keyboard handler
**File:** Same, lines 268-281

Add arrow key handling to existing Escape handler:
```typescript
useEffect(() => {
  const handleKeydown = (event: KeyboardEvent) => {
    if (!takeover) return;

    if (event.key === "Escape") {
      setTakeover(null);
    } else if (takeover.type === "image") {
      if (event.key === "ArrowRight" && canNavigateNext) {
        event.preventDefault();
        handleNextImage();
      } else if (event.key === "ArrowLeft" && canNavigatePrevious) {
        event.preventDefault();
        handlePreviousImage();
      }
    }
  };
  
  window.addEventListener("keydown", handleKeydown);
  return () => window.removeEventListener("keydown", handleKeydown);
}, [takeover, diagramImages, canNavigatePrevious, canNavigateNext]);
```

#### Step 4: Update image click handlers
**File:** Same, find where images are clicked

When an image is clicked to expand, ensure the index is stored:
```typescript
const handleImageClick = (asset: Asset) => {
  const index = diagramImages.findIndex((img) => img.id === asset.id);
  setTakeover({
    type: "image",
    assetId: asset.id,
    currentImageIndex: index >= 0 ? index : 0,
  });
};
```

#### Step 5: Add navigation buttons and counter to JSX
**File:** Same, around line 736-744 (image preview section)

Insert navigation buttons after the image element:
```jsx
{takeover?.type === "image" && (
  <>
    {/* Previous Button */}
    <button
      className="image-nav-button image-nav-previous"
      onClick={handlePreviousImage}
      disabled={!canNavigatePrevious}
      aria-label={`Previous image (${currentImageIndex + 1} of ${diagramImages.length})`}
    >
      &lsaquo; {/* Left chevron unicode or icon component */}
    </button>

    {/* Next Button */}
    <button
      className="image-nav-button image-nav-next"
      onClick={handleNextImage}
      disabled={!canNavigateNext}
      aria-label={`Next image (${currentImageIndex + 1} of ${diagramImages.length})`}
    >
      &rsaquo; {/* Right chevron unicode or icon component */}
    </button>

    {/* Image Counter */}
    <div
      className="image-counter"
      aria-live="polite"
      aria-atomic="true"
    >
      {currentImageIndex + 1} of {diagramImages.length}
    </div>
  </>
)}
```

#### Step 6: Add CSS for navigation UI
**File:** `rora-quest/source/apps/web/src/app/globals.css`

Add to the detail-takeover or detail-image-preview section:
```css
/* Image Navigation Buttons */
.image-nav-button {
  position: absolute;
  width: 44px;
  height: 44px;
  border-radius: 4px;
  background-color: rgba(0, 0, 0, 0.6);
  color: white;
  border: none;
  cursor: pointer;
  font-size: 24px;
  line-height: 1;
  display: flex;
  align-items: center;
  justify-content: center;
  top: 50%;
  transform: translateY(-50%);
  transition: background-color 0.2s ease;
  z-index: 10;
}

.image-nav-button:hover:not(:disabled) {
  background-color: rgba(0, 0, 0, 0.8);
}

.image-nav-button:focus {
  outline: 2px solid #0066cc;
  outline-offset: 2px;
}

.image-nav-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.image-nav-previous {
  left: 1rem;
}

.image-nav-next {
  right: 1rem;
}

/* Image Counter */
.image-counter {
  position: absolute;
  bottom: 1rem;
  right: 1rem;
  background-color: rgba(0, 0, 0, 0.6);
  color: white;
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 500;
  z-index: 10;
}

/* Mobile responsiveness */
@media (max-width: 640px) {
  .image-nav-button {
    width: 40px;
    height: 40px;
    font-size: 20px;
  }

  .image-nav-previous {
    left: 0.5rem;
  }

  .image-nav-next {
    right: 0.5rem;
  }

  .image-counter {
    bottom: 0.5rem;
    right: 0.5rem;
    font-size: 11px;
  }
}
```

### Phase 2: Optional Enhancements

#### Add Icon Component
If the app uses an icon library (e.g., lucide-react), replace unicode chevrons with proper SVG icons:
```jsx
import { ChevronLeft, ChevronRight } from "lucide-react";

<button className="image-nav-button image-nav-previous">
  <ChevronLeft size={16} />
</button>
```

#### Add Transition Animation
Optional: Add fade-in/fade-out animation when switching images:
```css
.detail-image-preview-img {
  animation: fadeIn 0.2s ease-in-out;
}

@keyframes fadeIn {
  from { opacity: 0.8; }
  to { opacity: 1; }
}
```

#### Add Touch Swipe Support (Mobile)
Use a library like `react-use-gesture` or implement simple swipe detection:
- Swipe left → next image
- Swipe right → previous image

---

## 8. Risks & Mitigations

### Risk 1: State Inconsistency - Deleted Images
**Risk:** If images are deleted while user is viewing an older version, `currentImageIndex` could point to a different image or be out of bounds.

**Likelihood:** Low (images are typically immutable in tasks)  
**Impact:** High (user might see wrong image or UI breaks)

**Mitigation:**
- Always validate `currentImageIndex` is within bounds before using it
- Use `assetId` as source of truth; if asset not found in `diagramImages`, close the takeover
- When opening the takeover, recalculate index from asset ID

**Code:**
```typescript
// Before navigation, always validate
const currentIndex = diagramImages.findIndex((img) => img.id === takeover?.assetId);
if (currentIndex < 0) {
  // Asset no longer in list, close takeover
  setTakeover(null);
  return;
}
```

### Risk 2: Keyboard Event Conflicts
**Risk:** Arrow key listeners might interfere with other components (e.g., text input, form fields).

**Likelihood:** Medium (depends on page layout)  
**Impact:** Medium (confusing behavior when typing)

**Mitigation:**
- Only listen for arrow keys when `takeover?.type === "image"` (already done)
- Add check: don't navigate if any input/textarea is focused:
```typescript
const handleKeydown = (event: KeyboardEvent) => {
  // Don't navigate if user is typing in an input
  if (event.target instanceof HTMLInputElement || event.target instanceof HTMLTextAreaElement) {
    return;
  }
  // ... rest of logic
};
```

### Risk 3: Mobile Touch Target Size
**Risk:** 44×44px buttons might be too small on some mobile devices or hard to distinguish from image.

**Likelihood:** Low (44×44 is standard)  
**Impact:** Medium (poor UX for mobile users)

**Mitigation:**
- Use 44×44px minimum (already in design)
- Test on mobile devices (iPhone SE, older Android)
- Consider increasing to 48×48px if feedback indicates issues
- Ensure sufficient spacing from image edges

### Risk 4: Performance - Large Image Lists
**Risk:** If task has 1000+ images, filtering and sorting might be slow.

**Likelihood:** Very low (most tasks have <50 images)  
**Impact:** Low (single sort operation is negligible)

**Mitigation:**
- `diagramImages` is already computed and memoized via `useMemo` (assumed)
- If needed, add performance monitoring to measure sort time
- For extreme cases, could add pagination or lazy-load additional images

### Risk 5: Browser Compatibility
**Risk:** CSS `object-fit: contain`, Flexbox, arrow key events might not work on older browsers.

**Likelihood:** Very low (Next.js 14 targets modern browsers)  
**Impact:** Low (graceful degradation, feature doesn't break core UI)

**Mitigation:**
- All used CSS features (flexbox, object-fit, position absolute) supported in modern browsers
- Arrow key events are standard JavaScript, no compatibility issues
- Test in Chrome, Firefox, Safari, Edge (all supported by Next.js 14)

### Risk 6: Accessibility - Screen Reader Announcement Timing
**Risk:** Screen reader might announce counter before image loads, or announcement interrupted by other events.

**Likelihood:** Low (aria-live="polite" should handle it)  
**Impact:** Low (non-critical UX issue)

**Mitigation:**
- Use `aria-live="polite"` (waits for pause before announcing)
- Include counter in button ARIA labels as fallback
- Test with screen readers (NVDA, JAWS, VoiceOver)

### Risk 7: Index Persistence Confusion
**Risk:** User expects navigation index to persist after minimize/maximize, but it doesn't (by design).

**Likelihood:** Medium (common UX expectation)  
**Impact:** Low (feature still works, just minor UX inconsistency)

**Mitigation:**
- Document this behavior in comments in code
- This matches PRD requirement: "no persistence across maximize/minimize"
- If feedback indicates need, add state persistence to localStorage (Phase 2)

---

## 9. Summary: File Changes

### Primary File
**`rora-quest/source/apps/web/src/app/tasks/[id]/page.tsx`** (~1027 lines)

Changes:
1. Line 97-99: Extend `DetailTakeover` type to include `currentImageIndex?: number`
2. Line ~200-300: Add 3 navigation handlers (`currentImageIndex`, `canNavigate*`, `handlePrevious/NextImage`)
3. Line 268-281: Extend keyboard handler to support arrow keys
4. Line ~700-750: Update image click handlers to capture index
5. Line 736-744: Add navigation buttons, counter JSX in image preview section

**Estimated changes:** ~50-80 lines added

### Secondary File
**`rora-quest/source/apps/web/src/app/globals.css`** (~640 lines)

Changes:
1. Add `.image-nav-button` base styles (10 lines)
2. Add `.image-nav-button:hover`, `:focus`, `:disabled` states (8 lines)
3. Add `.image-nav-previous`, `.image-nav-next` positioning (6 lines)
4. Add `.image-counter` styles (8 lines)
5. Add `@media (max-width: 640px)` responsive adjustments (12 lines)

**Estimated changes:** ~50 lines added

### No New Files Required
- No new components (implementation inline)
- No new dependencies (use native HTML/CSS)
- No new utility functions (reuse existing patterns)

---

## 10. Implementation Readiness Checklist

- [x] Current component structure analyzed
- [x] State management strategy defined
- [x] Keyboard event handling pattern identified
- [x] UI/UX design specified (button size, position, styling)
- [x] Accessibility requirements documented
- [x] Mobile responsiveness considered
- [x] Risks and mitigations identified
- [x] Step-by-step implementation plan provided
- [x] File paths and line numbers specified
- [x] Code snippets ready for copy/paste
- [ ] Engineer review (next step)
- [ ] Testing plan (Phase 2, to be defined)
- [ ] Browser/device testing matrix (Phase 2)

---

## 11. Open Questions & Notes

1. **Icon Library:** Does Rora-Quest use lucide-react, heroicons, or another icon library? Use that for chevron icons instead of unicode.

2. **Color Scheme:** Should button colors (black overlay) respect app theme? Check if there's a design system token for overlay opacity.

3. **Animation:** Should image switch include fade/transition, or instant? Current proposal is instant.

4. **Swipe Support:** Should mobile support swipe-to-navigate? Not in PRD, defer to Phase 2.

5. **Keyboard Alternative for Mobile:** On mobile without physical keyboard, users still have button UI. Adequate.

6. **Persistence Across Session:** PRD says "no persistence," but should this be revisited? Mentioned in Risk #7.

---

## References

- **PRD:** `rora-quest/docs/prd/image-gallery-navigation.md`
- **Current Implementation:** `rora-quest/source/apps/web/src/app/tasks/[id]/page.tsx` (lines 97-744)
- **Styling:** `rora-quest/source/apps/web/src/app/globals.css` (lines 580-640)
- **Next.js Documentation:** https://nextjs.org/docs
- **WCAG 2.1 Touch Target:** https://www.w3.org/WAI/WCAG21/Understanding/target-size.html
- **MDN ARIA Live Regions:** https://developer.mozilla.org/en-US/docs/Web/Accessibility/ARIA/ARIA_Live_Regions

---

**Document prepared by:** Architect Agent  
**Ready for:** Engineer implementation phase
