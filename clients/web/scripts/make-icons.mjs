// Generates the app icons from one image (ideally 1024×1024, PNG or SVG):
//   npm run icons -- path/to/icon.png
// Writes public/icons/{icon-192,icon-512,maskable-512,apple-touch-icon}.png and public/favicon.ico.
// Rendered with the headless Chromium that Playwright already installs (no image tools needed).
import { readFileSync, writeFileSync } from 'node:fs';
import { extname, resolve } from 'node:path';
import { chromium } from 'playwright';

const source = resolve(process.argv[2] ?? 'scripts/icon-placeholder.svg');
const type = { '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp' }[extname(source).toLowerCase()];
if (!type) throw new Error(`Unsupported image: ${source}`);
const dataUrl = `data:${type};base64,${readFileSync(source).toString('base64')}`;

// Maskable icons are cropped to a circle by Android: keep the image inside the central 80 %.
const outputs = [
  { file: 'public/icons/icon-192.png', size: 192, padding: 0, background: 'transparent' },
  { file: 'public/icons/icon-512.png', size: 512, padding: 0, background: 'transparent' },
  { file: 'public/icons/maskable-512.png', size: 512, padding: 0.1, background: '#4f46e5' },
  // iOS draws the home-screen icon on black when it has transparency: give it a solid background.
  { file: 'public/icons/apple-touch-icon.png', size: 180, padding: 0, background: '#ffffff' },
  { file: 'public/icons/favicon-48.png', size: 48, padding: 0, background: 'transparent' },
];

const browser = await chromium.launch();
const page = await browser.newPage();

for (const { file, size, padding, background } of outputs) {
  await page.setViewportSize({ width: size, height: size });
  await page.setContent(`<html><body style="margin:0;background:${background}">
    <div style="width:${size}px;height:${size}px;display:grid;place-items:center">
      <img src="${dataUrl}" style="width:${size * (1 - 2 * padding)}px;height:${size * (1 - 2 * padding)}px;object-fit:contain">
    </div></body></html>`);
  await page.locator('img').evaluate((img) => img.decode());
  await page.screenshot({ path: file, omitBackground: background === 'transparent' });
  console.log(`wrote ${file}`);
}

await browser.close();

// favicon.ico: a single 48×48 PNG wrapped in an ICO header (supported by every current browser).
const png = readFileSync('public/icons/favicon-48.png');
const header = Buffer.alloc(22);
header.writeUInt16LE(0, 0); header.writeUInt16LE(1, 2); header.writeUInt16LE(1, 4);
header.writeUInt8(48, 6); header.writeUInt8(48, 7); header.writeUInt8(0, 8); header.writeUInt8(0, 9);
header.writeUInt16LE(1, 10); header.writeUInt16LE(32, 12); header.writeUInt32LE(png.length, 14); header.writeUInt32LE(22, 18);
writeFileSync('public/favicon.ico', Buffer.concat([header, png]));
console.log('wrote public/favicon.ico');
