# ConnectionsSolver

A semantic-similarity helper for the NYT *Connections* puzzle. Given the 16
puzzle words, it ranks candidate groups and full 4x4 partitions by how
semantically related the words are, proposes labels for each group, and runs
an **interactive feedback loop** so you can mark guesses correct / wrong /
one-away and have the next suggestion refined.

It is **deterministic** - no LLMs, no randomness, no network calls at runtime.
The "intelligence" comes from static data files you download once (GloVe
vectors; optionally Norvig bigram counts).

## Strengths and limitations

The solver is built on cosine similarity of GloVe vectors plus a handful of
specialized passes. That gives strong results on the "easy" and "medium"
Connections categories (synonyms, members of a clear semantic class) and
acceptable results on harder semantic ones. It can also catch some
suffix/prefix-based wordplay (e.g. words ending in synonyms of ASAP).

It will **not** reliably solve "purple"-style categories that depend on
homophones, palindromes, or cultural compound nouns that aren't in the
bigram corpus. Use it as a starting point, not an oracle.

## One-time setup

### Required: GloVe vectors

Download `glove.6B.zip` from <https://nlp.stanford.edu/data/glove.6B.zip>
(~822 MB zipped). Drop the zip into your **Downloads** folder — on first
run the tool will auto-extract `glove.6B.300d.txt` into
`<Downloads>\glove.6B\` and use it from there.

If you prefer to put the file elsewhere, extract `glove.6B.300d.txt` (~1 GB
unzipped) wherever you like and either pass `--embeddings <path>` per run
or set the `GLOVE_PATH` environment variable. **Don't commit it to the
repo** — it's over GitHub's 100 MB file limit.

Lookup order: `--embeddings <path>` → `GLOVE_PATH` →
`<Downloads>\glove.6B\glove.6B.300d.txt` → `<Downloads>\glove.6B.zip`
(auto-extracts).

### Optional: Norvig bigram counts

Phase-5 phrase-pattern detection (the `[P]` section) reads a word-bigram
counts file. Download `count_2w.txt` from
<https://norvig.com/ngrams/count_2w.txt> (~5 MB) into your **Downloads**
folder. Without this file, the `[P]` section is silently skipped — every
other section still works.

Lookup order: `--bigrams <path>` → `BIGRAMS_PATH` →
`<Downloads>\count_2w.txt`.

On first load each file is parsed (slow, ~30 s for GloVe; ~1 s for bigrams)
and a sibling `.cache` binary file is written. Subsequent loads use the
cache (~2 s for GloVe). Caches auto-invalidate when the source `.txt`
becomes newer.

## Build / Run

```
dotnet build .\ConnectionsSolver\ConnectionsSolver.csproj
dotnet run --project .\ConnectionsSolver\ConnectionsSolver.csproj -- .\ConnectionsSolver\sample-puzzle.txt
```

Or with stdin (reads up to 16 non-comment, non-blank lines, then proceeds):

```
dotnet run --project .\ConnectionsSolver\ConnectionsSolver.csproj
# (paste 16 words, one per line)
```

Input rules:

* Exactly 16 entries.
* One entry per line. Entries may be multi-word (e.g. `FREE LOVE`).
* Blank lines and lines starting with `#` are ignored.
* All entries are lowercased before lookup. Multi-word entries are
  represented as the centroid of their constituent tokens' vectors.
* Any entry not present in GloVe is kept in play as a *missing* word —
  you can still mark groups containing it as correct via label feedback.

## Interactive feedback loop

After the first set of suggestions, the tool prompts:

```
What did you try?  <label or 4 words> <yes | no | off-by-one>
  e.g.  A1 yes   |   A2v1 no   |   N1s3 yes   |   X1s2 off   |   W1 yes   |   P1 yes   |   piano violin guitar drums off
  ('with X[, Y[, Z]]' asks what completes a pinned 1-3 word set; 'skip' to recompute, 'quit' to exit)
```

Verdicts:
* `yes` / `y` / `correct` — remove those 4 words from play; the next round
  works with the remaining 12.
* `no` / `n` / `wrong` — forbid that exact 4-set so it won't be re-suggested.
* `off` / `near` / `off-by-one` — forbid the 4-set AND track it; the next
  round will offer the four single-word-swap follow-ups as `[N1s1]..[N1s4]`.

Identifiers can be a label (`A1`, `A2v1`, `N1s3`, `X1s2`, `W1`, `P1`, `C1`) or
4 words / multi-word entries (comma-separated if any contain spaces).

### "What goes with these?" query

At the same prompt, you can ask for completions of a partial group you're
sure about. Useful when you have a strong intuition that 1-3 words belong
together but aren't sure which other word(s) complete the four:

```
with hero, hoagie, sub        -> ranks the top 5 fourth words (labeled C1..C5)
with hero, sub                -> ranks the top 5 (3rd, 4th) pairs
with hero                     -> ranks the top 5 (2nd, 3rd, 4th) triples
with free love, hippie        -> multi-word entries comma-separated
```

The query is stateless — it doesn't mark anything solved or forbid anything.
Each result is registered as a one-shot label `C1..C5` for the duration of
the current prompt, so you can follow up with `C2 yes` once you decide.

Completions are scored through the same Phase 2 + Phase 6 pipeline as the
regular anchors, so they incorporate leftover-partition and label-overlap
signals on top of raw pairwise similarity.

## Label scheme

| Label | Meaning |
| --- | --- |
| `A1`..`A4` | Anchor groups of 4 (disjoint top picks by combined score) |
| `A1v1`..`A1v3` | Single-word-swap variants of an anchor |
| `B1`..`B4` | Anchor groups of 3 (each with possible 4th word suggestions) |
| `N1`, `N1s1`..`N1s4` | Near-miss follow-ups after a one-away verdict |
| `X1`, `X1s1`..`X1s5` | Dense-cluster warnings ("5+ words look related — pick carefully") |
| `W1`..`W5` | Suffix/prefix wordplay (e.g. words ending in synonyms of ASAP) |
| `P1`..`P5` | Bigram phrase patterns (4 entries sharing a common modifier — needs `count_2w.txt`) |
| `C1`..`C5` | Completions from a `with X[, Y[, Z]]` query (one-shot, per prompt) |

## Options

| Flag | Default | Meaning |
| --- | --- | --- |
| `-e`, `--embeddings <path>` | `$GLOVE_PATH` or `<Downloads>` fallback | Path to GloVe text file |
| `-b`, `--bigrams <path>` | `$BIGRAMS_PATH` or `<Downloads>` fallback | Path to Norvig bigram counts |
| `--no-bigrams` | | Skip bigram loading even if a file is present |
| `--anchors N` | 4 | Disjoint top groups to show |
| `--variants N` | 3 | Single-word-swap variants per anchor |
| `--top-partitions N` | 5 | Full partitions to show |
| `--label-vocab N` | 50000 | Top-N GloVe words used as label candidates |
| `--labels N` | 3 | Labels per group |
| `--fourth N` | 3 | "Possible 4th word" suggestions per triplet |
| `--rerank-alpha F` | 0.5 | Weight of leftover-partition score in candidate reranking |
| `--rerank-top N` | 50 | How many top candidates participate in leftover reranking |
| `--label-rerank-beta F` | 1.5 | Phase 6 centroid-to-label weight; 0 (or `--no-label-rerank`) disables |
| `--label-rerank-top N` | 200 | How many top candidates participate in label-overlap reranking |
| `--no-label-rerank` | | Shortcut for `--label-rerank-beta 0` |
| `-h`, `--help` | | Show usage |

## How it works (high level)

1. **Lookup.** Each entry is mapped to its 300-dim GloVe vector (multi-word
   entries are the centroid of their token vectors) and normalized.
2. **Similarity matrix.** A pairwise cosine matrix is built once per round.
3. **Candidate groups.** All 3- and 4-subsets of the active set are scored
   by average pairwise cosine.
4. **Leftover-partition reranking.** Top-N 4-set candidates are rescored by
   how well the *remaining* 12 words still partition into 3 clean groups.
   Candidates that "steal" words from another plausible group get demoted.
5. **Label-overlap reranking.** Same top-N candidates get a second pass: each
   candidate's centroid is compared against the top-5000 vocabulary words,
   and the mean of the top-3 sims becomes a "single dominant theme" signal
   added (with weight `--label-rerank-beta`) on top of the previous score.
   Promotes groups like `{grinder, hero, hoagie, sub}` (weak internal cosine
   but a strong "sandwich" centroid) over generic-but-tight neighbors.
6. **Anchor selection.** Top disjoint 4-sets become `A1..A4`; size-3
   anchors become `B1..B4`.
7. **Specialized passes.**
   - *Dense clusters* (`X`) — 5+ words that all sit in each other's top-K
     neighborhood; surfaced as a "pick carefully" warning.
   - *Wordplay* (`W`) — suffix/prefix splits where the affix is itself a
     real English word and the 4 affixes cluster tightly in GloVe.
   - *Phrase patterns* (`P`) — 4 entries that share a common bigram
     modifier on the same side (needs `count_2w.txt`).
8. **Labels.** For each shown group, the top-K vocab words nearest the
   centroid are returned, with input words and basic morphological
   variants excluded.

## Roadmap

* Phase 6 (done): label-overlap reranking. Adds a centroid-to-label-vocab
  signal on top of the Phase 2 combined score, weighted by `--label-rerank-beta`
  (default 1.5). Composes with — not replaces — Phase 2. On the 5 test
  puzzles, this lifted green/yellow exact-match rate from 2/10 to 3/10
  (puzzle 4 green: 1960s counterculture went from 2/4 → 4/4). Doesn't
  rescue cases where the correct 4-set isn't even in the top-200
  candidates by combined score — those need sense disambiguation or
  multi-sense embeddings.
* Phase-5 bigram data: swap Norvig (286K bigrams) for a larger source
  (Google Books Ngrams, COCA, Wikipedia) or a phrase dictionary
  (WordNet multi-word entries, Wiktionary compound terms) so the `[P]`
  section actually fires on real puzzles.
* Sense-aware embeddings: try sense2vec or ConceptNet Numberbatch via the
  existing `IWordEmbeddings` interface to break polysemy ties (HERO/SUB
  as sandwich, FIDDLE as verb, DECK/PUNCH/SLUG/SOCK as slang for "hit").
