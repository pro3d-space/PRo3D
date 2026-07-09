/* PRo3D — small progressive enhancements. The site works fine without JS. */
(function () {
  "use strict";

  var reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  /* Mobile navigation ------------------------------------------------ */
  var toggle = document.querySelector(".nav-toggle");
  var nav = document.getElementById("site-nav");

  if (toggle && nav) {
    toggle.addEventListener("click", function () {
      var open = toggle.getAttribute("aria-expanded") === "true";
      toggle.setAttribute("aria-expanded", String(!open));
      toggle.setAttribute("aria-label", open ? "Open menu" : "Close menu");
      nav.classList.toggle("is-open", !open);
    });

    nav.addEventListener("click", function (e) {
      if (e.target.tagName === "A") {
        toggle.setAttribute("aria-expanded", "false");
        nav.classList.remove("is-open");
      }
    });

    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && nav.classList.contains("is-open")) {
        toggle.setAttribute("aria-expanded", "false");
        nav.classList.remove("is-open");
        toggle.focus();
      }
    });
  }

  /* Header border once scrolled -------------------------------------- */
  var header = document.querySelector(".site-header");
  if (header) {
    var onScroll = function () {
      header.classList.toggle("is-stuck", window.scrollY > 8);
    };
    window.addEventListener("scroll", onScroll, { passive: true });
    onScroll();
  }

  /* Reveal on scroll -------------------------------------------------- */
  var revealables = document.querySelectorAll(".reveal");
  if (reduceMotion || !("IntersectionObserver" in window)) {
    revealables.forEach(function (el) { el.classList.add("is-visible"); });
  } else {
    var revealer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          revealer.unobserve(entry.target);
        }
      });
    }, { rootMargin: "0px 0px -12% 0px", threshold: 0.05 });

    revealables.forEach(function (el) { revealer.observe(el); });
  }

  /* Highlight the nav link for the section in view --------------------- */
  var sections = document.querySelectorAll("main section[id]");
  var navLinks = new Map();

  document.querySelectorAll('.site-nav a[href^="#"]').forEach(function (link) {
    navLinks.set(link.getAttribute("href").slice(1), link);
  });

  if (sections.length && navLinks.size && "IntersectionObserver" in window) {
    var spy = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        var link = navLinks.get(entry.target.id);
        if (link && entry.isIntersecting) {
          navLinks.forEach(function (l) { l.classList.remove("is-active"); });
          link.classList.add("is-active");
        }
      });
    }, { rootMargin: "-45% 0px -50% 0px" });

    sections.forEach(function (s) { spy.observe(s); });
  }

  /* Footer year -------------------------------------------------------- */
  var year = document.getElementById("year");
  if (year) year.textContent = String(new Date().getFullYear());
})();
