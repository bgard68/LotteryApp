# Security policy

## Reporting a vulnerability

Please report suspected vulnerabilities privately via
[GitHub private vulnerability reporting](https://github.com/bgard68/LotteryApp/security/advisories/new)
- do not open a public issue for security problems.

You can expect an acknowledgment within a few days. Please include steps to
reproduce and the potential impact.

## Posture

What is enabled and why - plus the audit behind it - is documented in
[docs/SECURITY-POSTURE.md](docs/SECURITY-POSTURE.md).

## Scope

This is a portfolio application with no authentication, no user accounts, and
no sensitive data: the database contains only public lottery results, and the
repository is designed to contain **zero secrets** (see the
[main README](README.md#securing-github)). Reports about dependency
vulnerabilities are usually already covered by Dependabot alerts and CodeQL,
but reports of flaws in the application code itself are welcome.

## Supported versions

Only the latest commit on `main` is supported.
