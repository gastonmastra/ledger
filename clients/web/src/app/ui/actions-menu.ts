import { CdkMenu, CdkMenuItem, CdkMenuTrigger } from '@angular/cdk/menu';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Button } from './button';
import { Icon, IconName } from './icon';

/** One entry of an actions menu. Hidden entries are left out (e.g. actions that don't apply now). */
export interface MenuAction {
  label: string;
  run: () => unknown;
  icon?: IconName;
  /** Shown in red (e.g. Delete). */
  danger?: boolean;
  hidden?: boolean;
  /** Tooltip explaining the action. */
  hint?: string;
}

/**
 * The "⋯" button of a row or a header: opens a menu with its actions (keyboard and focus handled by the CDK).
 * Clicks don't reach the row, so a clickable row doesn't also open its detail.
 */
@Component({
  selector: 'ui-actions-menu',
  imports: [CdkMenuTrigger, CdkMenu, CdkMenuItem, Icon, Button],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'inline-flex', '(click)': '$event.stopPropagation()', '(keydown)': '$event.stopPropagation()' },
  template: `
    @if (visible().length) {
      @if (text(); as text) {
        <button uiButton variant="secondary" type="button" [cdkMenuTriggerFor]="menu">
          <ui-icon [name]="icon()" size="sm" />{{ text }}<ui-icon name="chevronDown" size="sm" />
        </button>
      } @else {
        <button
          type="button"
          class="inline-flex size-9 cursor-pointer items-center justify-center rounded-ui text-muted transition hover:bg-surface-muted hover:text-foreground"
          [cdkMenuTriggerFor]="menu"
          [attr.aria-label]="label() ?? defaultLabel"
          [attr.title]="label() ?? defaultLabel"
        >
          <ui-icon [name]="icon()" />
        </button>
      }
    }
    <ng-template #menu>
      <div cdkMenu class="z-[1050] min-w-48 rounded-ui border border-border bg-surface p-1 text-sm text-foreground shadow-lg">
        @for (action of visible(); track action.label) {
          <button
            cdkMenuItem
            type="button"
            class="flex w-full cursor-pointer items-center gap-2 rounded-ui px-3 py-2 text-left outline-none hover:bg-surface-muted focus:bg-surface-muted"
            [class.text-danger]="action.danger"
            [attr.title]="action.hint ?? null"
            (cdkMenuItemTriggered)="action.run()"
          >
            @if (action.icon) {
              <ui-icon [name]="action.icon" size="sm" />
            }
            {{ action.label }}
          </button>
        }
      </div>
    </ng-template>
  `,
})
export class ActionsMenu {
  readonly actions = input.required<MenuAction[]>();
  /** Accessible name of the button (default: "More actions"). */
  readonly label = input<string | null>(null);
  readonly icon = input<IconName>('dots');
  /** Shows a text button ("Add ▾") instead of the "⋯" icon. */
  readonly text = input<string | null>(null);

  protected readonly defaultLabel = $localize`:@@ui.moreActions:More actions`;
  protected readonly visible = computed(() => this.actions().filter((a) => !a.hidden));
}
