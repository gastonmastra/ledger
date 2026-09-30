import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { NgTemplateOutlet } from '@angular/common';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { LedgerApi } from '../core/api/ledger-api';
import { AuthService } from '../core/auth/auth.service';
import { DataChanges } from '../core/data-changes';
import { LOCALES, LocaleService } from '../core/i18n/locale.service';
import { ThemeService } from '../core/theme/theme.service';
import { Badge, Icon, IconName, ToastContainer } from '../ui';

interface NavItem {
  path: string;
  label: string;
  icon: IconName;
}

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, NgTemplateOutlet, Icon, Badge, ToastContainer],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './shell.html',
})
export class Shell {
  private readonly api = inject(LedgerApi);
  private readonly changes = inject(DataChanges);
  protected readonly auth = inject(AuthService);
  protected readonly theme = inject(ThemeService);
  protected readonly locale = inject(LocaleService);
  protected readonly locales = LOCALES;
  protected readonly menuOpen = signal(false);

  protected readonly nav: NavItem[] = [
    { path: '/', label: $localize`:@@nav.dashboard:Dashboard`, icon: 'home' },
    { path: '/wallets', label: $localize`:@@nav.wallets:Wallets`, icon: 'wallet' },
    { path: '/transactions', label: $localize`:@@nav.transactions:Transactions`, icon: 'list' },
    { path: '/debts', label: $localize`:@@nav.debts:Debts & splits`, icon: 'scale' },
    { path: '/groups', label: $localize`:@@nav.groups:Groups`, icon: 'users' },
    { path: '/contacts', label: $localize`:@@nav.contacts:Contacts`, icon: 'user' },
    { path: '/categories', label: $localize`:@@nav.categories:Categories`, icon: 'tag' },
    { path: '/pending', label: $localize`:@@nav.pending:Pending`, icon: 'inbox' },
  ];

  /** Items shown in the mobile bottom bar; the rest are in the menu. */
  protected readonly bottomNav = this.nav.slice(0, 4);

  protected readonly pending = rxResource({
    params: () => this.changes.version(),
    stream: () => this.api.pendingAssignments(),
  });

  protected readonly pendingCount = computed(() => this.pending.value()?.length ?? 0);

  protected readonly themeIcon = computed<IconName>(() =>
    this.theme.mode() === 'system' ? 'computer' : this.theme.mode() === 'dark' ? 'moon' : 'sun',
  );

  protected readonly themeLabel = computed(() => {
    switch (this.theme.mode()) {
      case 'light':
        return $localize`:@@theme.light:Theme: light`;
      case 'dark':
        return $localize`:@@theme.dark:Theme: dark`;
      default:
        return $localize`:@@theme.system:Theme: system`;
    }
  });

  constructor() {
    inject(Router)
      .events.pipe(filter((e) => e instanceof NavigationEnd))
      .subscribe(() => this.menuOpen.set(false));
  }

  protected onLocaleChange(event: Event): void {
    this.locale.switchTo((event.target as HTMLSelectElement).value as 'en' | 'es');
  }
}
