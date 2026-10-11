import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { LedgerApi } from '../../core/api/ledger-api';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/notifications/toast.service';
import { InstallPrompt } from '../../core/pwa/install-prompt';
import { Button, Card, DateTimeInput, FormField, Icon, PageHeader, Spinner } from '../../ui';

/**
 * Settings: this device (install the app, remember the login), security (passkeys) and the books lock date
 * (nothing on or before it can be recorded, edited, deleted or restored).
 */
@Component({
  selector: 'app-settings-page',
  imports: [DatePipe, FormsModule, PageHeader, Card, Button, Icon, FormField, DateTimeInput, Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ui-page-header i18n-heading="@@settings.title" heading="Settings" />

    <div class="mb-6 grid gap-6 lg:grid-cols-2">
      <ui-card i18n-heading="@@settings.deviceTitle" heading="This device">
        <div class="grid gap-5 text-sm">
          <section class="grid gap-2">
            <h3 class="font-semibold" i18n="@@settings.installTitle">Install the app</h3>
            @switch (install.state()) {
              @case ('installed') {
                <p class="flex items-center gap-2 text-success"><ui-icon name="check" size="sm" /><ng-container i18n="@@settings.installed">Ledger is installed on this device.</ng-container></p>
              }
              @case ('available') {
                <p class="text-muted" i18n="@@settings.installHint">Use Ledger like an app: its own icon and window, and it opens even without a connection.</p>
                <div><button uiButton type="button" (click)="installApp()"><ui-icon name="download" size="sm" /><ng-container i18n="@@settings.install">Install app</ng-container></button></div>
              }
              @case ('ios') {
                <p class="text-muted" i18n="@@settings.installIos">On iPhone or iPad: tap Share, then “Add to Home Screen”.</p>
                <p class="flex items-center gap-2 text-muted"><ui-icon name="share" size="sm" /><ng-container i18n="@@settings.installIosHint">Open Ledger in Safari to do it.</ng-container></p>
              }
              @default {
                <p class="text-muted" i18n="@@settings.installUnavailable">Open Ledger in Chrome, Edge or Safari (on iPhone) to install it. If you already installed it, open it from its icon.</p>
              }
            }
          </section>

          <section class="grid gap-2">
            <label class="flex cursor-pointer items-start gap-3">
              <input type="checkbox" class="mt-1 accent-primary" [checked]="auth.remember()" (change)="toggleRemember($any($event.target).checked)" />
              <span>
                <span class="font-semibold" i18n="@@settings.remember">Remember me on this device</span>
                <span class="block text-muted" i18n="@@settings.rememberHint">You stay logged in after closing the app (until you log out). Don’t turn it on in shared devices.</span>
              </span>
            </label>
          </section>
        </div>
      </ui-card>

      <ui-card i18n-heading="@@settings.securityTitle" heading="Security">
        <div class="grid gap-3 text-sm">
          <h3 class="font-semibold" i18n="@@settings.passkeyTitle">Passkey (Face ID, fingerprint)</h3>
          <p class="text-muted" i18n="@@settings.passkeyHint">Optional: log in with Face ID, your fingerprint or your device’s lock instead of typing the password. Your password keeps working.</p>
          <div class="flex flex-wrap gap-2">
            <button uiButton type="button" (click)="auth.registerPasskey()"><ui-icon name="key" size="sm" /><ng-container i18n="@@settings.passkeySetUp">Set up a passkey</ng-container></button>
            <a uiButton variant="secondary" [href]="auth.accountUrl()" target="_blank" rel="noopener">
              <ui-icon name="external" size="sm" /><ng-container i18n="@@settings.passkeyManage">Manage passkeys and password</ng-container>
            </a>
          </div>
        </div>
      </ui-card>
    </div>

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
  protected readonly auth = inject(AuthService);
  protected readonly install = inject(InstallPrompt);

  protected readonly saving = signal(false);
  protected readonly date = signal<string>('');

  protected readonly lock = rxResource({ stream: () => this.api.booksLock() });

  constructor() {
    if (this.auth.rememberRefused()) {
      this.toasts.error($localize`:@@settings.rememberRefused:Your account can’t stay logged in on this device (ask the administrator for offline access). “Remember me” was turned off.`);
    }

    // Coming back from Keycloak after setting up a passkey.
    effect(() => {
      const result = this.auth.actionResult();
      if (!result) return;

      if (result === 'success') this.toasts.success($localize`:@@settings.passkeyDone:Passkey ready. Next time, choose “Sign in with a passkey”.`);
      else if (result === 'error') this.toasts.error($localize`:@@settings.passkeyFailed:The passkey could not be set up. Try again.`);
      this.auth.actionResult.set(null);
    });

    firstValueFrom(this.api.booksLock())
      .then((current) => this.date.set(current.lockedUntil ?? ''))
      .catch(() => undefined);
  }

  protected async installApp(): Promise<void> {
    if (await this.install.install()) this.toasts.success($localize`:@@settings.installedNow:Ledger installed.`);
  }

  protected toggleRemember(remember: boolean): void {
    this.auth.setRemember(remember);
    if (!remember) this.toasts.success($localize`:@@settings.forgotten:This device no longer keeps you logged in after closing the app.`);
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
