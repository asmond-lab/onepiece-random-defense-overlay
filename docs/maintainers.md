# Maintainers

RandyPick is maintained by the  RandyPick Studio team.

| Maintainer | Role  | Focus |
|---|---|---|
| [@asmond-lab](https ://github.com/asmond-lab) | Lead maintainer |  Recommendation engine, game readers, release  pipeline |
| [@Anlse](https://github.com/Anl se) | Maintainer | Build/release verification , data bundle review, docs |

## How we work
 
- Every user-facing change ships with a test  gate run; failures are kept as receipts unde r `artifacts/`.
- Release candidates are publ ished to the signed `test` channel first and  only promoted after byte/hash/PE verification .
- The overlay is read-only with respect to  the game: it never writes to game memory, inj ects code, or sends input.

## Contributing

 Issues and discussions are welcome in Korean  or English. For data corrections (unit recipe s, map bundles),
please include the map versi on and the source you are citing so the chang e can be reproduced offline.













 