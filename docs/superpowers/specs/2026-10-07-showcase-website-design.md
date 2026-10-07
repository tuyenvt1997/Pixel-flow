# Showcase Website — Design

**Status:** approved in chat (2026-10-07).

## Goal

A one-page portfolio site for job applications. Recruiters and engineers should see the gameplay and the engineering behind it. The content is in English.

## Hosting

- The static files live in `website/`. `docs/` holds internal specs and is not published.
- `.github/workflows/pages.yml` deploys `website/` to GitHub Pages on a push to `master`, and it can also be run by hand.
- The URL is `https://tuyenvt1997.github.io/Pixel-flow/`.
- One-time setup: Settings → Pages → Source: "GitHub Actions".

## Files

| File | Role |
|---|---|
| `website/index.html` | All the content: plain HTML, no build step. |
| `website/style.css` | Styles. The palette follows the game: a blue gradient, sky blue, yellow, glossy cubes. |
| `website/assets/gameplay.mp4` | The user's 70 s gameplay recording (488×626). |

## Sections

1. **Hero**: the "PIXEL FLOW" logo, a tagline, and buttons for "Watch gameplay" and GitHub.
2. **Gameplay video**: in a phone-style frame, with `controls`, `muted`, `playsinline` and `preload="metadata"`.
3. **How it plays**: the belt, matching colours on the outer layer, 5 waiting slots, win and lose.
4. **Engineering highlights**:
   - layered asmdefs;
   - GPU instancing;
   - zero-alloc gameplay checked by tests;
   - O(1) four-sided fronts;
   - a fixed-tick belt with interpolated visuals;
   - RLE/JSON levels and a tank generator;
   - editor-generated scenes.
5. **Architecture**: a CSS diagram of Data → Core → View / Controller → Meta.
6. **Tech stack & testing**: Unity 6, C#, URP, TextMeshPro, Unity Test Framework. Includes the test counts and the performance budgets.
7. **Footer**: a GitHub link.

## Assets

The root `.jpg` files are reference shots of the original commercial game. They are not used. The video is the only media.

## Verification

- Open `website/index.html` locally.
- Check desktop and mobile widths.
- Check that the video plays and the links resolve.
