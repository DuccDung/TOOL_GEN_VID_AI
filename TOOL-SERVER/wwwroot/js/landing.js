(() => {
    'use strict';

    document.documentElement.classList.add('js');

    const body = document.body;
    const header = document.querySelector('[data-site-header]');
    const menuToggle = document.querySelector('[data-menu-toggle]');
    const siteNav = document.querySelector('[data-site-nav]');
    const desktopMedia = window.matchMedia('(min-width: 921px)');

    const setMenu = (open) => {
        body.classList.toggle('menu-open', open);
        menuToggle?.classList.toggle('menu-open', open);
        menuToggle?.setAttribute('aria-expanded', String(open));

        const label = menuToggle?.querySelector('.sr-only');
        if (label) label.textContent = open ? 'Đóng menu' : 'Mở menu';
    };

    menuToggle?.addEventListener('click', () => {
        setMenu(!body.classList.contains('menu-open'));
    });

    siteNav?.querySelectorAll('a').forEach((link) => {
        link.addEventListener('click', () => setMenu(false));
    });

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape' || !body.classList.contains('menu-open')) return;
        setMenu(false);
        menuToggle?.focus();
    });

    desktopMedia.addEventListener?.('change', (event) => {
        if (event.matches) setMenu(false);
    });

    const updateHeader = () => {
        header?.classList.toggle('is-scrolled', window.scrollY > 12);
    };

    updateHeader();
    window.addEventListener('scroll', updateHeader, { passive: true });

    const revealItems = [...document.querySelectorAll('[data-reveal]')];
    if ('IntersectionObserver' in window) {
        const revealObserver = new IntersectionObserver((entries, observer) => {
            entries.forEach((entry) => {
                if (!entry.isIntersecting) return;
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            });
        }, { rootMargin: '0px 0px -8% 0px', threshold: 0.08 });

        revealItems.forEach((item) => revealObserver.observe(item));
    } else {
        revealItems.forEach((item) => item.classList.add('is-visible'));
    }

    const sectionLinks = [...document.querySelectorAll('.site-nav a[href^="#"]')];
    const sections = sectionLinks
        .map((link) => document.querySelector(link.getAttribute('href')))
        .filter(Boolean);

    if ('IntersectionObserver' in window && sections.length > 0) {
        const sectionObserver = new IntersectionObserver((entries) => {
            const visible = entries
                .filter((entry) => entry.isIntersecting)
                .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
            if (!visible) return;

            sectionLinks.forEach((link) => {
                const active = link.getAttribute('href') === `#${visible.target.id}`;
                link.classList.toggle('is-active', active);
                if (active) link.setAttribute('aria-current', 'location');
                else link.removeAttribute('aria-current');
            });
        }, { rootMargin: '-20% 0px -65% 0px', threshold: [0, 0.25, 0.5] });

        sections.forEach((section) => sectionObserver.observe(section));
    }

    const faqItems = [...document.querySelectorAll('.faq-item')];
    const setFaq = (item, open) => {
        const button = item.querySelector('button');
        const answer = item.querySelector('.faq-answer');
        item.classList.toggle('is-open', open);
        button?.setAttribute('aria-expanded', String(open));
        answer?.setAttribute('aria-hidden', String(!open));
    };

    faqItems.forEach((item, index) => {
        const button = item.querySelector('button');
        const answer = item.querySelector('.faq-answer');
        if (!button || !answer) return;

        const answerId = `faq-answer-${index + 1}`;
        answer.id = answerId;
        answer.setAttribute('role', 'region');
        answer.setAttribute('aria-hidden', 'true');
        button.setAttribute('aria-controls', answerId);

        button.addEventListener('click', () => {
            const willOpen = !item.classList.contains('is-open');
            faqItems.forEach((candidate) => setFaq(candidate, false));
            setFaq(item, willOpen);
        });
    });

    document.querySelectorAll('[data-current-year]').forEach((element) => {
        element.textContent = String(new Date().getFullYear());
    });
})();
