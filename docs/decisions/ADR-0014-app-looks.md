# ADR-0014: Selectable app looks (Classic, Lens, Instrument, Stage)

- **Status:** Accepted
- **Date:** 2026-10-12

## Context
The stock Fluent look (Mica, system accent, grey cards) made XrealScreen look generic. Three directions were mocked up
and then built as a proof of concept in the app; the user wanted to keep all of them and make **Stage** the default.

## Decision
- **Four looks**, picked under Settings → **Look** and applied while the app runs (`AppLook`, `AppPreferences.Look`,
  default `Stage`):
  - **Classic**: stock Fluent; follows the app theme setting.
  - **Lens** (dark): graphite with a cyan-rose-gold "coated lens" gradient, only on Start and the page marker.
  - **Instrument** (light): condensed Bahnschrift labels, monospace numbers, signal orange, degree-scale field map.
  - **Stage** (dark): the screens drawn in a dark room at their real angles, one floating dock, icon-rail navigation.
- **One resource dictionary per look** (`src/XrealScreen.App/Looks/<Name>Look.xaml`) merged after
  `XamlControlsResources`; `MainWindow.ApplyLook` swaps it, flips the theme once so `{ThemeResource}` re-resolves in the
  shell, and re-navigates the open page. Control templates read brushes such as `AccentButtonBackground`, so each look
  overrides those brushes (not only the accent colours). Every look defines `CardStyle`, `PageTitleStyle`,
  `LookShellBackground`, `LookDisplayFontFamily` and `LookMonoFontFamily`.
- **Own Home page per look** (`Pages/Looks/*HomePage`); the other pages are shared and take the look's resources.
  Stage also replaces the Screens page preview with its `StageView`. Shared formatting/actions in `LookFormat`.
- **Theme:** Lens and Stage are dark, Instrument light; the App theme setting only applies to Classic and is locked
  for the other looks.
- **Fonts:** only fonts shipped with Windows (Segoe UI Variable, Bahnschrift, Cascadia Mono → Consolas), so nothing
  new to license or bundle.

## Consequences
- New pages must use `CardStyle` / `PageTitleStyle` and theme brushes so they follow every look.
- A new look = one dictionary + one Home page + an `AppLook` value; keys used by the shared pages must exist in all
  dictionaries (a missing `StaticResource` fails at page load).
- Title-bar caption buttons are not yet tinted for the dark looks.
