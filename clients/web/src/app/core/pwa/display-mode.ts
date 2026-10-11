/** True when the app runs installed (home screen / app window) rather than in a browser tab. */
export function isStandalone(win: Window = window): boolean {
  return (
    win.matchMedia?.('(display-mode: standalone)').matches === true ||
    (win.navigator as Navigator & { standalone?: boolean }).standalone === true
  );
}

/** iPhone and iPad (also iPadOS, which reports itself as a Mac with a touch screen). */
export function isIos(nav: Navigator = navigator): boolean {
  return /iphone|ipad|ipod/i.test(nav.userAgent) || (/macintosh/i.test(nav.userAgent) && nav.maxTouchPoints > 1);
}
