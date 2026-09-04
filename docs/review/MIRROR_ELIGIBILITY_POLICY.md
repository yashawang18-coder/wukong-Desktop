# Horizontal mirror eligibility audit

The owner requested a repository-wide check for reusable left/right mirrors. The generated
[`MIRROR_ELIGIBILITY_AUDIT.json`](MIRROR_ELIGIBILITY_AUDIT.json) is deliberately fail closed.

## Result for this branch

- 23 action packages inspected.
- 0 packages are currently eligible for automatic full-frame horizontal mirroring.
- 0 mirrored frames are runtime integrated.
- No approved PNG was edited in place.

This is not a missing implementation. The existing packages do not declare `mirror_safe=true`,
and the current full-frame art contains at least one of these disqualifiers:

- native opposite-direction art already exists (car ride and broom flight);
- curled-tail direction or asymmetric identity details would be reversed;
- the raised paw, turn direction, steering direction, prop, lighting, or effect direction is semantic;
- the source is expired/rejected; or
- the source is already approved and therefore requires a versioned derivative plus new owner QA.

## Runtime rule

Runtime code must never infer mirror safety from a filename or missing direction. A future mirrored
derivative requires all of the following:

1. the source manifest explicitly declares `mirror_safe=true` for the exact sequence;
2. the sequence has no native opposite-direction artwork;
3. handed action semantics, props, text, lighting, curled tail, and identity asymmetry are absent;
4. the derivative records source frame hashes and `transform=flip_horizontal`;
5. the derivative remains `runtime_use=false` until owner visual QA and Windows renderer QA pass.

Head-local edits are a separate production operation and are not treated as full-frame mirrors.
The new car road-gaze and side-body/front-head candidates therefore require their own frames and QA.
