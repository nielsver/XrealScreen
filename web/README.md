# XrealScreen website

Static site for **https://xrealscreen.timeonline.se** (Linux LiteSpeed host), plain HTML5/CSS/JS with no build step
([ADR-0015](../docs/decisions/ADR-0015-website.md)). This README is not uploaded.

## Layout
| Path | What |
|------|------|
| `index.html`, `download.html`, `developers.html`, `faq.html`, `privacy.html`, `404.html` | English pages |
| `sv/` | The same pages in Swedish (+ `sv/.htaccess` for the Swedish 404) |
| `assets/css/site.css` | All styles; colour tokens at the top (dark default, light theme) |
| `assets/js/theme.js` | Runs in `<head>`: saved theme, `js` class (no flash) |
| `assets/js/site.js` | Mobile menu, theme toggle, looks tabs, screenshot lightbox |
| `assets/img/` | WebP screenshots, icons, `og.jpg` (link preview, 1200×630) |
| `.htaccess` | HTTPS redirect, CSP and security headers, caching, 404 |
| `robots.txt`, `sitemap.xml`, `site.webmanifest`, `favicon.ico` | SEO and icons |

## Edit
- Header and footer are repeated in all 12 pages: change them everywhere (search and replace across `web/`).
- Change both languages: every English page has a Swedish twin in `sv/`.
- New page: copy an existing one, update `<title>`, description, canonical, the `hreflang` links, `aria-current` in
  the nav, and add both URLs to `sitemap.xml`.
- Changed the privacy policy? Update the date on both privacy pages.
- No inline `<script>` or `style=""` attributes: the CSP blocks them. JSON-LD (`application/ld+json`) is fine.
- New screenshot (check it for names, email and desktop contents first):
  `magick images\x.png -quality 82 web\assets\img\x.webp`; always set `width`, `height`, `alt` and `loading="lazy"`.

## Preview and check
```powershell
cd web; python -m http.server 8000      # http://localhost:8000
python tools/check-website.py           # from the repo root: HTML structure, ids, links, anchors
```

## Deploy
Pushing changes in `web/` to `main` runs `.github/workflows/website.yml`: check, then upload over FTPS with `lftp`.
Also **Actions → Website → Run workflow**. The upload overwrites files and never deletes any on the server.

One-time setup in GitHub (**Settings → Secrets and variables → Actions**):

| Secret | Example |
|--------|---------|
| `FTP_SERVER` | `ftp.timeonline.se` (the host name on the FTP certificate) |
| `FTP_USERNAME` | FTP account for the site |
| `FTP_PASSWORD` | Its password |
| `FTP_DIR` | Document root of the subdomain, e.g. `/public_html` or `/xrealscreen.timeonline.se` |

Manual upload: copy the contents of `web/` (including `.htaccess` and `sv/.htaccess`, without this README) to the
document root with any FTPS client.
