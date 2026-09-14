import { Injectable, signal } from '@angular/core';

/** A user's colour scheme choice. `auto` defers to the operating system. */
export type ThemePreference = 'auto' | 'light' | 'dark';

/** Where the choice is remembered, so it survives a reload. */
const STORAGE_KEY = 'mrb-theme';

/**
 * Chooses light, dark, or the operating system's own preference, and remembers the choice.
 *
 * `styles.scss` already varies every colour by `prefers-color-scheme`, which is the right default
 * and is what `auto` leaves in charge. `light` and `dark` override it by stamping `data-theme` on
 * `<html>`; the stylesheet's dark block is guarded with `:not([data-theme="light"])` so an explicit
 * light choice wins even on a dark system, and a `[data-theme="dark"]` block applies the same
 * variables unconditionally so an explicit dark choice wins on a light one.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  /** The user's current choice. */
  readonly preference = signal<ThemePreference>(restorePreference());

  constructor() {
    // Applied once up front for the preference restored above -- see setPreference() for why this
    // is a plain call rather than an effect().
    applySideEffects(this.preference());
  }

  /** Sets the colour scheme preference. */
  setPreference(preference: ThemePreference): void {
    this.preference.set(preference);

    // A direct call rather than an `effect()` reacting to the signal: an effect only flushes on a
    // change-detection tick, which a service under test -- constructed with no component driving
    // one -- never gets, and the `data-theme` attribute has to be correct the instant the choice
    // changes so the very next paint uses it, not merely the next render.
    applySideEffects(preference);
  }
}

/** Stamps or clears `data-theme` on `<html>`, and remembers the choice for next time. */
function applySideEffects(preference: ThemePreference): void {
  const root = document.documentElement;

  if (preference === 'auto') {
    root.removeAttribute('data-theme');
  } else {
    root.setAttribute('data-theme', preference);
  }

  try {
    localStorage.setItem(STORAGE_KEY, preference);
  } catch {
    // Private browsing, or storage disabled outright. The choice just does not outlive the tab.
  }
}

/** Restores a previous visit's choice, defaulting to following the operating system. */
function restorePreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);

    if (stored === 'auto' || stored === 'light' || stored === 'dark') {
      return stored;
    }
  } catch {
    // Storage unavailable; fall through to the default below.
  }

  return 'auto';
}
