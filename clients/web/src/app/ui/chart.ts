import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
  effect,
  inject,
  input,
  viewChild,
  OnDestroy,
} from '@angular/core';
import {
  ArcElement,
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  ChartConfiguration,
  DoughnutController,
  Filler,
  Legend,
  LineController,
  LineElement,
  LinearScale,
  PointElement,
  Tooltip,
} from 'chart.js';
import { MASK, PrivacyService } from '../core/privacy/privacy.service';
import { ThemeService } from '../core/theme/theme.service';

// Only what the app uses (keeps the bundle small).
Chart.register(
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  DoughnutController,
  ArcElement,
  CategoryScale,
  LinearScale,
  Filler,
  Legend,
  Tooltip,
);

/** Colors of the current theme, read from the CSS tokens (see styles/theme.css). */
export interface ChartColors {
  series: string[];
  income: string;
  expenses: string;
  line: string;
  lineFill: string;
  text: string;
  grid: string;
}

export type ChartConfigFactory = (colors: ChartColors) => ChartConfiguration;

/**
 * Chart.js wrapper. Takes a factory that receives the theme colors, so charts follow the design
 * tokens and are redrawn when the theme changes.
 */
@Component({
  selector: 'ui-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'relative block' },
  template: `<canvas #canvas [attr.aria-label]="label()" role="img"></canvas>`,
})
export class ChartView implements OnDestroy {
  private readonly document = inject(DOCUMENT);
  private readonly theme = inject(ThemeService);
  private readonly privacy = inject(PrivacyService);
  private readonly canvas = viewChild.required<ElementRef<HTMLCanvasElement>>('canvas');
  private chart: Chart | null = null;

  readonly config = input.required<ChartConfigFactory>();
  readonly label = input.required<string>();

  constructor() {
    effect(() => {
      this.theme.isDark(); // redraw with the new colors when the theme changes
      const hidden = this.privacy.hidden(); // and when amounts are hidden or shown
      const factory = this.config();

      // Wait for the theme class to be applied before reading the CSS variables.
      queueMicrotask(() => this.render(factory, hidden));
    });
  }

  ngOnDestroy(): void {
    this.chart?.destroy();
  }

  private render(factory: ChartConfigFactory, hidden: boolean): void {
    const colors = this.readColors();
    Chart.defaults.color = colors.text;
    Chart.defaults.font.family = getComputedStyle(this.document.body).fontFamily;

    const config = factory(colors);
    if (hidden) maskValues(config);

    this.chart?.destroy();
    this.chart = new Chart(this.canvas().nativeElement, config);
  }

  private readColors(): ChartColors {
    const style = getComputedStyle(this.document.documentElement);
    const token = (name: string) => style.getPropertyValue(name).trim();

    return {
      series: [1, 2, 3, 4, 5, 6, 7, 8].map((i) => token(`--chart-${i}`)),
      income: token('--success'),
      expenses: token('--danger'),
      line: token('--primary'),
      lineFill: token('--primary-soft'),
      text: token('--muted'),
      grid: token('--border'),
    };
  }
}

/**
 * "Hide amounts": the shapes stay, the numbers go. Value axes lose their labels and tooltips show
 * only the series and category names.
 */
function maskValues(config: ChartConfiguration): void {
  const options = (config.options ??= {}) as ChartConfiguration['options'] & {
    indexAxis?: 'x' | 'y';
    scales?: Record<string, { ticks?: { callback?: unknown } }>;
  };

  if (config.type === 'line' || config.type === 'bar') {
    const valueAxis = options.indexAxis === 'y' ? 'x' : 'y';
    const scales = (options.scales ??= {});
    const axis = (scales[valueAxis] ??= {});
    axis.ticks = { ...axis.ticks, callback: () => MASK };
  }

  const plugins = (options.plugins ??= {});
  plugins.tooltip = {
    ...plugins.tooltip,
    callbacks: {
      ...plugins.tooltip?.callbacks,
      label: (ctx) => ` ${ctx.dataset.label || ctx.label}: ${MASK}`,
      footer: () => '',
    },
  };
}
