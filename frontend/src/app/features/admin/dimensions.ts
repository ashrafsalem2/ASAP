import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DimensionRow, DimensionValueRow } from '../../core/api/asap-api.models';
import { DimensionService } from '../../core/api/dimension.service';
import { AuthService } from '../../core/auth/auth.service';
import { I18nService } from '../../core/i18n/i18n.service';
import { TranslationKey } from '../../core/i18n/translations';
import { MessageService } from '../../core/messages/message.service';

/**
 * The axes a company analyses its figures along, and the values each may take.
 *
 * The values matter more than they look. An axis anybody can type a new value into stops being an
 * axis and becomes a comment field, so a document naming anything not on this list is refused —
 * which makes this screen the only place a new department comes from.
 */
@Component({
  selector: 'asap-dimensions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  templateUrl: './dimensions.html',
  styleUrl: './admin.scss',
})
export class Dimensions implements OnInit {
  protected readonly i18n = inject(I18nService);
  private readonly api = inject(DimensionService);
  private readonly auth = inject(AuthService);
  private readonly messages = inject(MessageService);

  protected readonly dimensions = signal<DimensionRow[]>([]);
  protected readonly selected = signal<DimensionRow | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);

  protected readonly kinds = ['Standard', 'Heading', 'Total'] as const;

  ngOnInit(): Promise<void> {
    return this.reload();
  }

  protected t(key: TranslationKey, values?: Record<string, string | number>): string {
    return this.i18n.translate(key, values);
  }

  protected canWrite(): boolean {
    return this.auth.can('Platform.Dimension.Update');
  }

  protected name(row: { name: string; nameArabic: string | null }): string {
    return this.i18n.language() === 'ar' && row.nameArabic ? row.nameArabic : row.name;
  }

  protected select(dimension: DimensionRow): void {
    // A copy, so abandoning an edit leaves the list as it was.
    this.selected.set(structuredClone(dimension));
  }

  protected addValue(): void {
    const dimension = this.selected();

    if (!dimension) {
      return;
    }

    this.selected.set({
      ...dimension,
      values: [
        ...dimension.values,
        {
          code: '',
          name: '',
          nameArabic: null,
          kind: 'Standard',
          totalRange: null,
          indentation: 0,
          isBlocked: false,
        },
      ],
    });
  }

  protected removeValue(value: DimensionValueRow): void {
    const dimension = this.selected();

    if (!dimension) {
      return;
    }

    this.selected.set({ ...dimension, values: dimension.values.filter((v) => v !== value) });
  }

  protected async save(): Promise<void> {
    const dimension = this.selected();

    if (!dimension) {
      return;
    }

    this.busy.set(true);

    try {
      await this.api.save(dimension);
      this.messages.showSuccess(this.t('admin.dimensions.saved', { code: dimension.code }));

      await this.reload();
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.busy.set(false);
    }
  }

  private async reload(): Promise<void> {
    this.loading.set(true);

    try {
      const list = await this.api.list(true);

      this.dimensions.set(list);

      const current = this.selected();

      if (current) {
        const again = list.find((d) => d.code === current.code);

        this.selected.set(again ? structuredClone(again) : null);
      }
    } catch (error) {
      this.messages.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
}
