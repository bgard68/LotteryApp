/**
 * Architecture rules for the import graph, run by `npm run depcruise` in CI.
 *
 * ARCHITECTURE.md draws the layer picture ("nothing points outward"); these
 * are the invariants a machine can hold without chasing every refactor. All
 * three are `error`: a finding fails the job and names both ends of the edge.
 */
module.exports = {
  forbidden: [
    {
      name: 'no-circular',
      severity: 'error',
      comment:
        'Import cycles make load order matter and refactors risky - and with ' +
        'standalone components plus signal stores there is never a reason for one. ' +
        'The fix is to extract the shared piece, not to re-point the arrow.',
      from: {},
      to: { circular: true },
    },
    {
      name: 'not-to-test',
      severity: 'error',
      comment:
        'Production code must not import spec files or test doubles (the fake-* ' +
        'in-memory implementations). A fake wired into the app would compile, ' +
        'render, and quietly serve made-up draws - exactly the kind of bug only ' +
        'a human squinting at an import list would catch. Specs and fakes may ' +
        'use each other freely.',
      from: { pathNot: ['\\.spec\\.ts$', '(^|/)fake-[^/]+\\.ts$'] },
      to: { path: ['\\.spec\\.ts$', '(^|/)fake-[^/]+\\.ts$'] },
    },
    {
      name: 'not-to-unresolvable',
      severity: 'error',
      comment:
        'An import that does not resolve to a file or an installed package is a ' +
        'broken edge: a typo, a deleted module, or a dependency missing from ' +
        'package.json. The compiler catches most of these - this catches the ' +
        'rest (assets, raw .mjs, anything outside the TS program).',
      from: {},
      to: { couldNotResolve: true },
    },
  ],
  options: {
    // Resolve into node_modules so "could not resolve" stays meaningful, but
    // do not cruise third-party internals - their graphs are not ours to lint.
    doNotFollow: { path: ['node_modules'] },
    tsConfig: { fileName: 'tsconfig.json' },
    // Count type-only imports as dependencies. `import type` is still
    // coupling, and the generated schema.d.ts arrives exactly that way.
    tsPreCompilationDeps: true,
    // Angular ships ESM-only packages addressed through package.json
    // `exports` maps; without these fields the resolver predates that world
    // and every `@angular/*` import reads as unresolvable.
    enhancedResolveOptions: {
      exportsFields: ['exports'],
      conditionNames: ['import', 'require', 'node', 'default', 'types'],
      mainFields: ['module', 'main', 'types', 'typings'],
    },
  },
};
