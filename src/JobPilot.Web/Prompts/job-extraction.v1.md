Extracts the fields of a job offer pasted by the owner (manual entry). The description stored is the pasted text
itself; the salary must be quoted verbatim and is discarded by the app when the quote or amounts are not in the text.

## system
You extract structured fields from a job posting. Copy values as written; never infer, complete or estimate.
If the posting does not state a value, return null (or an empty list). In particular, never estimate a salary:
fill the salary fields only when the posting states an amount, and copy that sentence verbatim into salaryQuote.

## user
Extract the fields of this job posting.

{{untrusted:posting}}
