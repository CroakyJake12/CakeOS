# CakeOS HUI Linux graphical preview host

This is the first graphical Linux backend slice for HUI. It is intentionally a normal unprivileged desktop process: it does not replace GNOME Shell or Mutter, install a GDM session, or call privileged OS services.

The host consumes the platform-neutral `Haven.UI` source pinned by `havenos.lock`. `HUI/stage-donor.sh` copies that exact pinned source and strips only generated `obj*`/`bin*` build-output trees; the graphical build does not patch or rewrite donor source after staging.

The initial renderer deliberately implements only the HUI draw-command subset used by this preview and throws on unsupported commands so a partial backend cannot be mistaken for full HUI compatibility.

The automated graphical gate must:

1. stage the exact pinned HUI source;
2. compile a self-contained Linux-x64 Avalonia host;
3. check native-library closure for every shipped ELF file;
4. start a real window under Xvfb and a lightweight window manager;
5. exercise HUI pointer and keyboard invocation through `HavenInputRouter`;
6. capture the displayed application window to a PNG;
7. build the graphical Debian package twice from the same publish payload and require byte equality;
8. inspect the package metadata, installed file set, executable launcher, and desktop entry;
9. preserve runtime, screenshot, dependency, package, and checksum evidence in CI artifacts.

Text measurement is performed through Avalonia's real `FormattedText` metrics so HUI layout receives the same wrapped text height that the Linux renderer will draw. This is part of the graphical acceptance boundary: a screenshot with overlapping wrapped text is a failed visual QA result even if the process exits successfully.

The package remains a preview package. Its desktop entry launches the HUI preview as an ordinary user application; it does not create or select a CakeOS GDM session and it does not alter GNOME Shell, Mutter, apt sources, or the image package list.

The CI screenshot is evidence that a real Linux window was rendered in the runner's virtual display; it is not evidence that the approved CakeOS VM has displayed the same window. Passing this gate is graphical Linux-host and package evidence, not boot evidence or approved-VM visual proof.
