# LocalChromeStore — Logo Prompt Set

Project: **LocalChromeStore** — a personal store UI for Chromium extensions sourced from your own GitHub repos. One-click install / uninstall, browser launcher, native Windows desktop app.

All five prompts ship a **transparent-background PNG with alpha channel (RGBA)**. No flat fill, no inner panel, the glyph itself must composite cleanly onto any host surface.

---

## 1. Minimal icon — favicon / toolbar (16-128 px)

> Flat single-color glyph for the LocalChromeStore project, a personal Chromium extension store. Render a stylized **storefront awning over a chevron-shaped puzzle piece** — the awning is three short stripes at the top, the puzzle piece below has one tab and one socket, instantly readable as "extensions". Single color: `#cba6f7` (Catppuccin Mauve). Crisp geometry, no gradients, no shadows, no text. **Transparent background — no rectangle, no panel, only the glyph is opaque.** Output: PNG with alpha channel (RGBA) at 256×256, vector-friendly so it down-samples cleanly to 16 px. The shape must read as one unified silhouette at 16 px.

## 2. App icon — Android adaptive / Chrome Web Store / Windows tile

> App icon for **LocalChromeStore**, a personal Chromium extension store. Compose a soft **rounded square**, where the foreground is a **storefront with a puzzle-piece shopfront** — three pillars holding up a striped awning, and the central pillar is a Chromium-extension puzzle tile. Foreground colors: deep blue-violet body `#cba6f7`, sapphire accents `#74c7ec`, with a thin highlight on the awning stripes for depth. Foreground glyphs are opaque; **the canvas outside the rounded square is fully transparent.** The rounded-square fill itself can be an opaque dark surface (`#1e1e2e`) since iOS-style icons are expected to ship with a fill. Subtle inner shadow to give the awning lift. No text. Output: PNG with alpha channel (RGBA), 1024×1024, designed to crop cleanly to the 66% safe zone of Android adaptive icons.

## 3. Wordmark — header / splash

> Wordmark for **LocalChromeStore**. Render the literal text "LocalChromeStore" in a **modern geometric sans-serif**, semi-bold weight, with the three words visually distinguished by color: "Local" in soft white (`#cdd6f4`), "Chrome" in mauve (`#cba6f7`), "Store" in sapphire (`#74c7ec`). Letterforms are clean and self-contained — no underline, no surrounding shape. To the immediate left of the text, prepend a small **storefront-awning glyph** in mauve, sized to match the cap height. Tight kerning, balanced optical alignment. **Background fully transparent — no rectangle, no panel — only the glyphs are opaque.** Output: PNG with alpha channel (RGBA), 2048×512, with at least 32 px of transparent padding on every side so it can be dropped straight into a README hero or splash without further trimming.

## 4. Emblem — README header / about dialog

> Circular emblem badge for **LocalChromeStore**. A clean ring border in mauve `#cba6f7` (3 px stroke, no fill) encloses a **storefront-with-puzzle-piece glyph** centered inside. Above the glyph, a small banner-ribbon shape carries no text but suggests "store / shop". Below the glyph, three small dots representing the install / uninstall / launch actions in green `#a6e3a1`, red `#f38ba8`, sapphire `#74c7ec`. Everything inside the ring is opaque artwork; **everything outside the ring (and the area inside the ring not covered by artwork) is fully transparent.** No text, no signature. Output: PNG with alpha channel (RGBA), 1024×1024.

## 5. Abstract — symbolic / marketing

> Abstract / symbolic logo for **LocalChromeStore**. A floating **stack of three rounded extension-tile cards**, fanned diagonally like a hand of cards, each card a different shade — front card mauve `#cba6f7`, middle sapphire `#74c7ec`, back muted overlay `#6c7086`. The front-most card has a small puzzle-piece notch cut out of its top-left corner. A subtle downward-pointing chevron near the bottom-right of the front card hints at "install / pull down". Soft glow halo behind the stack in mauve at 25% opacity to give it lift. **Background fully transparent — only the cards, the notch silhouette, and the halo are present.** No text. Output: PNG with alpha channel (RGBA), 1024×1024.

---

## Verification checklist (every PNG)

- [ ] `magick identify -format '%[channels]'` returns `rgba` / `srgba` / `graya`
- [ ] Drop onto pure white, pure black, and Catppuccin base `#1e1e2e` — all three should look right
- [ ] Down-sample the minimal icon to 16×16 — silhouette still reads as "store / extension"
- [ ] No JPEG, no flattened PNG, no hidden white rectangle behind transparency
