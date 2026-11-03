// Runs in <head> before first paint: marks JS as available and applies the saved theme (no flash).
(function () {
  var root = document.documentElement;
  root.classList.add("js");
  try {
    var saved = localStorage.getItem("theme");
    if (saved === "light" || saved === "dark") root.setAttribute("data-theme", saved);
  } catch (e) { /* storage blocked: follow the system theme */ }
})();
