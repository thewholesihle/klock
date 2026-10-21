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

An original "lume" direction built around the product itself: a graphite hero with a glowing lime-lit dial and a
minute-ruler along its foot, flat full-width bands, a bento feature grid, heavy Satoshi Black headlines, rectangular
10 px-radius buttons and one accent colour (lume green `#C6F36B`). Palette: graphite `#101318`, paper `#F5F4EF`,
bone `#E8E6DD`, mist `#D5DEE3`, ink `#171B21`. The page background eases between these bands as you scroll.

## How it works

* `js/clock.js` is a canvas port of the app's renderer: the same layout, colours and spring-based movement maths.
* `js/main.js` holds the demo state (dial, lume, movement, numerals, day/night) and drives the page colour from the
  section in view (`[data-bg]`). Only the framed demo screen takes on the chosen dial's colours (`--bg1`/`--bg2`).
* Real screenshots in `img/` are frames rendered by the app itself (`Klock.scr --capture`).
* Respects `prefers-reduced-motion` (no marquee, no fades). The Night theme in the demo is opt-in.
* Body text uses Satoshi Regular; the clock numerals use Satoshi Black, Panchang, Clash Display and Melodrama.

To preview locally: `python -m http.server 8099` inside this folder, then open <http://localhost:8099>.
