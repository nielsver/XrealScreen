# ADR-0015: Static website on a Linux LiteSpeed host

- **Status:** Accepted
- **Date:** 2026-10-25

## Context
XrealScreen needs a public home page: what it is, downloads, a setup guide, developer pages, and the privacy policy
URL the Microsoft Store submission requires (ADR-0005). The host is a Linux LiteSpeed web hotel at
`xrealscreen.timeonline.se` with FTP access. The user wanted plain HTML5, CSS and JavaScript, in English and Swedish.

## Decision
- **Static site in `web/`**, uploaded as is: hand-written HTML, one stylesheet (`assets/css/site.css`), two small
  scripts (`theme.js` in `<head>` to avoid a theme flash, `site.js` deferred). No build step, no framework, no Node.
  Every page works without JavaScript; JS only adds the mobile menu, theme toggle, looks tabs and lightbox.
- **Languages:** English at `/`, Swedish at `/sv/` with the same file names; `hreflang` alternates and a language
  switch that keeps the current page. App UI names (Glasses anchor, Screens, Start workspace) stay in English on the
  Swedish pages because the app is English.
- **Shared header and footer are repeated** in every page (12 files). A change to them is made in all pages.
- **Look:** colours from the app's Stage look (dark navy, orange accent) with a light theme; Windows system fonts
  (Segoe UI Variable, Cascadia), so no font files and no third-party requests.
- **Privacy by design:** no cookies, analytics, CDNs or embeds; `.htaccess` sets a strict CSP (`'self'` only), HSTS and
  other security headers. The privacy policy covers the app (no data collected, local files only) and the site.
- **Images:** WebP copies of `images/` (ImageMagick), plus `og.jpg` for link previews.
- **Deploy:** `.github/workflows/website.yml` runs `tools/check-website.py` (HTML structure, ids, links, anchors) on
  pull requests, and on `main` uploads `web/` with `lftp` over FTPS (secrets `FTP_SERVER`, `FTP_USERNAME`,
  `FTP_PASSWORD`, `FTP_DIR`). No third-party deploy action, so the FTP password goes to no one else's code. The
  upload never deletes files on the server.

## Consequences
- Editing the site needs only a text editor; preview with `python -m http.server` in `web/`.
- Header/footer changes touch 12 files. If the site grows past about 10 pages per language, revisit with a small
  generator or a static site generator.
- Download buttons point to GitHub `releases/latest`; until the first release exists the page says "Preview" and
  links to building from source.
