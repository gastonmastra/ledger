import { TestBed } from '@angular/core/testing';
import { isIos, isStandalone } from './display-mode';
import { InstallPrompt } from './install-prompt';
import { Network } from './network';

describe('Installable app helpers', () => {
  it('tells iPhones and iPads apart (iPadOS says it is a Mac with a touch screen)', () => {
    const nav = (userAgent: string, maxTouchPoints = 0) => ({ userAgent, maxTouchPoints }) as Navigator;

    expect(isIos(nav('Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X)'))).toBe(true);
    expect(isIos(nav('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 5))).toBe(true);
    expect(isIos(nav('Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)', 0))).toBe(false);
    expect(isIos(nav('Mozilla/5.0 (Linux; Android 15)'))).toBe(false);
  });

  it('detects the installed app', () => {
    const win = (standalone: boolean) =>
      ({ matchMedia: () => ({ matches: standalone }), navigator: {} }) as unknown as Window;

    expect(isStandalone(win(true))).toBe(true);
    expect(isStandalone(win(false))).toBe(false);
  });

  it('offers the browser install prompt once it arrives, and forgets it after installing', async () => {
    const install = TestBed.inject(InstallPrompt);
    expect(['unavailable', 'ios']).toContain(install.state());

    const prompt = Object.assign(new Event('beforeinstallprompt'), {
      prompt: vi.fn(() => Promise.resolve()),
      userChoice: Promise.resolve({ outcome: 'accepted' as const }),
    });
    window.dispatchEvent(prompt);
    expect(install.state()).toBe('available');

    expect(await install.install()).toBe(true);
    expect(prompt.prompt).toHaveBeenCalled();

    window.dispatchEvent(new Event('appinstalled'));
    expect(install.state()).toBe('installed');
  });

  it('follows the connection', () => {
    const network = TestBed.inject(Network);

    window.dispatchEvent(new Event('offline'));
    expect(network.online()).toBe(false);
    window.dispatchEvent(new Event('online'));
    expect(network.online()).toBe(true);
  });
});
