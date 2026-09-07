# CakeOS HUI Linux graphical preview host

This is the first graphical Linux backend slice for HUI. It is intentionally a normal unprivileged desktop process: it does not replace GNOME Shell or Mutter, install a GDM session, or call privileged OS services.

The host consumes the platform-neutral `Haven.UI` source pinned by `havenos.lock`. The initial renderer deliberately implements only the HUI draw-command subset used by this preview and throws on unsupported commands so a partial backend cannot be mistaken for full HUI compatibility.

The automated graphical gate must:

1. stage the exact pinned HUI source;
2. compile a Linux-x64 Avalonia host;
3. start a real window under Xvfb;
4. exercise HUI pointer and keyboard activation through `HavenInputRouter`;
5. capture the displayed window to a PNG;
6. preserve logs and screenshot as CI evidence.

Text measurement is performed through Avalonia's real `FormattedText` metrics so HUI layout receives the same wrapped text height that the Linux renderer will draw. This is part of the graphical acceptance boundary: a screenshot with overlapping wrapped text is a failed visual QA result even if the process exits successfully.

The CI screenshot is evidence that a real Linux window was rendered in the runner's virtual display; it is not evidence that the approved CakeOS VM has displayed the same window.

Passing that gate is graphical Linux-host evidence, not approved-VM visual proof. Approved-VM installation and observation remain a later acceptance gate.
