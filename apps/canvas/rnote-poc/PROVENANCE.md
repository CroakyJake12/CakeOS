# Rnote PoC provenance

## Upstream

- Repository: `https://github.com/flxzt/rnote`
- Pinned release: `v0.14.2`
- Upstream licence declaration: `GPL-3.0-or-later`
- Consumed packages: `rnote-compose`, `rnote-engine`
- Rnote UI package consumed: no
- Upstream source copied into CakeOS: no
- Upstream fork created: no

## CakeOS integration rule

This proof of concept references upstream through Cargo Git dependencies pinned to the release tag. No Rnote source file is vendored into this directory.

Any production distribution that links this engine into CakeOS must pass a GPL compliance review and ship the corresponding covered source/notice material required by the selected distribution model. This file records provenance; it is not legal advice.

## Evidence boundary

A source reference does not establish a successful build. A successful synthetic test does not establish real tablet, Wayland, HUI rendering, packaging, or VM runtime behaviour. Those claims require their own direct evidence.
