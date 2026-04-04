---
name: CanfarDesktop Search Architecture
description: Core search pipeline -- ADQLBuilder generates WHERE clauses from SearchFormState, TAPService executes via CADC TAP, results are SearchResultRow dictionaries with client-side filter/sort/pagination in SearchViewModel
type: project
---

Search pipeline: SearchFormState -> ADQLBuilder.Build() -> TAPService.ExecuteQueryAsync (POST to TAP sync endpoint, CSV response) -> SearchResults (columns + rows of Dictionary<string,string>).

**Why:** Understanding this flow is essential for any feature that modifies query construction or results refinement.

**How to apply:**
- ADQLBuilder.Build() returns a complete SELECT...FROM...WHERE query. The WHERE clause is built by joining multiple clause strings with AND.
- ResultFilter does client-side Contains matching per column (text-only, AND logic). ResultSorter does smart numeric/string sort.
- SearchViewModel holds both the full result set (Results) and a filtered/sorted cache (_filteredRowsCache). Pagination is client-side over the filtered set.
- The Results Pivot tab has: toolbar row, sticky header row (synced horizontal scroll), per-column filter TextBox row, scrollable data rows, pagination bar.
- CellFormatter.CleanKey() normalizes column headers to keys like "ra(j20000)", "inttime", "callev" etc.
- Columns are defined by the SELECT in ADQLBuilder with aliases like "RA (J2000.0)", "Int. Time", "Cal. Lev."
- The app uses CommunityToolkit.Mvvm (ObservableObject, RelayCommand, ObservableProperty).
- SearchPage.xaml uses a Pivot with three PivotItems: Search Form, Results, ADQL Editor.
