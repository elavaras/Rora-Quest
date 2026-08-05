# PRD: Image Gallery Navigation in Maximized View

## Problem

When viewing task details in Rora-Quest, users can attach diagram images to document their problem-solving approach (sketches, flowcharts, algorithm diagrams). Currently, when a user maximizes an image for detailed viewing, there is no way to browse to the next or previous image without:

1. Minimizing the current image back to the normal task detail view
2. Clicking on a different image
3. Maximizing it again

This creates friction in the workflow when users want to quickly review or compare multiple diagrams associated with a task. Users must repeatedly perform the minimize → click → maximize cycle, which disrupts focus and slows down review workflows.

---

## Target Users

| Persona | Description |
|---------|-------------|
| **Algorithm learner** | Solves DSA problems and attaches multiple hand-drawn diagrams or whiteboard photos to document their approach. Wants to quickly flip through diagrams to review their work or compare different attempts. |
| **Visual problem solver** | Uses diagrams extensively — flowcharts, state machines, dependency graphs. Needs to see multiple diagrams in sequence to understand the problem fully. |
| **Task reviewer** | After completing a task, reviews all attached diagrams in order to verify the solution approach. Quick navigation improves review speed. |

All target users are working within the Task Detail page (`/tasks/[id]`) and interact with the maximized image preview component.

---

## User Stories

### US-1 — Navigate to next image in maximized view

> *As a user reviewing multiple diagram images attached to a task, I want to click a "Next" button in the maximized image preview, so that I can quickly view the next image without minimizing and re-maximizing.*

**Acceptance criteria:**

```
Given I have a task with 3 or more attached images
When I maximize the first image
And I click the "Next" button (or press the right arrow key)
Then the next image in the sequence is displayed
 And the image file name and size are updated in the metadata line
 And the minimized task detail view is not affected
 And the image preview remains maximized

Given I am viewing the last image
When I click "Next" or press the right arrow key
Then nothing happens (no navigation, no error)
 And the button/key is disabled or visually indicates end-of-list
```

### US-2 — Navigate to previous image in maximized view

> *As a user reviewing multiple diagram images, I want to click a "Previous" button in the maximized image preview, so that I can navigate backward through my images without minimizing.*

**Acceptance criteria:**

```
Given I have a task with 3 or more attached images
When I maximize any image except the first
And I click the "Previous" button (or press the left arrow key)
Then the previous image in the sequence is displayed
 And the image file name and size are updated
 And the minimized task detail view is not affected

Given I am viewing the first image
When I click "Previous" or press the left arrow key
Then nothing happens
 And the button/key is disabled or visually indicates start-of-list

Given I have a task with only 1 image
When I maximize it
Then "Previous" and "Next" buttons are either hidden or disabled
```

### US-3 — Quickly scan multiple images with keyboard navigation

> *As a user reviewing multiple diagrams, I want to use arrow keys to navigate forward and backward through images in the maximized view, so that I can rapidly scan all diagrams with just keyboard input.*

**Acceptance criteria:**

```
Given I am viewing a maximized image with other images available
When I press the right arrow key
Then the next image is displayed (same as "Next" button behavior)
 And the image carousel does not require focus on a specific element

When I press the left arrow key
Then the previous image is displayed (same as "Previous" button behavior)

When I press Escape
Then the maximized view minimizes (existing behavior preserved)

Given I am typing in another field elsewhere on the page
When I press arrow keys
Then the keys are passed to the normal input/field, not the image navigator
```

---

## Functional Requirements

1. **Navigation UI**: Add Previous and Next buttons to the detail-takeover-header in the maximized image view component.
   - Buttons must be positioned clearly (e.g., left and right sides of the title).
   - Buttons must be disabled when there are no previous/next images (first/last image).
   - Button labels: "← Previous" and "Next →" (or similar clear icon + text).

2. **Navigation State**: Track the current image index within the maximized view.
   - When an image is first maximized, calculate its index in the task's assets array.
   - Store the current index in component state to enable forward/backward navigation.

3. **Image List Filtering**: Only show attached images in the navigation sequence.
   - Images with `assetType === "DiagramImage"` or similar are included in navigation.
   - Other asset types (if any) are excluded from the navigation carousel.

4. **Keyboard Navigation**: Support arrow key navigation for quick browsing.
   - Right arrow: Navigate to next image.
   - Left arrow: Navigate to previous image.
   - Escape: Minimize (existing behavior, no change).
   - Only respond to arrow keys when the maximized image view is active (take focus into account).

5. **Visual Feedback**: Indicate position in the image sequence.
   - Display a counter (e.g., "Image 2 of 5") in the header next to the file name.
   - Disabled buttons should have reduced opacity or a `disabled` attribute.

6. **Smooth State Management**: Ensure navigation does not create unintended side effects.
   - Changing images does not trigger a task save or modify any task data.
   - Focus and scroll position in the detail view remain unchanged when navigating images.
   - Image metadata (file name, size) updates immediately when navigation occurs.

---

## Non-Functional Requirements

- **Performance**: Image navigation must occur in under 100 ms (pure state update, no API call).
- **Accessibility**: Navigation buttons must have clear `aria-label` attributes; keyboard shortcuts must be discoverable (tooltip or help text).
- **Browser Support**: Must work on all modern browsers (Chrome, Firefox, Safari, Edge) and mobile viewports (if applicable).
- **No API Changes**: Navigation is client-side only; no changes to backend APIs or task data structures.
- **Responsive**: Buttons must remain visible and usable on small screens (mobile, tablet).

---

## Acceptance Criteria

### AC-1: Previous/Next buttons are rendered
```
Given I have a task with 2+ diagram images
When I maximize an image
Then two buttons labeled "← Previous" and "Next →" (or similar) are visible
 And they are positioned adjacent to the title
 And both buttons are clickable/focusable
```

### AC-2: Previous button is disabled at the start
```
Given I have a task with 3 images
When I maximize the first image
Then the "← Previous" button is disabled (grayed out, no click response)
 And the "Next →" button is enabled
```

### AC-3: Next button is disabled at the end
```
Given I have a task with 3 images
When I maximize the third (last) image
Then the "Next →" button is disabled
 And the "← Previous" button is enabled
```

### AC-4: Clicking Next navigates to the next image
```
Given I am viewing image 1 of 3
When I click "Next →"
Then image 2 is displayed immediately
 And the file name and size metadata update
 And the counter now shows "2 of 3" (or similar)
```

### AC-5: Clicking Previous navigates to the previous image
```
Given I am viewing image 2 of 3
When I click "← Previous"
Then image 1 is displayed
 And metadata updates to show "1 of 3"
```

### AC-6: Arrow key navigation works
```
Given I have a maximized image view
When I press the right arrow key
Then the next image is displayed (same as clicking "Next →")

When I press the left arrow key
Then the previous image is displayed (same as clicking "← Previous")
```

### AC-7: Minimize button still works
```
Given I am viewing a maximized image
When I click the "Minimize" button
Then the takeover view is closed
 And the task detail view returns to normal
 And the currently selected image index is not remembered (next maximize starts fresh)
```

### AC-8: Edge case: single image
```
Given a task with only 1 diagram image
When I maximize it
Then both Previous and Next buttons are disabled or hidden
 And the counter shows "1 of 1"
```

### AC-9: No data modifications
```
Given I navigate through multiple images
When I click Previous or Next multiple times
Then no task data is saved
 And the task remains in its current state (status, metadata, etc.)
 And no API calls are triggered
```

### AC-10: Image order consistency
```
Given I navigate images in any order (Next, Previous, etc.)
When I minimize and then re-maximize the first image
And then click "Next"
Then the second image in the original sequence is displayed
 And the navigation order is consistent
```

---

## Edge Cases

1. **No images attached**: If a task has no images, the takeover should never be triggered by clicking an image (current behavior preserved).

2. **Only one image**: Navigation buttons are either hidden or disabled; no error.

3. **Images deleted while viewing**: If an image is deleted via another action (e.g., background API call) while the user is viewing, the behavior is undefined for v1 — assume this does not happen in the single-user context.

4. **Mixed asset types**: If a task has both diagram images and other assets (e.g., documents), only images are included in the navigation carousel. Other asset types should not be shown in the image viewer.

5. **Keyboard focus**: If focus is inside a form field elsewhere on the page, arrow keys should not trigger image navigation (browser default behavior).

6. **Empty file name or metadata**: If an image has a missing or empty file name, display a placeholder (e.g., "Untitled image" or "Image (no name)").

7. **Very large images**: Images larger than the viewport should still be navigable; overflow handling is the same as for a single maximized image (existing CSS applies).

---

## Non-Goals

- **Slideshow/auto-play mode**: Not in scope for v1.
- **Thumbnail carousel**: Not in scope; users see only the full image.
- **Drag-to-swipe gestures**: Not in scope for v1 (desktop-first).
- **Fullscreen mode**: Not in scope; maximized modal is the viewer.
- **Image editing or annotation**: Not in scope.
- **Undo/redo for navigation**: Not in scope.
- **Zoom controls**: Assume existing image CSS handles zoom (if any); no additional zoom UI.
- **Image metadata editing**: Editing file names, sizes, etc., is not in scope.
- **Mobile swipe navigation**: Keyboard and button-based navigation only for v1.

---

## Open Questions

1. **Image ordering**: Should images be ordered by creation date, or by the order they appear in the API response? 
   - **Assumption for v1**: Order by creation date (ascending, oldest first); if creation date is unavailable, use the order from the API response.

2. **Focus management**: When an image is navigated via arrow key, should focus move to the "Next" or "Previous" button, or stay where it was?
   - **Assumption for v1**: No focus change; the image updates but focus remains on the currently focused element (or keyboard handler).

3. **Disabled button styling**: Should disabled buttons be grayed out, have `opacity: 0.5`, or use another visual treatment?
   - **Assumption for v1**: Use CSS `disabled` attribute and existing app theme (defer to designer/existing component library).

4. **Button position on mobile**: Should Previous/Next buttons remain at the top of the image, or move to the bottom on small screens?
   - **Assumption for v1**: Keep buttons at the top in the header for consistency; make header responsive if needed.

5. **Counter format**: Should the counter be "Image X of Y" or "X/Y" or hidden if only 1 image?
   - **Assumption for v1**: "Image X of Y" format shown regardless of count; hidden is also acceptable.

---

## Success Metrics

1. **Adoption**: % of users who interact with the Previous/Next buttons in tasks with 2+ images (measured via analytics/usage tracking).

2. **Engagement improvement**: Average time spent in the maximized image view for users with multiple images increases (indicating more detailed review).

3. **User satisfaction**: Survey or feedback from users indicating the feature reduces friction in image review workflows.

4. **Performance**: Page load time and image navigation latency remain under 100 ms (verified via performance testing).

5. **Accessibility**: Zero reported accessibility issues related to keyboard navigation or button labels in user feedback and accessibility audits.

---

## Implementation Notes

### Frontend (Next.js)

**Modified file**: `src/app/tasks/[id]/page.tsx`

- Update `DetailTakeover` type to track current image index.
- Add state variable: `const [expandedImageIndex, setExpandedImageIndex] = useState<number | null>(null);`
- Add helper function to get navigable images (filtered list).
- Add handlers: `handleNextImage()`, `handlePreviousImage()`.
- Add keyboard event listener for arrow keys (attach to the image container or use `useEffect` with `window.addEventListener`).
- Update the detail-takeover-header JSX to include Previous/Next buttons and counter.
- Apply `disabled` attribute to buttons based on index bounds.

**Modified file**: `src/app/globals.css`

- Add CSS for disabled button states (opacity, cursor pointer, etc.).
- Ensure button layout is responsive.

### Backend

**No changes required** for this release. Navigation is purely client-side state management.

### Database

**No schema changes required**.

---

## Approval

- **Product Manager approval**: Pending human review
- **Architecture/Design review**: Required before implementation begins
- **Approved at**: TBD
