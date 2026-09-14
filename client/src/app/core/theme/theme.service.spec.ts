import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ThemeService } from './theme.service';

/**
 * The light/dark/auto switch.
 *
 * What matters here is the `data-theme` attribute `styles.scss` reads: `auto` must leave it absent
 * so `prefers-color-scheme` alone decides, while `light` and `dark` must stamp it so an explicit
 * choice overrides the system either way.
 */
describe('ThemeService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-theme');
  });

  it('defaults to auto, with no attribute overriding the system', () => {
    const service = TestBed.inject(ThemeService);

    expect(service.preference()).toBe('auto');
    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('stamps the attribute when an explicit theme is chosen', () => {
    const service = TestBed.inject(ThemeService);

    service.setPreference('dark');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');

    service.setPreference('light');
    expect(document.documentElement.getAttribute('data-theme')).toBe('light');
  });

  it('removes the attribute when switched back to auto', () => {
    const service = TestBed.inject(ThemeService);

    service.setPreference('dark');
    service.setPreference('auto');

    expect(document.documentElement.hasAttribute('data-theme')).toBe(false);
  });

  it('remembers the choice across a reload', () => {
    const first = TestBed.inject(ThemeService);
    first.setPreference('dark');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const second = TestBed.inject(ThemeService);

    expect(second.preference()).toBe('dark');
  });
});
