// Paste this into Chrome DevTools Console when stuck at confirmidentifier page
// This will show what the automation is looking for

(function() {
    console.log('=== Google Login Page Diagnostic ===');
    console.log('URL:', window.location.href);
    console.log('Pathname:', window.location.pathname);

    // Check email field
    const emailSelectors = [
        'input[type="email"]',
        'input[name="identifier"]',
        'input[autocomplete*="username" i]'
    ];
    console.log('\n--- Email Field ---');
    emailSelectors.forEach(sel => {
        const el = document.querySelector(sel);
        if (el) {
            const rect = el.getBoundingClientRect();
            console.log(`Found: ${sel}`);
            console.log(`  Visible: ${el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0}`);
            console.log(`  Value: "${el.value}"`);
        }
    });

    // Check submit buttons
    console.log('\n--- Submit Buttons ---');
    const buttonSelectors = [
        '#identifierNext',
        '#passwordNext',
        '#totpNext',
        '[jsname="LgbsSe"]',
        '[jsname="Njthtb"]',
        '[data-primary-action-label]',
        'button[type="submit"]'
    ];

    buttonSelectors.forEach(sel => {
        const el = document.querySelector(sel);
        if (el) {
            const rect = el.getBoundingClientRect();
            console.log(`Found: ${sel}`);
            console.log(`  Text: "${el.innerText || el.textContent}"`);
            console.log(`  Disabled: ${el.disabled}`);
            console.log(`  Visible: ${el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0}`);
            console.log(`  Rect: width=${rect.width} height=${rect.height}`);
        }
    });

    // Check all buttons with text matching continue/next
    console.log('\n--- All Continue/Next Buttons ---');
    const labels = ['next', 'tiếp', 'continue', 'sign in', 'đăng nhập', 'submit'];
    const candidates = Array.from(document.querySelectorAll('[role="button"], button'));

    candidates.forEach(btn => {
        const label = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        if (labels.some(item => label.includes(item))) {
            const rect = btn.getBoundingClientRect();
            console.log('Button:', {
                text: btn.innerText || btn.textContent,
                ariaLabel: btn.getAttribute('aria-label'),
                disabled: btn.disabled,
                visible: btn.getClientRects().length > 0 && rect.width > 0 && rect.height > 0,
                rect: { width: rect.width, height: rect.height },
                selector: btn.id ? `#${btn.id}` : btn.className
            });
        }
    });

    console.log('\n=== End Diagnostic ===');
})();
