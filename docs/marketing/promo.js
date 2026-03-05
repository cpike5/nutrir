/* ── Particle System ────────────────────────────────────── */
(function initParticles() {
  const canvas = document.getElementById('particle-canvas');
  if (!canvas || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

  const ctx = canvas.getContext('2d');
  let w, h, particles = [], mouseX = 0, mouseY = 0;

  function resize() {
    const hero = canvas.parentElement;
    w = canvas.width = hero.offsetWidth;
    h = canvas.height = hero.offsetHeight;
  }

  function createParticles() {
    particles = [];
    const count = Math.min(45, Math.floor(w * h / 25000));
    for (let i = 0; i < count; i++) {
      particles.push({
        x: Math.random() * w,
        y: Math.random() * h,
        vx: (Math.random() - 0.5) * 0.3,
        vy: (Math.random() - 0.5) * 0.3,
        size: Math.random() * 2 + 0.5,
        opacity: Math.random() * 0.4 + 0.1,
      });
    }
  }

  function draw() {
    ctx.clearRect(0, 0, w, h);
    particles.forEach(p => {
      // Subtle parallax from mouse
      const dx = (mouseX - w / 2) * 0.005;
      const dy = (mouseY - h / 2) * 0.005;

      p.x += p.vx + dx * p.size * 0.1;
      p.y += p.vy + dy * p.size * 0.1;

      // Wrap around
      if (p.x < 0) p.x = w;
      if (p.x > w) p.x = 0;
      if (p.y < 0) p.y = h;
      if (p.y > h) p.y = 0;

      ctx.beginPath();
      ctx.arc(p.x, p.y, p.size, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(116, 198, 157, ${p.opacity})`;
      ctx.fill();
    });

    // Draw connections between nearby particles
    for (let i = 0; i < particles.length; i++) {
      for (let j = i + 1; j < particles.length; j++) {
        const dx = particles[i].x - particles[j].x;
        const dy = particles[i].y - particles[j].y;
        const dist = Math.sqrt(dx * dx + dy * dy);
        if (dist < 120) {
          ctx.beginPath();
          ctx.moveTo(particles[i].x, particles[i].y);
          ctx.lineTo(particles[j].x, particles[j].y);
          ctx.strokeStyle = `rgba(116, 198, 157, ${0.06 * (1 - dist / 120)})`;
          ctx.lineWidth = 0.5;
          ctx.stroke();
        }
      }
    }

    requestAnimationFrame(draw);
  }

  resize();
  createParticles();
  draw();

  window.addEventListener('resize', () => { resize(); createParticles(); });
  document.addEventListener('mousemove', e => { mouseX = e.clientX; mouseY = e.clientY; });
})();

/* ── Scroll Reveal ─────────────────────────────────────── */
const revealObserver = new IntersectionObserver((entries) => {
  entries.forEach(entry => {
    if (entry.isIntersecting) {
      entry.target.classList.add('visible');
    }
  });
}, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });

document.querySelectorAll('.reveal, .reveal-left, .reveal-right, .reveal-scale').forEach(el => {
  revealObserver.observe(el);
});

/* ── Nav Scroll Effect ─────────────────────────────────── */
const nav = document.querySelector('.nav');
let ticking = false;
window.addEventListener('scroll', () => {
  if (!ticking) {
    requestAnimationFrame(() => {
      nav.classList.toggle('scrolled', window.scrollY > 40);
      ticking = false;
    });
    ticking = true;
  }
});

/* ── Scroll Progress Bar ──────────────────────────────── */
const scrollProgress = document.querySelector('.scroll-progress');
window.addEventListener('scroll', () => {
  const max = document.documentElement.scrollHeight - window.innerHeight;
  const pct = (window.scrollY / max) * 100;
  scrollProgress.style.width = pct + '%';
}, { passive: true });

/* ── Smooth Scroll for Anchors ─────────────────────────── */
document.querySelectorAll('a[href^="#"]').forEach(a => {
  a.addEventListener('click', (e) => {
    const target = document.querySelector(a.getAttribute('href'));
    if (target) {
      e.preventDefault();
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });
      document.getElementById('nav-links').classList.remove('open');
    }
  });
});

/* ── Typing Effect ─────────────────────────────────────── */
(function initTyping() {
  const el = document.getElementById('typing-target');
  const cursor = document.getElementById('typing-cursor');
  if (!el || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
    if (cursor) cursor.classList.add('hidden');
    return;
  }

  const text = el.textContent;
  el.textContent = '';
  el.style.visibility = 'visible';
  let i = 0;

  function type() {
    if (i < text.length) {
      el.textContent += text.charAt(i);
      i++;
      setTimeout(type, 45 + Math.random() * 35);
    } else {
      setTimeout(() => cursor.classList.add('hidden'), 1500);
    }
  }

  // Start typing when hero is visible
  const heroObserver = new IntersectionObserver((entries) => {
    if (entries[0].isIntersecting) {
      setTimeout(type, 600);
      heroObserver.disconnect();
    }
  });
  heroObserver.observe(el.closest('.hero'));
})();

/* ── Counter Animation ─────────────────────────────────── */
document.querySelectorAll('[data-count]').forEach(el => {
  const target = parseInt(el.dataset.count, 10);
  const suffix = el.dataset.suffix || '';
  let counted = false;

  const counterObserver = new IntersectionObserver((entries) => {
    if (entries[0].isIntersecting && !counted) {
      counted = true;
      const duration = 1200;
      const start = performance.now();

      function update(now) {
        const elapsed = now - start;
        const progress = Math.min(elapsed / duration, 1);
        // Ease out cubic
        const eased = 1 - Math.pow(1 - progress, 3);
        el.textContent = Math.round(target * eased) + suffix;
        if (progress < 1) requestAnimationFrame(update);
      }
      requestAnimationFrame(update);
    }
  }, { threshold: 0.5 });

  counterObserver.observe(el);
});

/* ── Value Card Spotlight Effect ───────────────────────── */
document.querySelectorAll('.value-card').forEach(card => {
  const spotlight = card.querySelector('.spotlight');
  if (!spotlight) return;

  card.addEventListener('mousemove', (e) => {
    const rect = card.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    spotlight.style.background = `radial-gradient(400px circle at ${x}px ${y}px, rgba(82,183,136,0.08), transparent 60%)`;
  });
});

/* ── Button Ripple Effect ──────────────────────────────── */
document.querySelectorAll('.btn-primary').forEach(btn => {
  btn.addEventListener('mousemove', (e) => {
    const rect = btn.getBoundingClientRect();
    const x = ((e.clientX - rect.left) / rect.width * 100);
    const y = ((e.clientY - rect.top) / rect.height * 100);
    btn.style.setProperty('--ripple-x', x + '%');
    btn.style.setProperty('--ripple-y', y + '%');
  });
});

/* ── Screenshot Tabs ───────────────────────────────────── */
(function initTabs() {
  const tabs = document.querySelectorAll('.screenshot-tab');
  const panels = document.querySelectorAll('.screenshot-panel');
  const indicator = document.querySelector('.tab-indicator');
  if (!tabs.length || !indicator) return;

  function activateTab(tab) {
    const targetId = tab.dataset.tab;

    tabs.forEach(t => t.classList.remove('active'));
    tab.classList.add('active');

    panels.forEach(p => {
      p.classList.remove('active');
      if (p.id === targetId) p.classList.add('active');
    });

    // Slide indicator
    const rect = tab.getBoundingClientRect();
    const parentRect = tab.parentElement.getBoundingClientRect();
    indicator.style.left = (rect.left - parentRect.left) + 'px';
    indicator.style.width = rect.width + 'px';
  }

  tabs.forEach(tab => {
    tab.addEventListener('click', () => activateTab(tab));
  });

  // Initialize
  activateTab(tabs[0]);
  // Recalc on resize
  window.addEventListener('resize', () => activateTab(document.querySelector('.screenshot-tab.active')));
})();

/* ── Mobile Nav Toggle ─────────────────────────────────── */
document.querySelector('.nav-mobile-btn')?.addEventListener('click', () => {
  document.getElementById('nav-links').classList.toggle('open');
});

/* ── Back to Top ───────────────────────────────────────── */
document.querySelector('.back-to-top')?.addEventListener('click', () => {
  window.scrollTo({ top: 0, behavior: 'smooth' });
});

/* ── Scroll Hint ───────────────────────────────────────── */
document.querySelector('.scroll-hint')?.addEventListener('click', () => {
  document.getElementById('values')?.scrollIntoView({ behavior: 'smooth' });
});
