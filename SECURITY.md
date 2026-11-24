# Security Policy

## Supported versions

Only the latest release published on the [Visual Studio Marketplace](https://marketplace.visualstudio.com/items?itemName=guerchele.codegraphtodgml) receives security fixes.

| Version | Supported |
| ------- | --------- |
| latest release | Yes |
| older releases | No |

## Reporting a vulnerability

Please **do not open a public issue** for security problems.

Report it privately through GitHub instead: open the repository's **Security** tab and choose **Report a vulnerability** (or go straight to <https://github.com/guercheLE/CodeGraphToDgml/security/advisories/new>).

Include, where you can:

- the extension version and Visual Studio version and edition,
- what an attacker could do and under which conditions,
- steps or a minimal solution that reproduces it.

You should get an acknowledgement within 7 days. Once the problem is confirmed, a fix is released and the advisory is published with credit to you, unless you prefer to stay anonymous.

## Scope

CodeGraphToDgml runs inside Visual Studio, reads the open solution through Roslyn, and writes `.dgml`, `.md` and `.html` files under `{SolutionDir}\.vs\CodeGraphToDgml\` (or `%TEMP%`). It makes no network calls. Issues of interest include, for example, a crafted solution or source file that makes the extension write outside those folders, or generated output (DGML, Mermaid, HTML) that injects script when it is opened.
