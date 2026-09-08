// Screenshot driver for MEDOCS DME user documentation.
// Why: the Chrome extension is unavailable, and docs need repeatable shots.
// Usage: node shoot.js <outDir> <path1> <path2> ...
const puppeteer = require('puppeteer-core');
const path = require('path');
const fs = require('fs');

const CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const BASE = 'http://localhost:5005';
const EMAIL = 'admin@md.com';
const PASSWORD = 'DemoPass@2026';
const OTP = '123456';

const outDir = process.argv[2];
const paths = process.argv.slice(3);
fs.mkdirSync(outDir, { recursive: true });

const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await puppeteer.launch({
    executablePath: CHROME,
    headless: 'new',
    args: ['--no-sandbox', '--window-size=1600,1000'],
    defaultViewport: { width: 1600, height: 1000, deviceScaleFactor: 2 }
  });
  const page = await browser.newPage();
  await page.goto(BASE, { waitUntil: 'networkidle2' });

  await page.waitForSelector('#loginEmail', { visible: true });
  await page.type('#loginEmail', EMAIL);
  await page.type('#loginPassword', PASSWORD);
  await page.click('#loginForm button[type=submit]');

  await page.waitForSelector('#otpCode', { visible: true, timeout: 20000 });
  await page.type('#otpCode', OTP);
  await page.click('#otpForm button[type=submit]');
  await sleep(4000);

  console.log('after login:', page.url());

  for (const p of paths) {
    const url = BASE + (p.startsWith('/') ? p : '/' + p);
    await page.goto(url, { waitUntil: 'networkidle2' });
    await sleep(1500);
    const name = (p.replace(/^\//, '').replace(/[\/{}]/g, '-') || 'root') + '.png';
    const file = path.join(outDir, name);
    await page.screenshot({ path: file, fullPage: true });
    console.log('shot:', p, '->', file);
  }
  await browser.close();
})().catch(e => { console.error('ERROR', e.message); process.exit(1); });
