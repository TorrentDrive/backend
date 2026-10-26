# Changelog

## [2.4.2] - 2026-10-26

* Rebuild combined image with TorrentDrive/frontend v2.6.4

## [2.4.1] - 2026-10-26

* Rebuild combined image with TorrentDrive/frontend v2.6.2

## [2.4.0] - 2026-10-26

* Rebuild combined image with TorrentDrive/frontend v2.6.0

## [2.3.0] - 2026-10-26

* Rebuild combined image with TorrentDrive/frontend v2.5.0

## [2.2.0] - 2026-10-26

* fix(deploy): restore docker-compose.dokploy.yml using GHCR images for Dokploy (375cd99)
* fix(jobs): align phase filter counts, require explicit batch cancel IDs, and document IsStalled in glossary (28b1a78)
* feat(jobs): add token-aware search, multi-facet filtering, sorting, and batch actions (0ebcf0f)
* feat(ci): fold deploy repo into backend and unify all-in-one release pipeline (b66b7a1)
* docs(ci): add ADR-0005, release glossary terms, docs-only build skip, and upstream bump forwarding (6c18646)

## [2.1.1] - 2026-10-25

* ci: simplify CI/CD pipeline with direct automatic SemVer releases on main (ec98bcf)

## [2.1.0](https://github.com/TorrenClou/backend/compare/v2.0.1...v2.1.0) (2026-10-25)


### Features

* rebrand to TorrentDrive, add AI-SEO & ISP-blind torrent engine, and upgrade UI/PaaS/docs ([82a4ec2](https://github.com/TorrenClou/backend/commit/82a4ec2fd3ed836cc2593fe76d3a2d6b54dbc308))

## [2.0.1](https://github.com/TorrentDrive/backend/compare/v2.0.0...v2.0.1) (2026-09-03)


### Bug Fixes

* **ci:** retag from the release commit, not from latest ([#25](https://github.com/TorrentDrive/backend/issues/25)) ([b2b3e91](https://github.com/TorrentDrive/backend/commit/b2b3e9147ded8ff1d16bf97c676a07af7c16d3ff))
* **ci:** stop a docs upload from blocking the release images ([#23](https://github.com/TorrentDrive/backend/issues/23)) ([8119154](https://github.com/TorrentDrive/backend/commit/8119154d10d17ad261ce22db072da49bb7ba52a0))

## [2.0.0](https://github.com/TorrentDrive/backend/compare/v1.2.0...v2.0.0) (2026-09-03)


### ⚠ BREAKING CHANGES

* rename TorreClou to TorrentDrive throughout ([#20](https://github.com/TorrentDrive/backend/issues/20))

### Refactoring

* rename TorreClou to TorrentDrive throughout ([#20](https://github.com/TorrentDrive/backend/issues/20)) ([95545e0](https://github.com/TorrentDrive/backend/commit/95545e09bd2cd720db23a220a8279e312eb0fde0))

## [1.2.0](https://github.com/TorrentDrive/backend/compare/v1.1.1...v1.2.0) (2026-09-03)


### Features

* generate the configuration reference from the code ([#17](https://github.com/TorrentDrive/backend/issues/17)) ([53842f9](https://github.com/TorrentDrive/backend/commit/53842f9db8d9071782e902818bb81758011b0f42))


### Bug Fixes

* **deps:** resolve vulnerable OpenTelemetry and Snappier packages ([#19](https://github.com/TorrentDrive/backend/issues/19)) ([fd979e8](https://github.com/TorrentDrive/backend/commit/fd979e8ef1d4f967604b0095338b71d7411797c0))

## [1.1.1](https://github.com/TorrentDrive/backend/compare/v1.1.0...v1.1.1) (2026-09-03)


### Bug Fixes

* **ci:** actually apply semver tags on an automated release ([#14](https://github.com/TorrentDrive/backend/issues/14)) ([708210b](https://github.com/TorrentDrive/backend/commit/708210b5e94a672a042fcf2dd19605f47134aae6))
* **ci:** lowercase the image reference when applying semver tags ([#16](https://github.com/TorrentDrive/backend/issues/16)) ([c14eab7](https://github.com/TorrentDrive/backend/commit/c14eab7b389eb77347e2c2de410d90b3d95b2cc6))

## [1.1.0](https://github.com/TorrentDrive/backend/compare/v1.0.0...v1.1.0) (2026-09-03)


### Features

* real versions, safe rollback, and an actual release process ([#12](https://github.com/TorrentDrive/backend/issues/12)) ([5bef056](https://github.com/TorrentDrive/backend/commit/5bef05644ff7162f04443a276e797e9af88e9257))
