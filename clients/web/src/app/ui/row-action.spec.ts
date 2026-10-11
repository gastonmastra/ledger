import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActionsMenu, MenuAction } from './actions-menu';
import { RowAction } from './row-action';

@Component({
  imports: [RowAction, ActionsMenu],
  template: `
    <ul>
      <li uiRowAction (rowOpen)="opened = opened + 1">
        <span class="text">Coffee</span>
        <button type="button" class="inner" (click)="inner = inner + 1">Edit</button>
        <ui-actions-menu [actions]="actions" />
      </li>
    </ul>
  `,
})
class Host {
  opened = 0;
  inner = 0;
  ran = 0;
  readonly actions: MenuAction[] = [
    { label: 'Delete', run: () => this.ran++, danger: true },
    { label: 'Hidden', run: () => this.ran++, hidden: true },
  ];
}

describe('RowAction and ActionsMenu', () => {
  it('opens the row on click, Enter and Space, but not from buttons inside it', async () => {
    const fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    const host = fixture.componentInstance;
    const row: HTMLElement = fixture.nativeElement.querySelector('li');

    expect(row.getAttribute('role')).toBeNull(); // still a list item
    expect(row.getAttribute('aria-haspopup')).toBe('dialog');
    expect(row.tabIndex).toBe(0);

    (fixture.nativeElement.querySelector('.text') as HTMLElement).click();
    row.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter' }));
    row.dispatchEvent(new KeyboardEvent('keydown', { key: ' ' }));
    expect(host.opened).toBe(3);

    (fixture.nativeElement.querySelector('.inner') as HTMLElement).click();
    expect(host.inner).toBe(1);
    expect(host.opened).toBe(3);

    // The ⋯ button opens its menu, not the row.
    (fixture.nativeElement.querySelector('ui-actions-menu button') as HTMLElement).click();
    await fixture.whenStable();
    expect(host.opened).toBe(3);

    const items = Array.from(document.querySelectorAll<HTMLButtonElement>('[cdkmenuitem], .cdk-menu button'));
    expect(items.map((i) => i.textContent?.trim())).toEqual(['Delete']);
    items[0].click();
    expect(host.ran).toBe(1);
  });
});
