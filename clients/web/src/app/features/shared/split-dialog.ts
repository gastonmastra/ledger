import { DIALOG_DATA, DialogRef } from '@angular/cdk/dialog';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { rxResource } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { LedgerApi } from '../../core/api/ledger-api';
import { CURRENCIES, Currency, SplitMethod, SplitRequest } from '../../core/api/models';
import { splitMethodLabel } from '../../core/i18n/labels';
import { OperationTracker } from '../../core/operations/operation-tracker';
import { Button, DateTimeInput, DialogLayout, FormField, Icon, Input, MoneyPipe, nowIso } from '../../ui';

export interface SplitPerson {
  personId: string;
  name: string;
  isMe: boolean;
}

export interface SplitDialogData {
  groupId: string | null;
  people: SplitPerson[];
}

interface PayerRow {
  personId: string;
  amount: number | null;
  walletId: string;
}

interface ParticipantRow {
  personId: string;
  selected: boolean;
  value: number | null;
}

/** Splits an expense among people (me, my contacts or a group's members), with any number of payers. */
@Component({
  selector: 'app-split-dialog',
  imports: [FormsModule, DialogLayout, FormField, Input, Button, Icon, MoneyPipe, DateTimeInput],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './split-dialog.html',
})
export class SplitDialog {
  private readonly api = inject(LedgerApi);
  private readonly tracker = inject(OperationTracker);
  protected readonly data = inject<SplitDialogData>(DIALOG_DATA);
  protected readonly ref = inject<DialogRef<boolean>>(DialogRef);

  protected readonly currencies = CURRENCIES;
  protected readonly methods: SplitMethod[] = ['Equal', 'Custom', 'Percent'];
  protected readonly methodLabel = splitMethodLabel;
  protected readonly me = this.data.people.find((p) => p.isMe) ?? null;

  protected readonly wallets = rxResource({ stream: () => this.api.wallets() });
  private readonly allCategories = rxResource({ stream: () => this.api.categories() });
  protected readonly categories = computed(() => (this.allCategories.value() ?? []).filter((c) => c.type === 'Expense'));

  protected readonly description = signal('');
  protected readonly total = signal<number | null>(null);
  protected readonly chosenCurrency = signal<Currency>('EUR');
  protected readonly method = signal<SplitMethod>('Equal');
  protected readonly categoryId = signal('');
  protected readonly occurredAt = signal(nowIso());
  protected readonly saving = signal(false);

  protected readonly payers = signal<PayerRow[]>([
    { personId: this.me?.personId ?? this.data.people[0].personId, amount: null, walletId: '' },
  ]);

  protected readonly participants = signal<ParticipantRow[]>(
    this.data.people.map((p) => ({ personId: p.personId, selected: true, value: null })),
  );

  /** If I paid, the currency is my wallet's. */
  protected readonly myPayer = computed(() => this.payers().find((p) => p.personId === this.me?.personId) ?? null);

  protected readonly currency = computed<Currency>(() => {
    const walletId = this.myPayer()?.walletId;
    return this.wallets.value()?.find((w) => w.id === walletId)?.currency ?? this.chosenCurrency();
  });

  protected readonly availablePayers = computed(() =>
    this.data.people.filter((p) => !this.payers().some((row) => row.personId === p.personId)),
  );

  protected readonly selectedParticipants = computed(() => this.participants().filter((p) => p.selected));
  protected readonly iParticipate = computed(() => this.selectedParticipants().some((p) => p.personId === this.me?.personId));

  /** With a single payer, that payer paid the whole total. */
  private readonly effectivePayers = computed(() => {
    const rows = this.payers();
    return rows.length === 1 ? [{ ...rows[0], amount: this.total() }] : rows;
  });

  protected readonly problem = computed<string | null>(() => {
    const total = this.total() ?? 0;
    if (total <= 0) return null;

    const paid = round(this.effectivePayers().reduce((sum, p) => sum + (p.amount ?? 0), 0));
    if (paid !== total) {
      return $localize`:@@split.payersMismatch:The payers add up to ${paid}:paid:, not ${total}:total:.`;
    }

    if (this.myPayer() && !this.myPayer()!.walletId) {
      return $localize`:@@split.chooseWallet:Choose the wallet you paid from.`;
    }

    const selected = this.selectedParticipants();
    if (selected.length === 0) return $localize`:@@split.noParticipants:Select who took part.`;

    const values = round(selected.reduce((sum, p) => sum + (p.value ?? 0), 0));
    if (this.method() === 'Custom' && values !== total) {
      return $localize`:@@split.sharesMismatch:The shares add up to ${values}:values:, not ${total}:total:.`;
    }
    if (this.method() === 'Percent' && values !== 100) {
      return $localize`:@@split.percentMismatch:The percentages add up to ${values}:values:, not 100.`;
    }

    return null;
  });

  protected readonly canSave = computed(
    () => (this.total() ?? 0) > 0 && !!this.description().trim() && this.problem() === null,
  );

  protected personName(personId: string): string {
    const person = this.data.people.find((p) => p.personId === personId);
    return person?.isMe ? $localize`:@@person.me:Me` : (person?.name ?? '');
  }

  protected addPayer(): void {
    const next = this.availablePayers()[0];
    if (next) this.payers.update((rows) => [...rows, { personId: next.personId, amount: null, walletId: '' }]);
  }

  protected removePayer(index: number): void {
    this.payers.update((rows) => rows.filter((_, i) => i !== index));
  }

  protected updatePayer(index: number, change: Partial<PayerRow>): void {
    this.payers.update((rows) => rows.map((row, i) => (i === index ? { ...row, ...change } : row)));
  }

  protected updateParticipant(index: number, change: Partial<ParticipantRow>): void {
    this.participants.update((rows) => rows.map((row, i) => (i === index ? { ...row, ...change } : row)));
  }

  protected async save(): Promise<void> {
    if (!this.canSave()) return;

    const request: SplitRequest = {
      groupId: this.data.groupId,
      total: this.total()!,
      currency: this.currency(),
      method: this.method(),
      description: this.description().trim(),
      occurredAt: this.occurredAt(),
      categoryId: this.iParticipate() ? this.categoryId() || null : null,
      payers: this.effectivePayers().map((p) => ({
        personId: p.personId,
        amount: p.amount!,
        walletId: p.personId === this.me?.personId ? p.walletId : null,
      })),
      participants: this.selectedParticipants().map((p) => ({
        personId: p.personId,
        value: this.method() === 'Equal' ? null : (p.value ?? 0),
      })),
    };

    this.saving.set(true);

    try {
      await this.tracker.submit(this.api.split(request), $localize`:@@split.saved:Expense split: “${request.description}:description:”.`);
      this.ref.close(true);
    } catch {
      this.saving.set(false);
    }
  }
}

function round(value: number): number {
  return Math.round(value * 100) / 100;
}
