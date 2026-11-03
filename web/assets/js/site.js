// XrealScreen website: mobile menu, theme toggle, looks tabs, screenshot lightbox.
// Every page also works without JavaScript; these only enhance it.
(function () {
  "use strict";
  var root = document.documentElement;

  // Mobile menu
  var menuBtn = document.querySelector(".menu-btn");
  var nav = document.getElementById("site-nav");
  if (menuBtn && nav) {
    menuBtn.addEventListener("click", function () {
      var open = nav.classList.toggle("open");
      menuBtn.setAttribute("aria-expanded", open ? "true" : "false");
    });
  }

  // Theme toggle: system theme until the visitor picks one
  var themeBtn = document.querySelector(".theme-btn");
  if (themeBtn) {
    themeBtn.addEventListener("click", function () {
      var current = root.getAttribute("data-theme") ||
        (window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark");
      var next = current === "light" ? "dark" : "light";
      root.setAttribute("data-theme", next);
      try { localStorage.setItem("theme", next); } catch (e) { /* not saved */ }
    });
  }

  // Tabs (WAI-ARIA tabs pattern, arrow keys move between tabs)
  document.querySelectorAll("[data-tabs]").forEach(function (container) {
    var tabs = Array.prototype.slice.call(container.querySelectorAll('[role="tab"]'));
    var panels = tabs.map(function (t) { return document.getElementById(t.getAttribute("aria-controls")); });

    function select(index, focus) {
      tabs.forEach(function (t, i) {
        var on = i === index;
        t.setAttribute("aria-selected", on ? "true" : "false");
        t.tabIndex = on ? 0 : -1;
        panels[i].hidden = !on;
      });
      if (focus) tabs[index].focus();
    }

    tabs.forEach(function (tab, i) {
      tab.addEventListener("click", function () { select(i, false); });
      tab.addEventListener("keydown", function (e) {
        var n = tabs.length, target = null;
        if (e.key === "ArrowRight") target = (i + 1) % n;
        else if (e.key === "ArrowLeft") target = (i - 1 + n) % n;
        else if (e.key === "Home") target = 0;
        else if (e.key === "End") target = n - 1;
        if (target !== null) { e.preventDefault(); select(target, true); }
      });
    });
    select(0, false);
  });

  // Lightbox for screenshots (native <dialog>; links open the image if unsupported)
  var dialog = document.getElementById("lightbox");
  if (dialog && typeof dialog.showModal === "function") {
    var img = dialog.querySelector("img");
    var caption = dialog.querySelector("p");
    var closeBtn = dialog.querySelector(".close");

    document.querySelectorAll("a[data-lightbox]").forEach(function (link) {
      link.addEventListener("click", function (e) {
        e.preventDefault();
        var thumb = link.querySelector("img");
        img.src = link.getAttribute("href");
        img.alt = thumb ? thumb.alt : "";
        caption.textContent = link.getAttribute("data-caption") || "";
        dialog.showModal();
      });
    });
    closeBtn.addEventListener("click", function () { dialog.close(); });
    dialog.addEventListener("click", function (e) { if (e.target === dialog) dialog.close(); });
    dialog.addEventListener("close", function () { img.removeAttribute("src"); });
  }
})();
