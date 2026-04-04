---
name: Visual/UX polish audit — full app (2026-04-01)
description: Comprehensive Fluent 2 polish audit covering all 6 views + all dashboard controls; key patterns, issues, and recommended fixes
type: project
---

Full audit completed 2026-04-01. Key findings:

**What's solid:**
- MicaBackdrop on MainWindow is correct
- Semantic color tokens used consistently (CardBackgroundFillColorDefaultBrush, CardStrokeColorDefaultBrush, ThemeResource throughout)
- InfoBar used correctly for errors
- TeachingTip used in LaunchFormControl for contextual help
- ProgressBar/ProgressRing both used; indeterminate ProgressBar for section-level loading, ProgressRing for inline actions — appropriate distinction
- AutomationProperties.Name on icon-only buttons in search panel — good accessibility habit

**Critical issues found:**
- `Foreground="White"` hardcoded in SessionCard StatusBadge and TypeBadge — breaks High Contrast mode
- `Opacity="0.3/0.4/0.5/0.6"` used everywhere to dim text instead of semantic `TextFillColorSecondaryBrush / TextFillColorTertiaryBrush / TextFillColorDisabledBrush` — breaks High Contrast
- Landing tiles use `CornerRadius="16"` — WinUI 3 cards use 8px; 16px is a web pattern anti-pattern
- ResearchPage hardcodes `FontSize="11"` and `FontSize="13"` instead of CaptionTextBlockStyle/BodyTextBlockStyle
- Title bar uses a custom Grid, not `TitleBar` with `ExtendsContentIntoTitleBar=True` — draggable region is missing, caption buttons float incorrectly
- LandingView tile hover is implemented in code-behind (C#) with instant background swap — no transition animation
- SessionListControl has InfoBar and ScrollViewer both assigned to Grid.Row="2" — they overlap
- DashboardPage is a skeleton of empty Borders with no empty states defined
- ResearchPage left panel uses fixed `Width="300"` — will clip at narrow widths or in snap layouts
- `FontSize="11"` in ResearchPage ListView item template — below 12px minimum for legibility
- SearchPage Results toolbar buttons ("CSV", "TSV", "Columns") are bare text Buttons — should be icon+label or CommandBar AppBarButton pattern
- LaunchFormControl duplicates Standard/Advanced tabs entirely instead of sharing a ViewModel with conditional visibility — structural but affects XAML consistency

**Why / How to apply:**
- Fix hardcoded White foreground first — it's a High Contrast accessibility blocker
- Replace all Opacity dimming with semantic brushes — second priority
- Title bar extension gives the app the Windows 11 integrated look users expect
- Landing tile hover animation (even a 150ms fade) makes the app feel alive vs. static
- Fix Grid.Row collision in SessionListControl before it causes visual bugs in production
