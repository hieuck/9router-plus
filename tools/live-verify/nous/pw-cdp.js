const { chromium } = require('playwright');

const CDP = process.env.CDP || 'http://127.0.0.1:9333';

async function main() {
  let browser;
  try {
    browser = await chromium.connectOverCDP(CDP);
  } catch (err) {
    console.error('ERR', 'Cannot connect to CDP at ' + CDP + '. Make sure Chrome is running with --remote-debugging-port=9333 and playwright is installed: npm i playwright');
    process.exit(1);
  }

  const contexts = browser.contexts();
  if (!contexts.length) throw new Error('No browser context');
  const context = contexts[0];
  const pages = context.pages();
  if (!pages.length) throw new Error('No pages');

  const cmd = process.argv[2] || 'snapshot';
  const arg = process.argv[3];
  const arg2 = process.argv[4];

  // Prefer the Nous page; allow PW_PAGE=<index> to target another tab.
  const pageIndex = process.env.PW_PAGE ? parseInt(process.env.PW_PAGE, 10) : null;
  let page = pageIndex !== null && pages[pageIndex]
    ? pages[pageIndex]
    : (pages.find(p => /nousresearch/.test(p.url()))
      || pages.find(p => /accounts\.google|google\.com/.test(p.url()))
      || pages[pages.length - 1]);

  if (cmd === 'pages') {
    const info = await Promise.all(pages.map(async (p, i) => ({
      i, url: p.url(), title: await p.title().catch(() => '')
    })));
    console.log(JSON.stringify(info, null, 2));
  } else if (cmd === 'snapshot') {
    console.log('URL', page.url());
    console.log('TITLE', await page.title().catch(() => ''));
    const text = await page.evaluate(() => (document.body && document.body.innerText || '').slice(0, 9000));
    console.log('---TEXT---');
    console.log(text);
    console.log('---ELEMENTS---');
    const els = await page.evaluate(() => Array.from(document.querySelectorAll('a,button,input,[role="button"],select')).slice(0, 120).map((e, i) => ({
      i,
      tag: e.tagName,
      type: e.getAttribute('type') || '',
      text: ((e.innerText || e.value || e.getAttribute('aria-label') || e.getAttribute('placeholder') || '') + '').trim().slice(0, 100),
      href: (e.href || '').slice(0, 200),
      id: e.id || '',
      name: e.getAttribute('name') || ''
    })));
    console.log(JSON.stringify(els, null, 2));
  } else if (cmd === 'click-text') {
    const loc = page.locator('a,button,[role="button"],input[type="submit"],input[type="button"]')
      .filter({ hasText: new RegExp(arg.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'i') }).first();
    await loc.click({ timeout: 15000 });
    console.log('CLICKED', arg);
    await page.waitForTimeout(2500);
    console.log('URL_NOW', page.url());
  } else if (cmd === 'click-account') {
    const loc = page.locator(`[data-identifier="${arg}"]`).first();
    await loc.click({ timeout: 15000 });
    console.log('CLICKED_ACCOUNT', arg);
    await page.waitForTimeout(4000);
    console.log('URL_NOW', page.url());
  } else if (cmd === 'click-href') {
    const loc = page.locator(`a[href*="${arg}"]`).first();
    await loc.click({ timeout: 15000 });
    console.log('CLICKED_HREF', arg);
    await page.waitForTimeout(2500);
    console.log('URL_NOW', page.url());
  } else if (cmd === 'fill') {
    await page.locator(arg).first().fill(arg2 || '', { timeout: 15000 });
    console.log('FILLED', arg);
  } else if (cmd === 'front') {
    await page.bringToFront();
    console.log('FRONT', page.url());
  } else if (cmd === 'press') {
    await page.keyboard.press(arg);
    console.log('PRESSED', arg);
  } else if (cmd === 'goto') {
    await page.goto(arg, { waitUntil: 'domcontentloaded', timeout: 45000 });
    console.log('GOTO', arg, '->', page.url());
  } else if (cmd === 'eval') {
    console.log(JSON.stringify(await page.evaluate(arg), null, 2));
  } else {
    console.log('Unknown command', cmd);
  }

  // Disconnect only; keep Chrome open.
  await browser.close().catch(() => {});
}

main().catch(err => { console.error('ERR', err && err.message || err); process.exit(1); });