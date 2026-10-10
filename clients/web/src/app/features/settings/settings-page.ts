import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { ToastService } from '../../core/notifications/toast.service';
import { Button, Card, DateTimeInput, FormField, Icon, PageHeader, Spinner } from '../../ui';

/** The books lock date: nothing on or before it can be recorded, edited, deleted or restored. */
@Component({
  selector: 'app-settings-page',
  imports: [DatePipe, FormsModule, PageHeader, Card, Button, Icon, FormField, DateTimeInput, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@settings.title" heading="Settings" />

    <ui-card i18n-heading="@@settings.lockTitle" heading="Close my books">
      @if (lock.isLoading() && !lock.value()) {
        <div class="flex justify-center py-6"><ui-spinner /></div>
      } @else {
        <div class="grid max-w-xl gap-4">
          <p class="text-sm text-muted" i18n="@@settings.lockExplanation">
            Once a period is reviewed (e.g. a closed month), lock it: nothing dated on or before the lock date can be
            recorded, edited, deleted or restored, and shared operations that would touch it are refused. You can move
            the date back at any time.
          </p>

          <p class="flex items-center gap-2 text-sm">
            <ui-icon name="lock" size="sm" />
            @if (lock.value()?.lockedUntil; as until) {
              <span i18n="@@settings.lockedUntil">Closed up to {{ until | date: 'medium' }}</span>
            } @else {
              <span i18n="@@settings.notLocked">Nothing is locked.</span>
            }
          </p>

          <ui-form-field i18n-label="@@settings.lockDate" label="Closed up to">
            <ui-datetime-input [ngModel]="date()" (ngModelChange)="date.set($event)" />
          </ui-form-field>

          <div class="flex flex-wrap gap-2">
            <button uiButton type="button" [disabled]="!date()" [loading]="saving()" (click)="save(date())" i18n="@@settings.lock">Lock</button>
            @if (lock.value()?.lockedUntil) {
              <button uiButton variant="secondary" type="button" [loading]="saving()" (click)="save(null)" i18n="@@settings.unlock">Unlock everything</button>
            }
          </div>
        </div>
      }
    </ui-card>
  `,
})
export class SettingsPage {
  private readonly api = inject(LedgerApi);
  private readonly toasts = inject(ToastService);

  protected readonly saving = signal(false);
  protected readonly date = signal<string>('');

  protected readonly lock = rxResource({ stream: () => this.api.booksLock() });

  constructor() {
    firstValueFrom(this.api.booksLock())
      .then((current) => this.date.set(current.lockedUntil ?? ''))
      .catch(() => undefined);
  }

  protected async save(lockedUntil: string | null): Promise<void> {
    this.saving.set(true);

    try {
      await firstValueFrom(this.api.setBooksLock(lockedUntil || null));
      this.toasts.success(
        lockedUntil
          ? $localize`:@@settings.locked:Books closed.`
          : $localize`:@@settings.unlocked:Everything is open again.`,
      );
      this.lock.reload();
      if (!lockedUntil) this.date.set('');
    } finally {
      this.saving.set(false);
    }
  }
}
