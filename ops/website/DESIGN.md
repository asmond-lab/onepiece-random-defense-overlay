# randypick.com design system (v2, reference-driven)

Source: two user-supplied reference screenshots (dark marketplace landing pages). Extracted grammar, not copied assets.

## Tokens
- Background `#0a0a0a`; panel `#0e0e0e`; raised panel `#131313`.
- Lines: `rgba(255,255,255,.09)` hairline, `rgba(255,255,255,.18)` strong.
- Text: primary `#ededed`; muted `#8f8f8f` (5.9:1 on bg); dim `#7a7a7a` (4.6:1, small mono only).
- Accent: compass gold `#f2b544` (one warm accent per view); signal teal `#38d39f` (status dot, bar field only).
- Type: sans = system Korean stack (Pretendard/SUIT if installed, Segoe UI, Apple SD Gothic Neo, Malgun Gothic), weight 400-500, tracking -0.02em on display; mono = ui-monospace stack, 11-12px, uppercase, tracking .08em for labels.
- Scale: display clamp(40px, 6vw, 84px) / h2 clamp(28px, 3.4vw, 44px) / body 16px / small 14px / label 11px.
- Spacing 4px base; section padding clamp(72px, 10vw, 140px); container 1360px, gutter clamp(20px, 4vw, 48px).
- Radius 0 everywhere (square buttons, panels). Borders do the structure; no shadows except the hero image fade.

## Primitives
- `.label` mono caps index label ("FIG.01", "01 / 실행").
- `.btn` square, 40px tall, mono caps 12px, trailing chevron; `.btn--solid` light fill; `.btn--line` 1px strong border; `.btn--pending` line + diagonal hatch + dim text (download is not available).
- `.title-duo` heading: first phrase primary, continuation muted.
- `.grid-lines` 1px-gap grid on the line color, cells on panel color.
- `.tab` nav chip with trailing dim index number.
- `.fig` inline SVG line diagram, stroke muted, one gold accent, mono 10px labels.
- Texture layers (CSS only): dot dither, vertical bar field, status bar strip. All `aria-hidden`.

## Motion
- Dither drift 18s linear, bar shimmer 6s, transform/opacity only. All disabled under `prefers-reduced-motion`.

## Constraints
- CSP: no JS, no inline style, no external fonts/images. Images live in `/assets` as WebP.
- Dark only (`color-scheme: dark`); the references are dark-native.
- Download controls always read 준비 중 and never link to a binary.

## Accepted debt
- No self-hosted Korean web font (size); rendering depends on the OS Korean font.
