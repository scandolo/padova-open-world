# Prato della Valle evidence ledger (prato)

Location: 11.8765 E, 45.3985 N → game x 203.0, z −1023.0 (fountain centroid). Inside WorldPlan extent: yes. Survey records: canal `AB_CDA:265, 269, 507, 511, 512, 513, 582, 584`, bridges `PONTE:414, 421, 422, 427`, fountain `MN_EDI_NOVOL:2951`. OSM artwork/obelisk nodes: 88 (snapshot 2026-09-29T10:10Z). Frozen in `sources.json["prato"]`.

## Signature features
- Elliptical Isola Memmia ringed by a canal lined with a double ring of statues on pedestals.
- Four stone bridges with interlaced-ring balustrades.
- Obelisks on the east and west bridge pedestals.
- Central fountain where the four island avenues meet.
- Low stone parapets along both canal edges with water well below the walkway.

## Dimension table
| Element | Value | Tier | Source | Derivation |
| --- | --- | --- | --- | --- |
| Canal outline (annulus) | exact | survey | AB_CDA (8 polygons) | union |
| Water surface | −3.695 m local (about 0.7 m below fitted ground) | survey | AB_CDA polygon Z | median of edge Z − 15 |
| Bridge outlines | exact | survey | PONTE:414, 421, 422, 427 | copied |
| Bridge crowns | 0.49–0.76 m above anchor ground | survey | PONTE polygon Z | max Z − 15 |
| Bridge spans | 12.5–12.7 m | survey | canal rings intersected with bridge axes | computed |
| Fountain basin | r ≈ 5.7 m | survey | MN_EDI_NOVOL:2951 | outline; bowl and jet are typology |
| Island area | about 20,000 m² | published (survey agrees) | Wikipedia it | check only |
| Statue positions | 85 of 88 OSM nodes | mapped | OSM tourism=artwork ref 1–88 | snapped to the parapet centre line, mean 1.06 m, max 2.67 m |
| Pedestal / statue height | 2.24 m / about 2.3 m | photo | prato2.jpg | ratio to a walker |
| Obelisk | about 6.6 m with pedestal | photo | prato1.jpg | ratio to the balustrade |
| Parapet | 0.55 m high, 0.7 m wide | photo | prato2.jpg | typology |
| Canal bed | 1 m below water | typology | none | hidden, gameplay only |

## Counts
| Item | Count | Sources |
| --- | --- | --- |
| Statues | 78 (40 outer + 38 inner) | Wikipedia it; OSM numbering |
| Numbered positions | 88 (44 + 44) | Commons plan *Prato della Valle-Pianta Statue.jpg*; OSM refs |
| Obelisks | 8 at 11, 12, 33, 34, 55, 56, 77, 78 | Wikipedia it statue list; OSM names |
| Empty pedestals | 45, 88 | Wikipedia it statue list |
| Bridges | 4 | DBT PONTE; Wikipedia it |

## Conflicts and decisions
- OSM ref 30 lies 13.6 m from the canal, so it is treated as an outlier and interpolated. Refs 49 and 50 are missing from OSM and are also interpolated.
- Statue facing (towards the canal) is not documented and is typology.
- The canal depth is not surveyed. The bed is hidden 1 m below the water.

## Reference photos (drawing reference only)
- *Prato della Valle - Bridges 01.jpg*, *Prato della Valle in Padua.JPG*, *Prato della Valle-Pianta Statue.jpg* (Wikimedia Commons, individual licences) in `.context/landmarks-ref/prato1-3.jpg`.
