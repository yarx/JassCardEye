// Finishes the localised build for Azure: what lives at the root of the site, above the languages.
//
//   node scripts/site-root.mjs        (run by `npm run build`, after `ng build`)
//
// `ng build` writes one complete site per language, `browser/de/`, `browser/fr/`, ... Everything the
// site needs above them is written here, from what the build produced:
//
// - A redirect page for the root and for every page, `browser/<page>/index.html`. It sends the visitor
//   to that page in the first of their browser's languages the site has, German when it has none of
//   them. The addresses without a language - above all `/datenschutz` and `/support`, which the store
//   entries and both apps link to - therefore keep working. Without JavaScript the page falls back to
//   the German version with a meta refresh.
// - `hreflang` links in every page of every language, naming its siblings, so a search engine shows
//   each visitor their language. The address without a language is the `x-default`.
// - `staticwebapp.config.json`, which Azure reads only at the root.
//
// The languages come from angular.json (source locale first, as the fallback) and the pages from the
// folders of the German build, so a new language or a new page needs nothing here. A language that
// lacks a page of the German build stops the build.

import { copyFileSync, existsSync, mkdirSync, readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const web = join(dirname(fileURLToPath(import.meta.url)), '..');
const browser = join(web, 'dist/jasscardeye-web/browser');
const origin = 'https://jasscardeye.yarx.ch';

// Swiss German is not German to a browser, but it is to this site.
const aliases = { gsw: 'de' };

function fail(message) {
  console.error(`site-root: ${message}`);
  process.exit(1);
}

const i18n = JSON.parse(readFileSync(join(web, 'angular.json'), 'utf8')).projects['jasscardeye-web'].i18n;
// A locale's folder is its subPath, or its code when it has none.
const folderOf = (code, locale) => (typeof locale === 'object' && locale.subPath) || code;
const source = i18n.sourceLocale;
const languages = [
  typeof source === 'string' ? source : folderOf(source.code, source),
  ...Object.entries(i18n.locales ?? {}).map(([code, locale]) => folderOf(code, locale)),
];

/** The pages of one language, "" for its start page, as the folders the prerender wrote. */
function pagesOf(language) {
  const folder = join(browser, language);
  if (!existsSync(join(folder, 'index.html'))) fail(`no build for ${language} in ${folder}`);
  const pages = readdirSync(folder, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && existsSync(join(folder, entry.name, 'index.html')))
    .map((entry) => entry.name);
  return ['', ...pages.sort()];
}

const pages = pagesOf(languages[0]);
for (const language of languages.slice(1)) {
  const missing = pages.filter((page) => !pagesOf(language).includes(page));
  if (missing.length) fail(`${language} lacks ${missing.map((page) => `/${page}`).join(', ')}`);
}

const path = (language, page) => '/' + [language, page].filter(Boolean).join('/');
const escape = (text) => text.replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');

function alternates(page) {
  return [
    ...languages.map((language) => `<link rel="alternate" hreflang="${language}" href="${origin}${path(language, page)}" />`),
    `<link rel="alternate" hreflang="x-default" href="${origin}${path('', page)}" />`,
  ].join('\n    ');
}

function redirectPage(page) {
  const fallback = path(languages[0], page);
  return `<!doctype html>
<html lang="${languages[0]}">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>JassCardEye</title>
    ${alternates(page)}
    <script>
      (function () {
        var languages = ${JSON.stringify(languages)};
        var aliases = ${JSON.stringify(aliases)};
        var wanted = (navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || ''])
          .map(function (tag) { var code = tag.split('-')[0].toLowerCase(); return aliases[code] || code; })
          .filter(function (code) { return languages.indexOf(code) >= 0; })[0] || languages[0];
        location.replace('/' + wanted + ${JSON.stringify(page ? '/' + page : '')} + location.search + location.hash);
      })();
    </script>
    <noscript><meta http-equiv="refresh" content="0; url=${escape(fallback)}" /></noscript>
  </head>
  <body style="background: #121212; color: #e4e4e7; font-family: system-ui, sans-serif">
    <p><a href="${escape(fallback)}" style="color: #30d158">JassCardEye</a></p>
  </body>
</html>
`;
}

for (const page of pages) {
  const folder = join(browser, page);
  mkdirSync(folder, { recursive: true });
  writeFileSync(join(folder, 'index.html'), redirectPage(page));

  for (const language of languages) {
    const file = join(browser, language, page, 'index.html');
    const html = readFileSync(file, 'utf8');
    if (html.includes('hreflang=')) fail(`${file} already carries hreflang links`);
    writeFileSync(file, html.replace('</head>', `  ${alternates(page)}\n  </head>`));
  }
}

copyFileSync(join(web, 'staticwebapp.config.json'), join(browser, 'staticwebapp.config.json'));
console.log(`site-root: ${languages.join(', ')}; ${pages.length} pages with a redirect at the root`);
