import { Injectable, computed, signal } from '@angular/core';
import { Locale, TranslationKey, translations } from './translations';

/** Where the chosen language is remembered, so it survives a reload. */
const STORAGE_KEY = 'mrb-locale';

/** A plural key's grammatical form. English and Ukrainian disagree on how many there are. */
type PluralForm = 'one' | 'few' | 'many' | 'other';

/**
 * Chooses the language every screen is shown in, and translates individual strings into it.
 *
 * Runtime rather than Angular's built-in `@angular/localize`: that mechanism picks a language at
 * *build* time, one bundle per locale, which is right for a site that always serves the same reader
 * the same language. Here the requirement is a switch in the topbar that takes effect immediately,
 * so translation has to be a lookup a component can call on every render rather than a compiled-in
 * choice — hence a plain dictionary (`./translations`) plus this service, rather than a second build
 * target.
 */
@Injectable({ providedIn: 'root' })
export class TranslationService {
  /** The language currently shown. */
  readonly locale = signal<Locale>(restoreLocale());

  /** The BCP 47 tag to format dates and numbers with, so "Sunday" becomes "неділя" too. */
  readonly dateLocale = computed(() => (this.locale() === 'uk' ? 'uk-UA' : 'en-GB'));

  constructor() {
    // Applied once up front for the locale restored above -- see setLocale() for why this is a
    // plain call rather than an effect().
    applySideEffects(this.locale());
  }

  /** Switches the application's language. */
  setLocale(locale: Locale): void {
    this.locale.set(locale);

    // A direct call rather than an `effect()` reacting to the signal: an effect only flushes on a
    // change-detection tick, which a service under test -- constructed with no component driving
    // one -- never gets. The two things this keeps in sync (the attribute a screen reader and
    // `:lang()` selectors read, and the storage a fresh visit restores from) both need to be correct
    // the instant the language changes, not merely by the next render.
    applySideEffects(locale);
  }

  /**
   * Looks up `key` in the current language, interpolating `{{name}}` placeholders from `params`.
   *
   * Falls back to English, then to the key itself, so a translation that is missing or not yet
   * written shows *something* legible rather than throwing mid-render.
   */
  t(key: TranslationKey, params?: Record<string, string | number>): string {
    const template = translations[this.locale()][key] ?? translations.en[key] ?? key;

    return interpolate(template, params);
  }

  /**
   * Like `t()`, but for text that names a count and must agree with it grammatically.
   *
   * `base` is a key prefix with `.one` / `.few` / `.many` / `.other` variants already in the
   * dictionary (for example `myBookings.slotsPhrase`); this picks the form `count` calls for in the
   * current language and interpolates it with `count` already in scope, alongside anything in
   * `params`.
   */
  plural(base: string, count: number, params?: Record<string, string | number>): string {
    const key = `${base}.${this.pluralForm(count)}` as TranslationKey;

    return this.t(key, { count, ...params });
  }

  /**
   * Which of a plural key's variants `count` selects.
   *
   * English has two forms. Ukrainian has three, chosen by the last one or two digits rather than by
   * the value itself — 1, 21, 31… take "one" but 11 does not; 2–4, 22–24… take "few" but 12–14 do
   * not — which is also why a naive `n === 1 ? singular : plural` cannot be reused across the two.
   */
  private pluralForm(count: number): PluralForm {
    if (this.locale() === 'en') {
      return count === 1 ? 'one' : 'other';
    }

    const abs = Math.abs(count);
    const lastDigit = abs % 10;
    const lastTwoDigits = abs % 100;

    if (lastDigit === 1 && lastTwoDigits !== 11) {
      return 'one';
    }

    if (lastDigit >= 2 && lastDigit <= 4 && (lastTwoDigits < 12 || lastTwoDigits > 14)) {
      return 'few';
    }

    return 'many';
  }
}

/** Replaces every `{{name}}` in `template` with `params[name]`, or leaves it untouched if unmatched. */
function interpolate(template: string, params?: Record<string, string | number>): string {
  if (!params) {
    return template;
  }

  return template.replace(/\{\{(\w+)\}\}/g, (placeholder, name: string) =>
    name in params ? String(params[name]) : placeholder,
  );
}

/** Keeps the document's `lang` attribute and storage in step with a locale change. */
function applySideEffects(locale: Locale): void {
  document.documentElement.lang = locale;

  try {
    localStorage.setItem(STORAGE_KEY, locale);
  } catch {
    // Private browsing, or storage disabled outright. The choice just does not outlive the tab.
  }
}

/** Restores the language chosen on a previous visit, defaulting to English on a first one. */
function restoreLocale(): Locale {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (stored === 'en' || stored === 'uk') {
      return stored;
    }
  } catch {
    // Storage unavailable; fall through to the default below.
  }

  return 'en';
}
