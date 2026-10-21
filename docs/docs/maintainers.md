# Maintainers

RandyPick is maintained by the RandyPick Studio team.

| Maintainer | Role | Focus |
|---|---|---|
| [@asmond-lab](https://github.com/asmond-lab) | Lead maintainer | Recommendation engine, game readers, release pipeline |
| [@Anlse](https://github.com/Anlse) | Maintainer | Build/release verification, data bundle review, docs |

## How we work

- Every user-facing change ships with a test gate run; failures are kept as receipts under `artifacts/`.
- Release candidates are published to the signed `test` channel first and only promoted after byte/hash/PE verification.
- The overlay is read-only with respect to the game: it never writes to game memory, injects code, or sends input.

## Contributing

Issues and discussions are welcome in Korean or English. For data corrections (unit recipes, map bundles),
please include the map version and the source you are citing so the change can be reproduced offline.













