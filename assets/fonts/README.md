# Bundled fonts

§27Y.2 の Bundled Font Chain である。`font-manifest.json` の `sha256` を Renderer が起動時に検証し、
不一致または欠落で Card Rendering を Fail Closed する。Runtime Download はしない。

| Role | Family | File | Upstream | Release | License |
|---|---|---|---|---|---|
| general | BIZ UDPGothic Bold | `BIZUDPGothic-Bold.ttf` | `googlefonts/morisawa-biz-ud-gothic` | v1.051 | OFL-1.1 |
| mono | IBM Plex Mono SemiBold | `IBMPlexMono-SemiBold.otf` | `IBM/plex` | `@ibm/plex-mono@2.5.0` | OFL-1.1 |
| fallback | Noto Sans CJK JP Bold | `NotoSansCJKjp-Bold.otf` | `notofonts/noto-cjk` | Sans2.004 | OFL-1.1 |

3件とも SIL Open Font License 1.1 である。条項に従い各ライセンス全文を
`LICENSE-BIZUDPGothic.txt` `LICENSE-IBMPlexMono.txt` `LICENSE-NotoSansCJK.txt` として同梱する。

BIZ UDPGothic と IBM Plex と Noto は Reserved Font Name を持つ。**改変版へ同じ名前を使ってはならない。**
本リポジトリは3件を未改変のまま再配布する。フォント単体の販売はしない。
