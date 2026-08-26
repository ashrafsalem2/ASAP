import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';

@Component({
  selector: 'asap-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="page">
      <div>
        <h1>{{ t('home.welcome') }}, {{ auth.user()?.displayName }}</h1>
        <p class="page__intro">{{ t('home.openMenu') }}</p>
      </div>

      <section class="panel">
        <div class="panel__body summary">
          <div class="summary__item">
            <span class="summary__label">{{ t('home.youAreIn') }}</span>
            <span class="summary__value">{{ companyName() }}</span>
          </div>

          <div class="summary__item">
            <span class="summary__label">{{ t('shell.branch') }}</span>
            <span class="summary__value">
              {{ auth.user()?.branchName ?? t('shell.headOffice') }}
            </span>
          </div>

          <div class="summary__item">
            <span class="summary__label">{{ t('home.permissions') }}</span>
            <span class="summary__value summary__value--figure">
              {{ auth.user()?.permissions?.length ?? 0 }}
            </span>
          </div>
        </div>
      </section>
    </div>
  `,
  styles: `
    .summary {
      display: flex;
      flex-wrap: wrap;
      gap: 2.5rem;
    }

    .summary__item {
      display: flex;
      flex-direction: column;
      gap: 0.125rem;
    }

    .summary__label {
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--ink-faint);
    }

    .summary__value {
      font-size: 1.0625rem;
      font-weight: 600;
    }

    .summary__value--figure {
      font-variant-numeric: tabular-nums;
      direction: ltr;
    }
  `,
})
export class Home {
  protected readonly auth = inject(AuthService);
  private readonly i18n = inject(I18nService);

  /** The company name in the reader's language, falling back when no Arabic name is set. */
  protected readonly companyName = computed(() => {
    const company = this.auth.activeCompany();

    if (!company) {
      return '';
    }

    return this.i18n.language() === 'ar' && company.nameArabic ? company.nameArabic : company.name;
  });

  protected t(key: TranslationKey): string {
    return this.i18n.translate(key);
  }
}
