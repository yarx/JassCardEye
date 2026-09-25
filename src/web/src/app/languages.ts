/**
 * The languages the site is built in, in the order the switcher shows them: the locales of `i18n` in angular.json,
 * source first. scripts/site-root.mjs stops the build when a page's switcher and angular.json disagree.
 */
export const languages = [
  { code: 'de', name: 'Deutsch' },
  { code: 'fr', name: 'Français' },
  { code: 'it', name: 'Italiano' },
  { code: 'en', name: 'English' },
] as const;

/** The language of this build; a development build without a language is German. */
export const currentLanguage: string = $localize.locale ?? 'de';
