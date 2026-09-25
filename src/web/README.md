# Website (src/web)

The public website of JassCardEye: https://jasscardeye.yarx.ch

It exists because both stores want a support page and a privacy policy on the web before they review an
app, and because a player or a reviewer should find the manual without installing anything. Six pages per
language: Start (`/de`), Anleitung (`/de/anleitung`), Support (`/de/support`), Datenschutz
(`/de/datenschutz`), Impressum (`/de/impressum`) and Lizenzen (`/de/lizenzen`), in German, French
(`/fr`), Italian (`/it`) and English (`/en`); see "Languages".

## Three URLs that must never change

These addresses are entered in App Store Connect and in the Play Console. A renamed route breaks the store
entry, and nothing warns about it until a reviewer or a player follows the link. They carry no language:
each is a redirect page that sends the visitor on to the page in their language (see "Languages").

- `https://jasscardeye.yarx.ch` - marketing URL in the App Store and TestFlight, website in Google Play
- `https://jasscardeye.yarx.ch/support` - support URL in the App Store
- `https://jasscardeye.yarx.ch/datenschutz` - privacy policy in the App Store, TestFlight and Google Play

What the store entries say is kept in `src/app/ios/store/listing.md` and
`src/app/android/store/listing.md`.

## Stack

Angular 22 with standalone components, styled with Tailwind CSS 4 through its PostCSS plugin
(`.postcssrc.json`). There are no component stylesheets: every style is a Tailwind utility in a template,
and the few colours the site takes from the apps are theme tokens in `src/styles.css`.

A page is a component in `src/app/pages/<language>/`: an `.html` template for the text, and a `.ts` file
for what repeats - features, scan rules, questions, credits. Each language has all six, and its
`index.ts` exports them.

## Working locally

```bash
cd src/web
npm ci          # the exact versions in package-lock.json
npm start       # development server with reload on http://localhost:4200, German, without a language path
npx ng serve --configuration fr   # the same in French (fr, it, en)
npm run build   # production build: every page of every language prerendered, then the root
```

CI builds with Node 22. The Angular 22 build tools accept Node 22.22.3 or a later 22.x, 24.15.0 or a later
24.x, and 26 or newer.

`dist/jasscardeye-web/browser/` is exactly what gets published: one folder per language (`de/`) with one
`index.html` per page (`de/anleitung/index.html`, ...), the script and stylesheet bundles and everything
from `public/`, and above them the redirect pages and `staticwebapp.config.json`. Next to it,
`dist/jasscardeye-web/3rdpartylicenses.txt` lists the licences of the libraries in the bundle. That file is
not published, but it is where the «Diese Webseite» line on the Lizenzen page comes from.

## Rendering

Every page is rendered to plain HTML at build time: `outputMode: "static"` in `angular.json`, and
`RenderMode.Prerender` for every route in `src/app/app.routes.server.ts`. The pages are therefore complete
before any script runs - which is what search engines and store reviewers see - and hosting needs nothing
but static files. `src/main.server.ts` exists for that build step alone.

In the browser, Angular then hydrates the prerendered page instead of drawing it again, with event replay,
so a tap made while the script loads is not lost. From there, a link switches the page without reloading.

## Adding a page

1. `<name>.ts` and `<name>.html` in `src/app/pages/<language>/` for every language, named after the German
   route like the other pages, and exported from each `index.ts`.
2. A route in `src/app/app.routes.ts`, with a title of the form `<Seite> – JassCardEye` as a message with
   its own id: `` $localize`:@@title.<name>:<Seite> – JassCardEye` ``.
3. A link: the header navigation is `navLinks` in `src/app/app.ts`, and the footer names the same pages
   and Lizenzen (`footerLinks`), each label a message with its own id.
4. `npx ng extract-i18n --format json --output-path src/locale`, then the new messages in every
   `src/locale/messages.<language>.json`.

Nothing else - the prerender picks up every route by itself, and the build gives the new page its redirect
page at the root and its `hreflang` links. The catch-all route sends an unknown path to the start page.

## Deploy

`.github/workflows/web.yml` builds every pull request that touches `src/web` and publishes every push to
`main` that does, so merging is publishing. It installs with `npm ci` on Node 22, runs `npm run build` and
uploads `dist/jasscardeye-web/browser` with `Azure/static-web-apps-deploy`, authenticated by the repository
secret `AZURE_STATIC_WEB_APPS_API_TOKEN_WEB`, the deployment token of the static web app.

The workflow can also be started by hand. It publishes only on `main`; started on another branch it just
builds, so no branch can go live. The deploy action is pinned to a commit, because it holds the
deployment token.

The site is hosted as the Azure Static Web App `jasscardeye-web` on the Free plan, region West Europe,
in the resource group `jasscardeye`. Azure's own address for it,
https://lively-cliff-06d482903.6.azurestaticapps.net, helps when the domain itself is in doubt. The
domain jasscardeye.yarx.ch is a CNAME record at the host of the yarx.ch domain, bound to the static web
app as a custom domain; Azure issues and renews the certificate.

## Azure configuration

`staticwebapp.config.json` is copied to the root of the output by `scripts/site-root.mjs` - Azure reads it
only there, and `public/` lands in every language folder instead. It says:

- `trailingSlash: "never"` - `/de/anleitung/` redirects to `/de/anleitung`, so every page has one address.
- `navigationFallback` - a path that matches no file is answered with `/index.html`, the redirect page of
  the root, with status 200, which sends the visitor to the start page in their language. There is no 404
  page. The screenshots, images, scripts, stylesheets and the other listed file types are excluded, so a
  missing asset still fails with a 404.

The local emulator of the Static Web Apps CLI adds a slash to a folder instead of honouring
`trailingSlash`, so a check with it is a check of the redirects, not of the addresses.
- Two headers on every response: `X-Content-Type-Options: nosniff` and
  `Referrer-Policy: strict-origin-when-cross-origin`.

## Languages

The site is built with Angular's own localisation, `@angular/localize`: `i18n` in `angular.json` names the
languages, the source locale German first, and the production build (`localize: true`) prerenders a complete
site per language into its own folder, each with its `<base href>` and `lang`. The development server builds
one language at a time, German unless a language is named.

- **The pages are prose, written per language** as a whole page, in `src/app/pages/<language>/`. The routes
  load the pages of the language being built as one chunk, so a visitor downloads one language only. The
  addresses are the same in every language (`/fr/datenschutz`), so a page and its translations differ in the
  language path alone.
- **Navigation, titles, the footer, the menu and the page description are messages**, marked with a stable
  id like the keys of the apps: `i18n="@@nav.menu"` in a template, `` $localize`:@@nav.guide:Anleitung` `` in
  code. `ng extract-i18n --format json --output-path src/locale` writes the German ones to
  `src/locale/messages.json`, and `messages.<language>.json` translates them. A message missing in a
  language stops the build (`i18nMissingTranslation: "error"`), and so does one left over.
- **The switcher** in the header links every page to the same page in the other languages, from
  `src/app/languages.ts`. It is a plain link, since every language is a build of its own.
- **`scripts/site-root.mjs`** runs after `ng build` and writes what lies above the languages: a redirect page
  at the root and at every page's address without a language (`/datenschutz`), which picks the first of the
  browser's languages the site has - Swiss German counting as German - and German when it has none; with
  JavaScript off it refreshes to the German page. It adds `hreflang` links for every language and the
  `x-default` address to every page, and copies `staticwebapp.config.json`. Languages and pages come from
  `angular.json` and the German build; a language missing a page, a switcher that does not offer exactly
  the languages built, and a translation with a message the site no longer has all stop the build.
- **A new language** needs its locale in `angular.json` and `src/app/languages.ts`, its pages, its
  `messages.<language>.json`, its privacy policy as `privacy.<language>.md`, and its folder in
  `pagesByLanguage` in `src/app/app.routes.ts`. The app texts are separate, in `l10n/`.
- **The screenshots** are the German ones in every language until the stores have screenshots per language.

## Writing the pages

- **German, Swiss spelling:** ss, never ß - like the apps and the store entries.
- **Informal and plain:** du, tu, tu, you - «Schreib uns», «Écris-nous», «Scrivici», “Write to us” - and
  short sentences.
- **The same page in every language.** A change to one language's page is made in all four in the same
  change; the translations follow the German page, which is the source.
- **Labels from the apps** are written exactly as they appear in both apps in that language
  (`l10n/<language>.json`). In running text they stand in « » (in English “ ”), like «Kauf
  wiederherstellen»; in the step lists of the Anleitung they are set in bold. Only a real
  label gets that treatment - a question such as "Falsch erkannt?", which is no button, stays plain text.
- **`&#64;` instead of `@`** in templates: Angular reads `@` as the start of a control-flow block such as
  `@for`, so an email address has to be written with the entity.
- **Tailwind utilities with the theme tokens only:** `ground`, `panel`, `line`, `jass`, `jass-dark` and
  `muted` in `src/styles.css`. `ground` is the dark background of the apps and `jass` the green they use for
  everything to tap, so the site looks like the apps.
- **Nothing from outside:** no web fonts, no external scripts, images or embeds, no cookies, no analytics.
  The Datenschutz page promises exactly that under «Diese Webseite», so adding any of it means changing that
  text first. Links to other sites are fine.
- **Both apps together:** the site describes what both apps do. A feature that only one of them has does not
  belong here until the other one has it too.
- **The developer tools of the apps never appear** on the site, as in every other text a player or a
  reviewer reads.
- **Legal texts** change only together with their sources (below), in every language. The «Stand» date on
  the Datenschutz page moves with every change of its content.

## Assets

- `public/icon-512.png` is the Play Store icon, the same file as `src/app/android/store/icon-512.png`, drawn
  by `src/tools/make_android_icon.py` from the app icon. It serves as favicon, touch icon and logo.
- `public/screens/start.jpg`, `neue-zaehlung.jpg` and `zaehlen.jpg` are the first three iPhone store
  screenshots (`src/app/ios/store/screenshots/6.9/`), scaled to 644 × 1400 and saved as JPEG. Their
  width and height are written into each `home.html`, which keeps the page from jumping while they load; new
  screenshots need the same size or new numbers there.

## Facts and where they come from

The pages repeat facts that live in the code of the apps. When one of these sources changes, the page has
to follow.

| Page | Fact | Source of truth |
|---|---|---|
| Start | features: both decks, disciplines, last trick, factor ×1 to ×8, corrections, recognition on the phone | `JassScoring` and `JassDeck` in both apps; the descriptions in the store listings |
| Start, Support | free demo with blurred points; one purchase of CHF 5; family sharing on the iPhone only | `Store` and `ScoreRow` in both apps; `src/app/ios/JassCardEye.storekit`; the price itself is set in the store consoles |
| Start | «Bald im App Store und bei Google Play» | the release: replace with links to both stores once version 1.0.0 is published |
| Anleitung, Support | steps and labels: «Zählen starten», «Fertig», «Karte fehlt?», «Reset», «Rückmeldung», «Kauf wiederherstellen», «Über» | `l10n/<language>.json`, which both apps are built from (`count.start`, `common.done`, `scan.missing_card`, `scan.reset`, `settings.feedback`, `purchase.restore`, `about.title`); where they appear: `HomeView`/`HomeScreen`, `StartSheet`, `ScanView`/`ScanScreen`, `AboutView`/`AboutScreen` |
| Anleitung | scan rules: tilt up to 60°, the whole card in the square, no card in motion, light | `context/architecture/data-pipeline.md` - the rules describe the training data, so new training data can change them |
| Anleitung | the two decks | the scans in `data/cards/`; maker and names of the decks under *Blatt* in `context/glossary.md`; the suit names of each language in `l10n/<language>.json` |
| Support | devices: iPhones from iOS 17, Android phones from Android 10 | `src/app/ios/project.yml` (deployment target, device family), `src/app/android/app/build.gradle.kts` (minSdk) |
| Support | what goes online: nothing from the app's own code; the purchase asks the store at start, when buying and when restoring | `Store` in both apps; `AndroidManifest.xml` and "What ships" in `src/app/android/README.md` (`INTERNET` comes with Play Billing, the app's own code does not use it) |
| Datenschutz | what is stored, permissions, purchase, backup | `src/web/privacy.md` and `privacy.<language>.md`, which have to say the same; the settings keys in `LiveDetectionModel` of both apps; `AndroidManifest.xml`; `src/app/ios/PrivacyInfo.xcprivacy`; `Store`; the privacy paragraph of the Über page, `about.privacy_text` in `l10n/<language>.json` |
| Impressum | publisher, address, UID, contact | YARX GmbH - the publisher on the Über page of both apps and in `LICENSE` |
| Lizenzen | licence, source code link, recognition model, suit mark credits | `LICENSE`; the `about.*` texts in `l10n/<language>.json` and the links in `About` in `AboutView.swift` and `AboutScreen.kt`; the suit table `JassSuit.all` in `JassDeck` |
| Lizenzen | libraries | Android: the dependencies in `src/app/android/app/build.gradle.kts`; website: `3rdpartylicenses.txt` after a build |

The source code link is `https://github.com/yarx/JassCardEye`, the same on the Lizenzen page and in both
apps (`About.source` in `AboutView.swift`, `About.SOURCE` in `AboutScreen.kt`).

## Before merging

- `npm run build` passes - it is what the workflow runs, and a page that fails to prerender, a missing
  message and a language without all its pages fail there.
- `src/locale/messages.json` is what `ng extract-i18n` writes; the workflow checks that too.
- Every changed page has been changed in all four languages and looked at in a phone-sized window.
- A changed fact has been checked against its source in the table above, in both apps and every language.
