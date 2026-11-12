# Basilica di Sant'Antonio (il Santo) evidence ledger (santo)

Location: 11.8807 E, 45.4015 N → game x 527.7, z −679.0 (crossing drum UN_VOL:30736). Inside WorldPlan extent: yes (outside the detailed core). Survey units: 30 `UN_VOL` records (list in `Tools/landmarks/extract_sources.py` → `SANTO_UNITS`) plus `MN_UVOL:10632` (Gattamelata). Frozen in `sources.json["santo"]`.

## Signature features
- Seven lead domes in a Latin-cross layout, each with a cross, plus the Treasury rotunda dome.
- Conical dome over the crossing with an open lantern, a spire and a gilded weather-vane angel.
- Two slender octagonal campanili with balustraded galleries and striped cone spires.
- Brick walls with Romanesque corbel tables (archetti pensili), Gothic lancet windows and an ambulatory.
- West front: five recessed portal arches, a loggia gallery with balustrades, rose window and two biforas, low gable with a cross.
- Donatello's Gattamelata equestrian monument on its pedestal in the piazza.

## Dimension table
| Element | Value | Tier | Source | Derivation |
| --- | --- | --- | --- | --- |
| All footprints, drum holes, bases | exact outlines | survey | DBT UN_VOL (30 units) | copied from WorldPlan |
| Body eave | 29.22 m above base | survey | UN_VOL:30694 QGR/AV | QGR − 15 |
| Drum eaves (dome springing) | 28.3–30.1 m | survey | UN_VOL:30619, 26182, 30736, 30849, 23960, 30759, 25375 | QGR − 15 |
| Campanile eaves | 56.08 / 56.13 m | survey | UN_VOL:30659, 30830 | QGR − 15 |
| Campanile total height | 68 m | published | Wikipedia it, Basilica di Sant'Antonio | spire tip = base + 68 |
| Façade height | about 28 m | published | Wikipedia it | gable apex = base + 28 |
| Façade width | 37 m | survey (published agrees: about 37 m) | UN_VOL:30788 west edge | measured along the facade line |
| Loggia bays top | 19.58 m | survey | UN_VOL:85827–85832, 30718 | QGR − 15 |
| Façade turret | 39.23 m | survey | UN_VOL:23928 | QGR − 15, conical cap is typology |
| Drum radius | 8.2–8.3 m | survey | drum unit areas | r = √(area/π) |
| Hemispherical dome rise | 0.92 × (r − 0.3) | photo | santo2.jpg, santo3.jpg | ratio of dome rise to drum width |
| Cone height | 14 m | photo | santo3.jpg | 0.86 × drum Ø |
| Lantern / spire / angel | 3.6 m / 8.2 m / 2.2 m | photo | santo2.jpg, santo3.jpg | ratios to drum Ø; total about 55 m, unmeasured |
| Gattamelata base / statue | 7.8 × 4.1 m / 3.4 × 3.9 m | published | Wikipedia it, Monumento equestre al Gattamelata | footprint from MN_UVOL:10632 |
| Ornament spacing (archlets 0.95 m, windows, pilasters) | typology | typology | reference photographs | game vocabulary |

## Counts
| Item | Count | Sources |
| --- | --- | --- |
| Domes | 8 (7 church + Treasury) | Wikipedia it; DBT circular units |
| Campanili | 2 octagonal | Wikipedia it; DBT 30659/30830 |
| Portal arches | 5 | Wikipedia it ("cinque arcate rientranti"); santo1.jpg |

## Conflicts and decisions
- The body eave (29.2 m) is almost equal to the drum eaves, and some drums are lower. The survey is kept, so the domes rise from a nearly flat lead roof instead of from pitched roofs as in photographs.
- The front block eave is 25.17 m but the published façade height is about 28 m. The gable therefore rises only from the eave to 28 m, which makes it flatter than in reality.
- The Gattamelata MN_UVOL height is 18.7 m, while the published dimensions total about 11 m. The published proportions are used and the discrepancy is recorded. The horse's heading (away from the basilica) is approximate.
- QCO ridge values are all 0, so nothing above an eave is surveyed.

## Reference photos (drawing reference only)
- *Basilica Di Sant'Antonio di Padova - Facciata.jpg*, *Basilica di Sant'Antonio di Padova - Cupole.jpg*, *Basilica di Sant'Antonio di Padova, August 2024.jpg* (Wikimedia Commons, individual licences) in `.context/landmarks-ref/santo1-3.jpg`.
