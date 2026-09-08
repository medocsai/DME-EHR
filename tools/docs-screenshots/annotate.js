// Numbered-callout annotator for the MEDOCS DME user documentation.
//
// Why this exists: an image cannot be edited in place, and hand-placed labels
// rot the moment a screen changes. This takes a raw screenshot plus a marker
// list and renders the annotated PNG, so re-shooting a screen is a re-run,
// not a redraw.
//
// Usage: node annotate.js <in.png> <markers.json> <out.png> <cssWidth>
// markers.json: [{ "n": 1, "x": 30, "y": 112 }, ...]  x/y in CSS pixels.
const puppeteer = require('puppeteer-core');
const fs = require('fs');
const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';

const [, , inPng, markersJson, outPng, widthArg] = process.argv;
const width = parseInt(widthArg, 10);
const markers = JSON.parse(fs.readFileSync(markersJson, 'utf8'));
const b64 = fs.readFileSync(inPng).toString('base64');

const badges = markers.map(m =>
  `<div class="m" style="left:${m.x}px;top:${m.y}px">${m.n}</div>`).join('');

const html = `<style>
  *{margin:0;padding:0;box-sizing:border-box}
  body{background:#fff}
  .wrap{position:relative;width:${width}px}
  .wrap img{display:block;width:${width}px}
  .m{position:absolute;width:26px;height:26px;border-radius:50%;
     background:#d92d20;color:#fff;font:700 15px/26px system-ui,sans-serif;
     text-align:center;box-shadow:0 0 0 3px #fff}
</style>
<div class="wrap"><img src="data:image/png;base64,${b64}">${badges}</div>`;

(async () => {
  const browser = await puppeteer.launch({
    executablePath: CHROME, headless: 'new', args: ['--no-sandbox'],
    defaultViewport: { width, height: 800, deviceScaleFactor: 2 }
  });
  const page = await browser.newPage();
  await page.setContent(html, { waitUntil: 'networkidle0' });
  const el = await page.$('.wrap');
  await el.screenshot({ path: outPng });
  await browser.close();
  console.log('annotated ->', outPng, `(${markers.length} markers)`);
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
