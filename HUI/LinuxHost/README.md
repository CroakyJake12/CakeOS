# CakeOS HUI Linux graphical preview host

This project is the first graphical Linux backend slice for HUI. It is intentionally a normal unprivileged desktop application, not a session, compositor, GNOME extension, or privileged service.

The host consumes the platform-neutral `Haven.UI` scene/layout/render contracts staged from the exact donor revision in `havenos.lock`. The initial backend translates only the draw-command subset exercised by the preview scene and routes basic pointer/keyboard activation. Unsupported commands fail visibly during development instead of being silently treated as proof of full HUI compatibility.

Acceptance for this slice is: build on Linux, start under a graphical/Xvfb session, create a real Avalonia window, render the HUI preview scene through the Linux backend, exercise a button using keyboard/pointer-compatible HUI input, and capture a screenshot artifact. This is still not approved-VM visual proof; that remains a later gate.
