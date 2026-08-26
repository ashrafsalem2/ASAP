import { Injectable, computed, effect, signal } from '@angular/core';
import { AsapLanguage, TRANSLATIONS, TranslationKey } from './translations';

const STORAGE_KEY = 'asap.language';

/**
 * Holds the chosen language and keeps the document in step with it.
 *
 * Switching to Arabic changes the writing direction of the whole page, not merely the words. That
 * is why the direction is applied to the document element rather than handled per component: an
 * ERP screen is mostly tables and forms, and a right-to-left layout has to flip the lot at once or
 * it reads as a mistake.
 */
@Injectable({ providedIn: 'root' })
export class I18nService {
  private readonly current = signal<AsapLanguage>(this.readStoredLanguage());

  /** The active language. */
  readonly language = this.current.asReadonly();

  /** True when the active language is written right to left. */
  readonly isRightToLeft = computed(() => this.current() === 'ar');

  /**
   * The locale used for formatting numbers and dates.
   *
   * Arabic uses `ar-SA-u-nu-latn`: Arabic conventions, Latin digits. Accountants in the Gulf read
   * figures in Latin digits even when the surrounding text is Arabic, and an invoice total in
   * Arabic-Indic numerals is one nobody can check at a glance against a bank statement.
   */
  readonly locale = computed(() => (this.current() === 'ar' ? 'ar-SA-u-nu-latn' : 'en-GB'));

  constructor() {
    effect(() => {
      const language = this.current();
      const element = document.documentElement;

      element.lang = language;
      element.dir = language === 'ar' ? 'rtl' : 'ltr';

      localStorage.setItem(STORAGE_KEY, language);
    });
  }

  /** Translates a shell string. */
  translate(key: TranslationKey, values?: Readonly<Record<string, string | number>>): string {
    const text = TRANSLATIONS[this.current()][key] ?? TRANSLATIONS.en[key] ?? key;

    if (!values) {
      return text;
    }

    // Same {Placeholder} shape the server catalogue uses, so a string can move between the two
    // without being rewritten.
    return text.replace(/\{(\w+)\}/g, (whole, name) =>
      name in values ? String(values[name]) : whole,
    );
  }

  /** Switches between the two languages. */
  toggle(): void {
    this.current.update((language) => (language === 'en' ? 'ar' : 'en'));
  }

  /** Sets the language, used when a signed-in user has a stored preference. */
  set(language: AsapLanguage): void {
    this.current.set(language);
  }

  /** Formats a money amount for the active locale. */
  money(value: number | null | undefined, currency = 'SAR'): string {
    if (value === null || value === undefined) {
      return '';
    }

    return new Intl.NumberFormat(this.locale(), {
      style: 'currency',
      currency,
      currencyDisplay: 'code',
    }).format(value);
  }

  /**
   * Formats a plain number with two decimals, for table columns that repeat the currency in a
   * header. Zero comes back blank, which keeps a wide grid readable.
   */
  amount(value: number | null | undefined): string {
    if (value === null || value === undefined || value === 0) {
      return '';
    }

    return this.decimals(value);
  }

  /**
   * Formats a total, showing zero as a figure rather than a blank.
   *
   * A detail row at zero is noise and is better left empty. A total at zero is a statement that
   * the block came to nothing, and blanking it reads as "not calculated" instead -- which on a
   * balance sheet is the difference between nil and a report the reader stops trusting.
   */
  total(value: number | null | undefined): string {
    return this.decimals(value ?? 0);
  }

  private decimals(value: number): string {
    return new Intl.NumberFormat(this.locale(), {
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
  }

  private readStoredLanguage(): AsapLanguage {
    const stored = localStorage.getItem(STORAGE_KEY);

    return stored === 'ar' || stored === 'en' ? stored : 'en';
  }
}
