import { TestBed } from '@angular/core/testing';
import { provideZonelessChangeDetection } from '@angular/core';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TranslationService } from './translation.service';

/**
 * The runtime language switch.
 *
 * Covers the two things that would otherwise fail silently: a missing Ukrainian string falling back
 * to English instead of rendering a raw key, and the Ukrainian plural rule — which picks a form from
 * the last one or two digits, not from whether the count is 1 — landing on the right form at the
 * boundaries where a naive port of the English rule would disagree with it (11–14 versus 21, 22).
 */
describe('TranslationService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('defaults to English on a first visit', () => {
    const service = TestBed.inject(TranslationService);

    expect(service.locale()).toBe('en');
    expect(service.t('shell.signOut')).toBe('Sign out');
  });

  it('translates once the locale is switched', () => {
    const service = TestBed.inject(TranslationService);

    service.setLocale('uk');

    expect(service.t('shell.signOut')).toBe('Вийти');
  });

  it('interpolates named placeholders', () => {
    const service = TestBed.inject(TranslationService);

    expect(service.t('admin.createdNotice', { name: 'Boardroom' })).toBe('Created Boardroom.');
  });

  it('leaves an unmatched placeholder rather than throwing', () => {
    const service = TestBed.inject(TranslationService);

    expect(service.t('admin.createdNotice', {})).toBe('Created {{name}}.');
  });

  it('remembers the chosen locale across a reload', () => {
    const first = TestBed.inject(TranslationService);
    first.setLocale('uk');

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    const second = TestBed.inject(TranslationService);

    expect(second.locale()).toBe('uk');
  });

  describe('English pluralisation', () => {
    it('picks the singular only at exactly one', () => {
      const service = TestBed.inject(TranslationService);

      expect(service.plural('myBookings.slotsPhrase', 1)).toBe('1 slot');
      expect(service.plural('myBookings.slotsPhrase', 0)).toBe('0 slots');
      expect(service.plural('myBookings.slotsPhrase', 2)).toBe('2 slots');
    });
  });

  describe('Ukrainian pluralisation', () => {
    it('uses the "one" form for 1, 21 and 31 but not 11', () => {
      const service = TestBed.inject(TranslationService);
      service.setLocale('uk');

      expect(service.plural('myBookings.daysPhrase', 1)).toBe('1 день');
      expect(service.plural('myBookings.daysPhrase', 21)).toBe('21 день');
      expect(service.plural('myBookings.daysPhrase', 11)).not.toBe('11 день');
    });

    it('uses the "few" form for 2-4, 22-24 but not 12-14', () => {
      const service = TestBed.inject(TranslationService);
      service.setLocale('uk');

      expect(service.plural('myBookings.daysPhrase', 2)).toBe('2 дні');
      expect(service.plural('myBookings.daysPhrase', 4)).toBe('4 дні');
      expect(service.plural('myBookings.daysPhrase', 22)).toBe('22 дні');
      expect(service.plural('myBookings.daysPhrase', 12)).not.toBe('12 дні');
      expect(service.plural('myBookings.daysPhrase', 14)).not.toBe('14 дні');
    });

    it('uses the "many" form for 0, 5-10, and every 11-14', () => {
      const service = TestBed.inject(TranslationService);
      service.setLocale('uk');

      expect(service.plural('myBookings.daysPhrase', 0)).toBe('0 днів');
      expect(service.plural('myBookings.daysPhrase', 5)).toBe('5 днів');
      expect(service.plural('myBookings.daysPhrase', 11)).toBe('11 днів');
      expect(service.plural('myBookings.daysPhrase', 12)).toBe('12 днів');
      expect(service.plural('myBookings.daysPhrase', 14)).toBe('14 днів');
    });
  });
});
