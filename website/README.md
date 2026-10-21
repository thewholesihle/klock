# Klock landing page

A dependency-free static site (no build step, no third-party requests). Deploy the `website` folder as-is.

## Deploy to Netlify

* **Drag and drop:** drop this folder onto <https://app.netlify.com/drop>.
* **Git:** set *Base directory* to `website`, *Build command* empty, *Publish directory* `.` (`netlify.toml` already says so).
* **CLI:** `npx netlify-cli deploy --dir website --prod`

`netlify.toml` adds a strict Content-Security-Policy (the page uses no inline scripts, styles or external hosts),
security headers, long caching for fonts, and a 404 page.

## Before you go live

1. **Installers:** releases are hosted on GitHub. Replace `OWNER/klock` in `index.html` (download buttons, footer) and in
   `src/Klock.Core/AppInfo.cs` (`GitHubRepo`) with the real repository. After `build.ps1`, create a release tagged
   `v<version>` and attach both `Klock-Setup-*.exe` files plus `dist/latest.json` (the app's update check reads it).
2. **Open Graph image:** `og:image` is relative so it works locally. Set it to an absolute URL
   (for example `https://your-site.netlify.app/img/og.jpg`) so link previews work on social networks.
3. **Fonts:** Satoshi, Panchang, Clash Display and Melodrama are self-hosted unmodified from `fonts/` under the ITF
   Free Font License (self-hosting on your own site is permitted). Don't republish the font files anywhere else,
   and keep `fonts/` out of public repositories.

## Design

Modelled on the design system of meetradial.com: warm cream sheets (`#FEFBED`) with very large radii floating on a
pastel page colour that changes section by section (orange, blue, green, purple), brown ink (`#625543`), light
Satoshi type, outlined pill buttons, hairline dividers, a floating nav pill under a marquee announcement bar, and a
numbered three-step block. The hero is the one deliberate departure: a soft dawn mesh gradient (peach, rose, lilac)
with film grain instead of concentric rings. The page stays light for everyone.

## How it works

* `js/clock.js` is a canvas port of the app's renderer: the same layout, colours and spring-based movement maths.
* `js/main.js` holds the demo state (dial, lume, movement, numerals, day/night) and drives the page colour from the
  section in view (`[data-bg]`). Only the framed demo screen takes on the chosen dial's colours (`--bg1`/`--bg2`).
* Real screenshots in `img/` are frames rendered by the app itself (`Klock.scr --capture`).
* Respects `prefers-reduced-motion` (no marquee, no fades). The Night theme in the demo is opt-in.
* Body text uses Satoshi Regular; the clock numerals use Satoshi Black, Panchang, Clash Display and Melodrama.

To preview locally: `python -m http.server 8099` inside this folder, then open <http://localhost:8099>.

## SEO

Everything lives in `index.html` and the root files. The site URL is `https://kloock.netlify.app`:
if it changes, update it (canonical, `og:*`, `twitter:*`, JSON-LD, `sitemap.xml`, `robots.txt`, `llms.txt`).

* Title, description, canonical, robots directives, full Open Graph and Twitter cards with an absolute `og:image`.
* JSON-LD: `WebSite`, `SoftwareApplication` (OS, version, price, download URL, screenshots) and `FAQPage`
  (mirrors the visible FAQ section; keep the two in sync).
* One `h1`, then `h2` per section and `h3` for items; the keyword sentence sits under the `h1`.
* `sitemap.xml` with image entries, `robots.txt` pointing to it, `site.webmanifest`, `llms.txt` for AI crawlers,
  `noindex` on the 404 page, revalidating cache on the HTML.
* After deploying, submit `sitemap.xml` in Google Search Console and Bing Webmaster Tools.
