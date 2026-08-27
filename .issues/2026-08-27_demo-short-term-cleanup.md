---
date: 2026-08-27
type: Feature Plan
severity: Medium
status: Open
owner: Demo Feature Overseer
parent: 2026-08-26_demo-missing-features-implementation.md
---

# Demo Short-Term Cleanup

## Goal

Polish and harden the demo feature coverage implementation completed on 2026-08-27.

## Items

### 1. CSS polish for DemoHome grouped layout
- Add CSS for `.launchpad__group` and `.launchpad__group-label` classes
- Verify responsive behavior on mobile viewports
- Test with all 9 scenarios visible

### 2. Suggestion service hardening
- Add unit tests for `DemoSuggestionService` route matching
- Verify `IAdaptiveSuggestionService` is resolved correctly in DI
- Test edge cases: null session ID, empty currentContext

### 3. Session browser UX improvements
- Add loading skeleton instead of progress spinner
- Handle case where `IConversationStore` returns empty history gracefully
- Add "no sessions" illustration/CTA to start a conversation

### 4. Inspector page polish
- Add guidance text when no runs are recorded yet
- Consider auto-refresh or polling for new runs
- Test with multiple agent sessions

### 5. Documentation alignment
- Update each feature guide in `docs/demo/` to reference the new pages
- Add step-by-step instructions for session browser, generative UI, inspector, and action render showcases
- Verify all cross-references in docs/demo/README.md are valid

### 6. Build verification
- Ensure `dotnet build` passes with zero warnings (not just zero errors)
- Address any remaining NU1903 SQLite vulnerability warnings
- Verify multi-target build (net8.0/net9.0/net10.0) still works
