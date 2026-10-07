# RandyPick intro site - visual verification

Build under test: local Worker (`npx wrangler dev --local --port 8792`, wrangler 4.127.1), source as of 2026-10-04.
Browser: headless Chromium 151 (Playwright cache `chromium-1234`) driven by playwright-core 1.58.2.
Desktop shots: 1440x900 viewport, DPR 1. Mobile shots: 390x844 viewport, DPR 2, `isMobile` + touch. All shots full-page, captured after `networkidle` and `document.fonts.ready`.

## Measured evidence

| Screenshot | URL | HTTP | scrollWidth / viewport | Elements past viewport edge | Text clipped (scrollWidth > clientWidth) | Console errors | CSP violations | Images |
|---|---|---|---|---|---|---|---|---|
| home-desktop-light.png | / | 200 | 1440 / 1440 | 0 | 0 | none | none | 0 `<img>` (all graphics inline SVG) |
| home-desktop-dark.png | / | 200 | 1440 / 1440 | 0 | 0 | none | none | 0 `<img>` |
| home-mobile-light.png | / | 200 | 390 / 390 | 0 | 0 | none | none | 0 `<img>` |
| home-mobile-dark.png | / | 200 | 390 / 390 | 0 | 0 | none | none | 0 `<img>` |
| download-mobile.png | /download | 200 | 390 / 390 | 0 | 0 | none | none | 0 `<img>` |
| 404-mobile.png | /does-not-exist | 404 | 390 / 390 | 0 | 0 | 1: "Failed to load resource: 404" for the document itself (expected) | none | 0 `<img>` |

No failed requests and no 4xx/5xx subresources on any page. Section order on `/` (DOM): hero, features, how, safety, data, faq, notice.

Hero control computed state (all 4 home shots): text "다운로드 · 준비 중", `cursor: default`, dashed border, hatched `repeating-linear-gradient` fill, clock icon. It is still an `<a href="/download">` that leads to the "준비 중" page.

Contrast (WCAG ratio, from tokens): light muted ink-700 on sand-50 9.0, on sand-100 8.26; light accent gold-700 on sand-50 5.86, on sand-100 5.37; light pending text 13.17; dark muted navy-300 on navy-950 9.65, on navy-850 8.45; dark accent gold-300 12.94; dark pending text 13.07; safety band muted 9.14; overlay mock muted ~7.2.

## Checklist

Items 1-7 are the requested checklist. Item 8 (natural Korean line breaks) is added from the visual-qa skill (CJK precision) and is listed separately so it can be weighed on its own.

| Screenshot | Item | Verdict | Observation |
|---|---|---|---|
| home-desktop-light.png | 1 Korean renders (no tofu) | PASS | All Hangul renders in Malgun Gothic across header, hero, cards, mock panel and footer; no missing-glyph boxes. |
| home-desktop-light.png | 2 No horizontal overflow | PASS | scrollWidth 1440 = viewport; no element extends past the edge. |
| home-desktop-light.png | 3 No overlapping or clipped text | PASS | Rotated overlay mock clears the hero copy; no clipped descenders; kbd chips in step 04 sit inline cleanly. |
| home-desktop-light.png | 4 Hero download control reads "다운로드 · 준비 중" and looks disabled | PASS | Hatched sand fill, dashed rim, clock icon, note "지금은 내려받을 수 없습니다" below; clearly reads "not yet" next to the solid ghost button. |
| home-desktop-light.png | 5 Readable contrast | PASS | Body and muted text 8-9:1, gold accent 5.4-5.9:1. |
| home-desktop-light.png | 6 Sections in order, sensible spacing | PASS | Hero, 01 Features, 02 How, 03 Safety (dark band), 04 Data, 05 FAQ, 06 Notice, footer; consistent 6rem section padding. Minor: the "06 · NOTICE" eyebrow renders slate instead of gold (see notes). |
| home-desktop-light.png | 7 No broken images | PASS | No `<img>` elements; brand mark, card icons and safety icons (inline SVG) all render. |
| home-desktop-light.png | 8 Natural Korean line breaks | PASS | Headings and leads break cleanly. Feature cards break "보여 / 줍니다" between main and auxiliary verb, which is an allowed break. |
| home-desktop-dark.png | 1 Korean renders (no tofu) | PASS | Same as light; all Hangul renders. |
| home-desktop-dark.png | 2 No horizontal overflow | PASS | scrollWidth 1440 = viewport. |
| home-desktop-dark.png | 3 No overlapping or clipped text | PASS | No overlaps or clipping. |
| home-desktop-dark.png | 4 Hero download control reads "다운로드 · 준비 중" and looks disabled | PASS | Navy hatched fill with dashed rim and gold clock; reads as pending, distinct from the outlined ghost button. |
| home-desktop-dark.png | 5 Readable contrast | PASS | Navy-100 / navy-300 text on navy-950/850 at 8.4-13:1; gold accent 12.9:1. |
| home-desktop-dark.png | 6 Sections in order, sensible spacing | PASS | Same order and spacing. The always-dark safety band is less separated from its dark neighbours but is still marked by its grid and gold glow. |
| home-desktop-dark.png | 7 No broken images | PASS | All inline SVG render; brand mark legible on dark header. |
| home-desktop-dark.png | 8 Natural Korean line breaks | PASS | Same breaks as light; no defects. |
| home-mobile-light.png | 1 Korean renders (no tofu) | PASS | All Hangul renders. |
| home-mobile-light.png | 2 No horizontal overflow | PASS | scrollWidth 390 = viewport; rotated mock is contained by the hero's overflow:hidden. |
| home-mobile-light.png | 3 No overlapping or clipped text | PASS | Nav wraps under the brand without overlap; chips wrap to two rows; mock rows, gauges and alert fit. |
| home-mobile-light.png | 4 Hero download control reads "다운로드 · 준비 중" and looks disabled | PASS | Hatched, dashed, clock icon; stacked above the ghost button. |
| home-mobile-light.png | 5 Readable contrast | PASS | Same tokens as desktop light. |
| home-mobile-light.png | 6 Sections in order, sensible spacing | PASS | Single column in correct order; cards stack with even 1rem gaps. |
| home-mobile-light.png | 7 No broken images | PASS | All inline SVG render. |
| home-mobile-light.png | 8 Natural Korean line breaks | FAIL | Notice paragraph (#notice .notice p) orphans the particle: "티모지지(TMO.GG) / 와 관련이". FAQ last summary splits "바꿀 수 / 있나요?". Hero .btn-note splits "내려받을 / 수 없습니다". How-section .modes .note splits "정할 / 수 있어요". |
| home-mobile-dark.png | 1 Korean renders (no tofu) | PASS | All Hangul renders. |
| home-mobile-dark.png | 2 No horizontal overflow | PASS | scrollWidth 390 = viewport. |
| home-mobile-dark.png | 3 No overlapping or clipped text | PASS | No overlaps or clipping. |
| home-mobile-dark.png | 4 Hero download control reads "다운로드 · 준비 중" and looks disabled | PASS | Navy hatched pending style, legible. |
| home-mobile-dark.png | 5 Readable contrast | PASS | Same tokens as desktop dark. |
| home-mobile-dark.png | 6 Sections in order, sensible spacing | PASS | Correct order; even spacing. |
| home-mobile-dark.png | 7 No broken images | PASS | All inline SVG render. |
| home-mobile-dark.png | 8 Natural Korean line breaks | FAIL | Same four breaks as mobile light: notice "(TMO.GG) / 와", FAQ "바꿀 수 / 있나요?", hero .btn-note "내려받을 / 수 없습니다", .modes note "정할 / 수 있어요". |
| download-mobile.png | 1 Korean renders (no tofu) | PASS | Status pill, heading, body and footer all render. |
| download-mobile.png | 2 No horizontal overflow | PASS | scrollWidth 390 = viewport. |
| download-mobile.png | 3 No overlapping or clipped text | PASS | Panel content fits; heading "다운로드는 / 준비 중이에요" wraps cleanly. |
| download-mobile.png | 4 Hero download control | N/A | No hero on this page; the "준비 중" status pill uses the same hatched pending style. |
| download-mobile.png | 5 Readable contrast | PASS | Light tokens, 8-13:1. |
| download-mobile.png | 6 Sections in order, sensible spacing | PASS | Header, centred panel on grid/glow backdrop, footer; balanced spacing. |
| download-mobile.png | 7 No broken images | PASS | Brand mark and clock icon render. |
| download-mobile.png | 8 Natural Korean line breaks | PASS | Body breaks at word boundaries ("준비가 / 끝나면" is a normal body-text break). |
| 404-mobile.png | 1 Korean renders (no tofu) | PASS | All Hangul renders. |
| 404-mobile.png | 2 No horizontal overflow | PASS | scrollWidth 390 = viewport. |
| 404-mobile.png | 3 No overlapping or clipped text | PASS | "404" display numerals and heading do not clip. |
| 404-mobile.png | 4 Hero download control | N/A | No hero on this page. |
| 404-mobile.png | 5 Readable contrast | PASS | Light tokens. |
| 404-mobile.png | 6 Sections in order, sensible spacing | PASS | Header, centred panel, footer. Minor: "404" renders slate instead of gold (see notes). |
| 404-mobile.png | 7 No broken images | PASS | Brand mark renders. |
| 404-mobile.png | 8 Natural Korean line breaks | FAIL | h1 in .panel splits "페이지를 찾을 / 수 없어요"; the natural break is "페이지를 / 찾을 수 없어요". |

## Notes (not checklist failures)

- Accent colour is overridden on two elements by selector specificity in styles.css: `.notice p` (color: --text-muted) beats `.eyebrow` (color: --accent) on "06 · NOTICE", and `.panel p` beats `.panel__code` on "404". Both render slate instead of gold, unlike the other eyebrows.
- The dual-oracle subagent review from the visual-qa skill was not run: this session has no subagent tool. All judgments above come from one reviewer looking at the screenshots plus the measured evidence.
- Motion, hover/focus states and FAQ open states were not captured; the task asked for static full-page shots only.
